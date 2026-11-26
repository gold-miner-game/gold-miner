using UnityEngine;

// Art: the now-playing tag. Whenever a new song starts (SoundDirector.SongChanged: the radio, a store
// pick, survival's song) a small paper ticket slides in at the bottom left: a cut-paper record turning a
// notch per stop-motion beat, the song's title in Fredoka and its style underneath, e.g.
// "Dig It Disco / funk disco". It holds about 3 seconds and slides back out, on held poses like every
// paper piece, on real time so it also shows over the pause popup. It sits above the movement-key hint
// and clear of the HUD's top bar, the timer and the playfield's middle. Installs itself; no scene edits.
// Art: Resources/Art/Music/Record (tools/art/make_music.py), UI/PaperCard, Fredoka (PaperType.Body).
public class NowPlayingTag : MonoBehaviour
{
    const float Hold = 3f;          // seconds fully shown
    const float Slide = 0.375f;     // seconds to slide in or out (3 held poses at 8 a second)
    const float Width = 360f, Height = 66f;
    const float Left = 18f, FromBottom = 158f;   // GUI units (1280 x 720 frame), above the WASD hint

    static NowPlayingTag instance;
    string title = "", style = "", lastFile;
    float shownAt = -100f;
    GUIStyle card, titleStyle, styleStyle;
    Texture record;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Now Playing Tag");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NowPlayingTag>();
    }

    void OnEnable() { SoundDirector.SongChanged += Show; }
    void OnDisable() { SoundDirector.SongChanged -= Show; }

    void Show(SoundDirector.Song song)
    {
        if (song == null) return;
        // SongChanged also fires when a song loops round: only a new song gets the tag
        if (song.file == lastFile) return;
        lastFile = song.file;
        title = song.title ?? "";
        style = song.style ?? "";
        shownAt = Time.unscaledTime;
    }

    void OnGUI()
    {
        float age = Time.unscaledTime - shownAt;
        float total = Slide + Hold + Slide;
        if (age < 0f || age > total || title == "" || DeveloperConsole.CapturesInput) return;
        if (card == null && !Styles()) return;

        // Slide in, hold, slide out, a held pose at a time (the stop-motion clock's 8 a second)
        float t = Mathf.Floor(age * StopMotion.Pose) / StopMotion.Pose;
        float shown = t < Slide ? t / Slide : t > Slide + Hold ? 1f - (t - Slide - Hold) / Slide : 1f;
        shown = Mathf.Clamp01(shown);
        float ease = 1f - (1f - shown) * (1f - shown);

        ScreenFrame.BeginGUI();
        float x = Mathf.Lerp(-Width - 10f, Left, ease);
        float y = ScreenFrame.GuiHeight - FromBottom;
        Color was = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(shown * 1.4f));
        GUI.Box(new Rect(x, y, Width, Height), GUIContent.none, card);

        // The record turns a notch per beat while the tag is up
        if (record != null)
        {
            GUI.DrawTexture(new Rect(x + 10f, y + 7f, 52f, 52f), ArtLibrary.RecordFrame(), ScaleMode.ScaleToFit, true);
        }
        GUI.Label(new Rect(x + 72f, y + 8f, Width - 84f, 30f), title, titleStyle);
        GUI.Label(new Rect(x + 73f, y + 37f, Width - 84f, 22f), style, styleStyle);
        GUI.color = was;
    }

    bool Styles()
    {
        card = new GUIStyle(GUI.skin.box);
        if (!ArtLibrary.PaperBox(card)) card.normal.background = Texture2D.whiteTexture;
        Sprite rec = ArtLibrary.Get("Music/Record");
        record = rec != null ? rec.texture : null;
        Color ink = new Color(0.24f, 0.16f, 0.1f), umber = new Color(0.46f, 0.32f, 0.2f);
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        styleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        if (PaperType.Body != null) { titleStyle.font = PaperType.Body; styleStyle.font = PaperType.Body; }   // Fredoka, as the HUD cards
        titleStyle.normal.textColor = ink;
        styleStyle.normal.textColor = umber;
        return true;
    }
}
