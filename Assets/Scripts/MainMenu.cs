using UnityEngine;
using UnityEngine.InputSystem;

// Owner: Butian (main menu)
// The Menu scene: the first page a build shows (Boot opens it). Start / Continue goes to the world
// map, New Game wipes the saved progress first (after a confirm), How to Play shows the controls
// and rules, and Quit closes the app (not on the web, where a page can't close itself).
// Same palette as the map (WorldMapController): OnGUI in ScreenFrame's 1280x720 space.
public class MainMenu : MonoBehaviour
{
    static readonly Color BgTop      = new Color(0.10f, 0.12f, 0.17f, 1f);
    static readonly Color BgBottom   = new Color(0.05f, 0.06f, 0.09f, 1f);
    static readonly Color Gold       = new Color(0.98f, 0.80f, 0.33f, 1f);
    static readonly Color Button     = new Color(0.17f, 0.20f, 0.27f, 1f);
    static readonly Color ButtonHot  = new Color(0.24f, 0.28f, 0.37f, 1f);
    static readonly Color Panel      = new Color(0.12f, 0.14f, 0.19f, 0.97f);
    static readonly Color TextBright = new Color(0.94f, 0.96f, 0.98f, 1f);
    static readonly Color TextDim    = new Color(0.62f, 0.66f, 0.74f, 1f);
    static readonly Color Danger     = new Color(0.85f, 0.33f, 0.30f, 1f);

    const int Radius = 12;
    const float ButtonWidth = 300f;
    const float ButtonHeight = 54f;
    const float ButtonGap = 14f;

    const string HowToPlay =
        "You are the one under the hooks. Eat the gold before the miner reels it in.\n\n" +
        "W A S D  -  move\n" +
        "SPACE  -  your character's skill\n" +
        "R  -  retry the level\n\n" +
        "A red dotted line shows where a hook will fly: step out of it before it fires.\n" +
        "Eating makes you digest (green): you can't eat again until it drains.\n" +
        "Finish a level with more money than the miner to clear it on the map.\n" +
        "After Level 8, Survive Mode: an endless field where every hook hunts you.";

    enum Page { Main, HowTo, ConfirmNew }

    Page page = Page.Main;
    Texture2D gradient;
    Texture2D rounded;
    GUIStyle box;
    GUIStyle titleStyle;
    GUIStyle taglineStyle;
    GUIStyle buttonStyle;
    GUIStyle bodyStyle;
    GUIStyle footStyle;

    static bool HasProgress => ProgressStore.IsCompleted("tut_basics") || ProgressStore.HasPickedCharacter;

    static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame && page != Page.Main) page = Page.Main;
        else if (keyboard.enterKey.wasPressedThisFrame && page == Page.Main) Play();
    }

    void OnGUI()
    {
        ScreenFrame.BeginGUI();
        EnsureStyles();
        float w = ScreenFrame.GuiWidth;
        float h = ScreenFrame.GuiHeight;
        GUI.DrawTexture(new Rect(0, 0, w, h), gradient, ScaleMode.StretchToFill);

        GUI.Label(new Rect(0, 120, w, 90), "GOLD MINER", titleStyle);
        GUI.Label(new Rect(0, 205, w, 30), "Eat the gold. Dodge the hook.", taglineStyle);

        switch (page)
        {
            case Page.Main: DrawMain(w); break;
            case Page.HowTo: DrawHowTo(w); break;
            case Page.ConfirmNew: DrawConfirm(w); break;
        }

        float best = PlayerPrefs.GetFloat(SurvivalLoader.BestTimeKey, 0f);
        if (best > 0f)
        {
            int s = Mathf.FloorToInt(best);
            GUI.Label(new Rect(0, h - 40, w, 24), "Best survival  " + (s / 60) + ":" + (s % 60).ToString("00"), footStyle);
        }
    }

    void DrawMain(float w)
    {
        float x = (w - ButtonWidth) * 0.5f;
        float y = 290f;
        if (DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), HasProgress ? "Continue" : "Start", true)) Play();
        y += ButtonHeight + ButtonGap;
        if (HasProgress)
        {
            if (DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "New Game", false)) page = Page.ConfirmNew;
            y += ButtonHeight + ButtonGap;
        }
        if (DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "How to Play", false)) page = Page.HowTo;
        y += ButtonHeight + ButtonGap;
        if (CanQuit && DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "Quit", false)) Application.Quit();
    }

    void DrawHowTo(float w)
    {
        Rect panel = new Rect((w - 760f) * 0.5f, 260f, 760f, 380f);
        DrawPanel(panel);
        GUI.Label(new Rect(panel.x + 36f, panel.y + 28f, panel.width - 72f, panel.height - 110f), HowToPlay, bodyStyle);
        Rect back = new Rect(panel.center.x - ButtonWidth * 0.5f, panel.yMax - 74f, ButtonWidth, ButtonHeight);
        if (DrawButton(back, "Back", true)) page = Page.Main;
    }

    void DrawConfirm(float w)
    {
        Rect panel = new Rect((w - 560f) * 0.5f, 290f, 560f, 220f);
        DrawPanel(panel);
        GUI.Label(new Rect(panel.x + 30f, panel.y + 28f, panel.width - 60f, 80f),
            "Start over?\nThe map, your character and your wallet are reset.", bodyStyle);
        float bw = (panel.width - 90f) * 0.5f;
        if (DrawButton(new Rect(panel.x + 30f, panel.yMax - 84f, bw, ButtonHeight), "Cancel", false)) page = Page.Main;
        if (DrawButton(new Rect(panel.x + 60f + bw, panel.yMax - 84f, bw, ButtonHeight), "Start over", true, Danger)) NewGame();
    }

    void Play()
    {
        GameFlow.Instance.OpenMap();
    }

    // A fresh save: no cleared nodes, no character, no money. The run record is rebuilt on role pick
    void NewGame()
    {
        ProgressStore.ResetAll();
        GameFlow.Instance.OpenMap();
    }

    bool DrawButton(Rect rect, string label, bool primary, Color? tint = null)
    {
        bool hot = rect.Contains(Event.current.mousePosition);
        Color before = GUI.backgroundColor;
        GUI.backgroundColor = tint ?? (hot ? ButtonHot : Button);
        GUI.Box(rect, GUIContent.none, box);
        GUI.backgroundColor = before;

        if (primary && tint == null)
        {
            // A gold bar on the left marks the main choice
            GUI.backgroundColor = Gold;
            GUI.Box(new Rect(rect.x, rect.y, 6f, rect.height), GUIContent.none, box);
            GUI.backgroundColor = before;
        }
        buttonStyle.normal.textColor = hot ? Gold : TextBright;
        if (tint != null) buttonStyle.normal.textColor = TextBright;
        return GUI.Button(rect, label, buttonStyle);
    }

    void DrawPanel(Rect rect)
    {
        Color before = GUI.backgroundColor;
        GUI.backgroundColor = Panel;
        GUI.Box(rect, GUIContent.none, box);
        GUI.backgroundColor = before;
    }

    void EnsureStyles()
    {
        if (box != null) return;

        gradient = new Texture2D(1, 64, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int i = 0; i < 64; i++) gradient.SetPixel(0, i, Color.Lerp(BgBottom, BgTop, i / 63f));
        gradient.Apply();

        rounded = WorldMapController.MakeRounded(Radius);
        box = new GUIStyle { border = new RectOffset(Radius, Radius, Radius, Radius) };
        box.normal.background = rounded;

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Gold;
        taglineStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        taglineStyle.normal.textColor = TextDim;
        buttonStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.UpperCenter };
        bodyStyle.normal.textColor = TextBright;
        footStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        footStyle.normal.textColor = TextDim;
    }
}
