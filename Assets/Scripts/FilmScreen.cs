using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

// Owner: Claude (film / in-game screenings)
// Plays one film full screen over everything, then calls back. The films live in
// Assets/StreamingAssets/Films (a URL, so the WebGL build streams them too).
//   FilmScreen.Play("map_intro.mp4", () => { ... });
// Letterboxed on black, runs on unscaled time (the game is usually frozen underneath), and the
// game's music steps aside while it plays (SoundDirector reads FilmScreen.Playing).
// After a moment a "Space / click: skip" tag shows; a key or click then skips. If the file is
// missing or can't play, it ends at once, so a story step never hangs on a film.
public class FilmScreen : MonoBehaviour
{
    public const string Folder = "Films";
    const float SkipAfter = 1.2f;        // seconds before a key can skip (a held key from play must not)
    const float PrepareTimeout = 10f;    // seconds to wait for the file to load before giving up
    const int SortingOrder = 600;        // above the HUD and the dialogue box (520)

    public static bool Playing => current != null;
    static FilmScreen current;

    VideoPlayer player;
    RenderTexture target;
    RawImage picture;
    AspectRatioFitter fitter;
    CanvasGroup group;
    GameObject skipTag;
    Action done;
    float started;
    bool prepared, finished;

    public static FilmScreen Play(string file, Action onDone)
    {
        if (current != null) current.End();
        var go = new GameObject("FilmScreen");
        var screen = go.AddComponent<FilmScreen>();
        screen.Begin(file, onDone);
        return screen;
    }

    // A film's URL under StreamingAssets (works in the editor, desktop and WebGL)
    public static string UrlFor(string file)
    {
        string root = Application.streamingAssetsPath;
        string path = root.TrimEnd('/', '\\') + "/" + Folder + "/" + file;
        return path.Contains("://") ? path : "file://" + path;
    }

    void Begin(string file, Action onDone)
    {
        current = this;
        done = onDone;
        started = Time.unscaledTime;
        BuildScreen();

        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.source = VideoSource.Url;
        player.url = UrlFor(file);
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.isLooping = false;
        player.skipOnDrop = true;
        player.audioOutputMode = VideoAudioOutputMode.Direct;
        player.prepareCompleted += OnPrepared;
        player.loopPointReached += _ => End();
        player.errorReceived += (_, message) => { Debug.LogWarning("FilmScreen: " + message); End(); };
        player.Prepare();
    }

    void OnPrepared(VideoPlayer vp)
    {
        if (finished) return;
        prepared = true;
        int w = (int)Mathf.Max(16, vp.width), h = (int)Mathf.Max(16, vp.height);
        target = new RenderTexture(w, h, 0);
        vp.targetTexture = target;
        picture.texture = target;
        picture.color = Color.white;
        fitter.aspectRatio = (float)w / h;
        float volume = SoundDirector.Muted ? 0f : SoundDirector.GetVolume(SoundDirector.Volume.Master);
        for (ushort i = 0; i < vp.audioTrackCount; i++) vp.SetDirectAudioVolume(i, volume);
        vp.Play();
    }

    void Update()
    {
        if (finished) return;
        float t = Time.unscaledTime - started;
        group.alpha = Mathf.Clamp01(t / 0.25f);
        if (!prepared && t > PrepareTimeout) { Debug.LogWarning("FilmScreen: the film didn't load in time"); End(); return; }

        bool canSkip = t > SkipAfter;
        if (skipTag.activeSelf != canSkip) skipTag.SetActive(canSkip);
        if (!canSkip || DeveloperConsole.CapturesInput) return;
        bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (pressed) End();
    }

    void End()
    {
        if (finished) return;
        finished = true;
        if (current == this) current = null;
        if (player != null) player.Stop();
        Action callback = done;
        done = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    void OnDestroy()
    {
        if (current == this) current = null;
        if (target != null) { target.Release(); Destroy(target); }
    }

    void BuildScreen()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;   // nothing under the film takes clicks

        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(transform, false);
        Stretch(black.GetComponent<RectTransform>());
        black.GetComponent<Image>().color = Color.black;

        var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        pic.transform.SetParent(black.transform, false);
        Stretch(pic.GetComponent<RectTransform>());
        picture = pic.GetComponent<RawImage>();
        picture.color = Color.black;   // until the first frame
        fitter = pic.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;

        skipTag = new GameObject("Skip", typeof(RectTransform));
        skipTag.transform.SetParent(transform, false);
        var rect = skipTag.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-36f, 28f);
        rect.sizeDelta = new Vector2(420f, 44f);
        var label = skipTag.AddComponent<TMPro.TextMeshProUGUI>();
        label.text = "Space / click: skip";
        label.fontSize = 26f;
        label.alignment = TMPro.TextAlignmentOptions.BottomRight;
        label.color = new Color(1f, 1f, 1f, 0.55f);
        skipTag.SetActive(false);
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
