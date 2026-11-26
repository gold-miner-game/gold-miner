using UnityEngine;

// Art + UI: the Sound card, opened from the pause popup ("Sound") and the main menu ("Sound"). One
// stitched felt card in the papercraft look:
//   Music / Sound effects / Menu sounds - a painted icon, a dark felt track with a running stitch, a gold paper fill and
//     a paper-tag handle, and the level in whole percent (SoundDirector.Get/SetVolume, 0..1). Changes
//     apply at once; on release a slider is saved (PlayerPrefs.Save, so WebGL keeps it) and the effects
//     and menu sliders play a preview (Sfx.Play("coin_small") / Sfx.Ui("ui_click")). Music is heard live.
//   Mute all - a paper toggle with the speaker / crossed speaker (SoundDirector.Muted; levels are kept).
//   Now playing - the record, the song's title and style, and back / next (SoundDirector.PreviousSong /
//     NextSong), greyed while a store pick or survival's song plays (SoundDirector.CanSkip).
// IMGUI buttons click with Sfx.Ui("ui_click"); the sliders don't (they preview on release instead).
// Art: Resources/Art/Music/{Icon_Note,Icon_Bell,Icon_Hand,Icon_Speaker,Icon_Mute,Record}, UI/FeltPanel,
// UI/PaperCard; Fredoka (PaperType.Body), the display face for the title.
public class SoundPanel : MonoBehaviour
{
    public static bool IsOpen => instance != null;
    static SoundPanel instance;

    System.Action onClose;
    System.Func<bool> closeWhen;
    readonly bool[] dragging = new bool[3];
    static readonly SoundDirector.Volume[] Channels = { SoundDirector.Volume.Music, SoundDirector.Volume.Sfx, SoundDirector.Volume.Ui };
    static readonly string[] Names = { "Music", "Sound effects", "Menu sounds" };
    static readonly string[] Icons = { "Music/Icon_Note", "Music/Icon_Bell", "Music/Icon_Hand" };

    GUIStyle card, paper, title, label, small, percent, ink, inkSmall, thumb, emptyStyle;
    Texture[] icons;
    Texture speaker, muted, record, fill;
    static readonly Color Gold = new Color(1f, 0.84f, 0.42f), Cream = new Color(0.97f, 0.9f, 0.74f),
                          Ink = new Color(0.24f, 0.16f, 0.1f), Umber = new Color(0.48f, 0.34f, 0.22f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    // closeWhen: close by itself when this turns true (the pause popup closed under it)
    public static void Show(System.Action onClose = null, System.Func<bool> closeWhen = null)
    {
        if (instance != null) return;
        var go = new GameObject("Sound Panel");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundPanel>();
        instance.onClose = onClose;
        instance.closeWhen = closeWhen;
    }

    // A drag cut short (the window lost focus, the card closed): keep the level, no stray preview later
    void EndDrags() { for (int i = 0; i < dragging.Length; i++) dragging[i] = false; }
    void OnApplicationFocus(bool focused) { if (!focused) { EndDrags(); PlayerPrefs.Save(); } }

    // A scene change (starting the game, leaving a level) always closes the card
    void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneChanged; }
    void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneChanged; }
    void SceneChanged(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single && instance == this) Close();
    }

    public static void Close()
    {
        if (instance == null) return;
        SoundPanel p = instance;
        instance = null;
        p.EndDrags();
        PlayerPrefs.Save();
        p.onClose?.Invoke();
        Destroy(p.gameObject);
    }

    void Update()
    {
        if (closeWhen != null && closeWhen()) { Close(); return; }
        if (DeveloperConsole.CapturesInput) return;   // the console owns the keys (its Esc closes only itself)
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k != null && k.escapeKey.wasPressedThisFrame && closeWhen == null) Close();   // the menu's card; the pause popup handles its own Esc
    }

    void OnGUI()
    {
        if (DeveloperConsole.CapturesInput) return;
        ScreenFrame.BeginGUI();
        GUI.depth = -20;
        if (card == null) Styles();
        const float W = 560f, H = 470f;
        var box = new Rect((ScreenFrame.GuiWidth - W) * 0.5f, (ScreenFrame.GuiHeight - H) * 0.5f, W, H);
        // a dim over everything behind, so the card reads as the only thing to touch
        Color was = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(0, 0, ScreenFrame.GuiWidth, ScreenFrame.GuiHeight), Texture2D.whiteTexture);
        GUI.color = was;
        GUI.Box(box, GUIContent.none, card);

        float x = box.x + 34f, w = box.width - 68f, y = box.y + 20f;
        GUI.Label(new Rect(x, y, w, 46f), "Sound", title);
        y += 58f;

        for (int i = 0; i < Channels.Length; i++)
        {
            Slider(new Rect(x, y, w, 44f), i);
            y += 54f;
        }

        // Mute all: a paper toggle with the speaker or the crossed speaker
        bool mute = SoundDirector.Muted;
        var muteRect = new Rect(x, y + 4f, w, 46f);
        if (GUI.Button(muteRect, GUIContent.none, paper))
        {
            SoundDirector.Muted = !mute;
            PlayerPrefs.Save();
            if (mute) Sfx.Ui("ui_click");   // unmuting: the click is heard at once
        }
        Texture mi = mute ? muted : speaker;
        if (mi != null) GUI.DrawTexture(new Rect(muteRect.x + 12f, muteRect.y + 5f, 36f, 36f), mi, ScaleMode.ScaleToFit, true);
        GUI.Label(new Rect(muteRect.x + 60f, muteRect.y, muteRect.width - 70f, muteRect.height), mute ? "Muted: tap to hear again" : "Mute all", ink);
        y += 66f;

        // Now playing, with back / next
        SoundDirector.Song now = SoundDirector.NowPlaying;
        bool canSkip = SoundDirector.CanSkip;
        var np = new Rect(x, y, w, 62f);
        GUI.Box(np, GUIContent.none, paper);
        if (record != null)
        {
            GUI.DrawTexture(new Rect(np.x + 8f, np.y + 6f, 50f, 50f), ArtLibrary.RecordFrame(), ScaleMode.ScaleToFit, true);
        }
        GUI.Label(new Rect(np.x + 66f, np.y + 6f, np.width - 200f, 28f), now != null ? now.title : "No song playing", ink);
        GUI.Label(new Rect(np.x + 67f, np.y + 33f, np.width - 200f, 22f),
            now == null ? "" : canSkip ? now.style : now.style + "  (fixed here)", inkSmall);
        GUI.enabled = canSkip;
        if (GUI.Button(new Rect(np.xMax - 124f, np.y + 11f, 54f, 40f), "<<")) { SoundDirector.PreviousSong(); Sfx.Ui("ui_click"); }
        if (GUI.Button(new Rect(np.xMax - 64f, np.y + 11f, 54f, 40f), ">>")) { SoundDirector.NextSong(); Sfx.Ui("ui_click"); }
        GUI.enabled = true;

        if (GUI.Button(new Rect(box.center.x - 90f, box.yMax - 64f, 180f, 46f), "Done")) { Sfx.Ui("ui_click"); Close(); }
    }

    // One volume row: icon, name, a felt track with a gold paper fill and a paper-tag handle, whole percent
    void Slider(Rect r, int i)
    {
        SoundDirector.Volume ch = Channels[i];
        if (icons[i] != null) GUI.DrawTexture(new Rect(r.x, r.y + 4f, 36f, 36f), icons[i], ScaleMode.ScaleToFit, true);
        GUI.Label(new Rect(r.x + 46f, r.y, 150f, r.height), Names[i], label);
        var bar = new Rect(r.x + 200f, r.y + 14f, r.width - 270f, 16f);
        float v = SoundDirector.GetVolume(ch);

        Color before = GUI.color;
        GUI.color = new Color(0.15f, 0.1f, 0.08f);
        GUI.DrawTexture(new Rect(bar.x - 4f, bar.y - 3f, bar.width + 8f, bar.height + 6f), Texture2D.whiteTexture);
        GUI.color = new Color(0.97f, 0.9f, 0.74f, 0.55f);
        for (float sx = bar.x + 2f; sx < bar.xMax - 4f; sx += 12f)   // the felt's running stitch
            GUI.DrawTexture(new Rect(sx, bar.center.y - 1f, 6f, 2f), Texture2D.whiteTexture);
        GUI.color = before;
        if (fill != null && v > 0f)
        {
            Color was = GUI.color;
            GUI.color = Gold;
            GUI.DrawTexture(new Rect(bar.x, bar.y + 2f, bar.width * v, bar.height - 4f), fill, ScaleMode.StretchToFill);
            GUI.color = was;
        }
        float next = GUI.HorizontalSlider(new Rect(bar.x, bar.y - 6f, bar.width, bar.height + 12f), v, 0f, 1f, emptyStyle, thumb);
        if (!Mathf.Approximately(next, v))
        {
            SoundDirector.SetVolume(ch, Mathf.Round(next * 100f) / 100f);   // whole percent
            dragging[i] = true;
        }
        GUI.Label(new Rect(bar.xMax + 12f, r.y, 58f, r.height), Mathf.RoundToInt(SoundDirector.GetVolume(ch) * 100f) + "%", percent);

        // on release: save, and let the player hear the new level
        if (dragging[i] && Event.current.rawType == EventType.MouseUp)
        {
            dragging[i] = false;
            PlayerPrefs.Save();
            if (ch == SoundDirector.Volume.Sfx) Sfx.Play("coin_small");
            else if (ch == SoundDirector.Volume.Ui) Sfx.Ui("ui_click");
        }
    }

    void Styles()
    {
        card = new GUIStyle(GUI.skin.box);
        ArtLibrary.FeltBox(card);
        paper = new GUIStyle(GUI.skin.box);
        ArtLibrary.PaperBox(paper);
        title = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
        label = new GUIStyle(GUI.skin.label) { fontSize = 21, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        small = new GUIStyle(label) { fontSize = 15 };
        percent = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
        if (PaperType.Display != null) title.font = PaperType.Display;
        if (PaperType.Body != null) label.font = small.font = percent.font = PaperType.Body;
        title.normal.textColor = Gold;
        label.normal.textColor = percent.normal.textColor = Cream;
        small.normal.textColor = new Color(0.85f, 0.74f, 0.56f);

        ink = new GUIStyle(label);
        inkSmall = new GUIStyle(small);
        ink.normal.textColor = Ink;
        inkSmall.normal.textColor = Umber;
        thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedWidth = 22f, fixedHeight = 30f };
        Sprite tag = ArtLibrary.Get("UI/PaperCard");
        if (tag != null) { thumb.normal.background = thumb.hover.background = thumb.active.background = tag.texture; thumb.border = new RectOffset(10, 10, 10, 10); }
        emptyStyle = new GUIStyle(GUIStyle.none) { fixedHeight = 28f };
        fill = tag != null ? tag.texture : Texture2D.whiteTexture;

        icons = new Texture[Icons.Length];
        for (int i = 0; i < Icons.Length; i++) icons[i] = Tex(Icons[i]);
        speaker = Tex("Music/Icon_Speaker");
        muted = Tex("Music/Icon_Mute");
        record = Tex("Music/Record");
    }

    static Texture Tex(string name) { Sprite s = ArtLibrary.Get(name); return s != null ? s.texture : null; }
}
