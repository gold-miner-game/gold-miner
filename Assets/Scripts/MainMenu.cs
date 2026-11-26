using UnityEngine;
using UnityEngine.InputSystem;

// Owner: Butian (main menu)
// The Menu scene: the first page a build shows (Boot opens it). Start / Continue goes to the world
// map, New Game wipes the saved progress first (after a confirm), and Quit closes the app
// (not on the web, where a page can't close itself).
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

    enum Page { Main, ConfirmNew }

    Page page = Page.Main;
    Texture2D gradient;
    Texture2D rounded;
    GUIStyle box;
    GUIStyle titleStyle;
    GUIStyle taglineStyle;
    GUIStyle buttonStyle;
    GUIStyle bodyStyle;
    GUIStyle footStyle;
    GUIStyle paperBox;      // art: paper card (gold "main choice" tab); the rounded box without art
    Texture2D titleArt;     // art: painted title backdrop; null = the gradient
    Texture2D logo;         // art: felt and gold-leaf "Oinky Oinky" logo; null = the text title
    bool crafted;           // art: felt buttons are in use (tints become light, the felt has its own colour)
    static readonly Color FeltTint    = new Color(0.9f, 0.88f, 0.86f, 1f);
    static readonly Color FeltHot     = new Color(1f, 1f, 1f, 1f);
    static readonly Color Cream       = new Color(0.95f, 0.88f, 0.74f, 1f);
    static readonly Color TitleShadow = new Color(0.12f, 0.06f, 0.02f, 0.75f);

    static bool HasProgress => ProgressStore.TutorialDone || ProgressStore.HasPickedCharacter;

    static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;

    void Update()
    {
        if (peelStart >= 0f && Time.unscaledTime - peelStart >= PeelTime)
        {
            peelStart = -1f;
            peeled = true;   // the sticker is gone: don't redraw it while the next scene loads
            Play();
            return;
        }
        Keyboard keyboard = DeveloperConsole.CapturesInput ? null : Keyboard.current;
        if (keyboard == null || SoundPanel.IsOpen) return;   // keys belong to the Sound card while it is open
        if (keyboard.escapeKey.wasPressedThisFrame && page != Page.Main) page = Page.Main;
        else if (keyboard.enterKey.wasPressedThisFrame && page == Page.Main) StartGame();
    }

    // Art: the main button is a felt sticker. Pressing it peels the sticker off, right to left, in
    // stop-motion poses (docs/art-style.md), then the game starts. Without art it starts at once.
    // Shot on threes (8 poses a second), every pose held 0.125 s so each reads as a separate exposure:
    // corner lifts and holds (anticipation), a third, two thirds, all of it curled, then it flips off.
    static readonly float[] PeelPoses = { 0.06f, 0.06f, 0.34f, 0.68f, 1f, 1f };
    const float PeelTime = 0.75f;   // the Start/Continue transition, as before; the six poses share it evenly
    float peelStart = -1f;
    bool peeled;

    void StartGame()
    {
        if (!crafted) { Play(); return; }
        if (peelStart < 0f) peelStart = Time.unscaledTime;
    }

    void DrawPeel(Rect rect, string label)
    {
        int pose = Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - peelStart) / PeelTime * PeelPoses.Length), 0, PeelPoses.Length - 1);
        float p = PeelPoses[pose];
        bool flipping = pose == PeelPoses.Length - 1;   // last pose: the sticker leaves the backdrop
        // Pulled by its gold tab on the left: the sticker peels left to right.
        float edge = rect.x + rect.width * p;
        float curl = Mathf.Min(rect.width * p * 0.5f, 46f);

        // the sticker's shadow on the backdrop where it has already lifted
        Color before = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
        GUI.Box(new Rect(rect.x, rect.y + 4f, edge - rect.x, rect.height), GUIContent.none, paperBox);

        // the part still stuck down (the right of the sticker)
        GUI.BeginGroup(new Rect(edge, rect.y, Mathf.Max(0f, rect.xMax - edge), rect.height));
        GUI.backgroundColor = FeltHot;
        GUI.Box(new Rect(rect.x - edge, 0f, rect.width, rect.height), GUIContent.none, box);
        buttonStyle.normal.textColor = Gold;
        GUI.Label(new Rect(rect.x - edge, 0f, rect.width, rect.height), label, buttonStyle);
        GUI.EndGroup();

        // the peeled flap: the sticker's paper back, folded over the edge
        if (curl > 1f)
        {
            GUI.backgroundColor = new Color(0.95f, 0.9f, 0.8f);
            // on the last pose the curled sticker is lifted clear, up and to the right, as it is pulled off
            float lift = flipping ? rect.height * 0.9f : 0f;
            GUI.Box(new Rect(edge + lift * 0.4f, rect.y - 2f - lift, curl, rect.height + 4f), GUIContent.none, paperBox);
        }
        GUI.backgroundColor = before;
    }

    void OnGUI()
    {
        if (DeveloperConsole.CapturesInput) return;
        ScreenFrame.BeginGUI();
        EnsureStyles();
        float w = ScreenFrame.GuiWidth;
        float h = ScreenFrame.GuiHeight;
        if (titleArt != null) GUI.DrawTexture(new Rect(0, 0, w, h), titleArt, ScaleMode.ScaleAndCrop);
        else GUI.DrawTexture(new Rect(0, 0, w, h), gradient, ScaleMode.StretchToFill);

        if (logo != null)
        {
            // art: the felt and gold-leaf logo (the same words), centred where the title was
            float lh = 175f, lw = lh * logo.width / logo.height;
            GUI.DrawTexture(new Rect((w - lw) * 0.5f, 52f, lw, lh), logo, ScaleMode.ScaleToFit);
        }
        else
        {
            if (crafted)
            {
                // cut-paper title: a dark shadow card slightly down-right of the gold letters
                Color gold = titleStyle.normal.textColor;
                titleStyle.normal.textColor = TitleShadow;
                GUI.Label(new Rect(4, 125, w, 90), "Oinky Oinky", titleStyle);
                titleStyle.normal.textColor = gold;
            }
            GUI.Label(new Rect(0, 120, w, 90), "Oinky Oinky", titleStyle);
        }
        GUI.Label(new Rect(0, logo != null ? 228f : 205f, w, 30), "Eat the gold. Dodge the hook.", taglineStyle);

        if (SoundPanel.IsOpen) return;   // the Sound card is on top: no menu button under it
        switch (page)
        {
            case Page.Main: DrawMain(w); break;
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
        Rect main = new Rect(x, y, ButtonWidth, ButtonHeight);
        string mainLabel = HasProgress ? "Continue" : "Start";
        if (peeled) { }
        else if (peelStart >= 0f) DrawPeel(main, mainLabel);
        else if (DrawButton(main, mainLabel, true)) StartGame();
        y += ButtonHeight + ButtonGap;
        if (HasProgress)
        {
            if (DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "New Game", false)) page = Page.ConfirmNew;
            y += ButtonHeight + ButtonGap;
        }
        if (DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "Sound", false)) SoundPanel.Show();
        y += ButtonHeight + ButtonGap;
        if (CanQuit && DrawButton(new Rect(x, y, ButtonWidth, ButtonHeight), "Quit", false)) Application.Quit();
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
        // Continuing keeps one-time stories seen. NewGame resets them with the save.
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
        GUI.backgroundColor = tint ?? (crafted ? (hot ? FeltHot : FeltTint) : (hot ? ButtonHot : Button));
        GUI.Box(rect, GUIContent.none, box);
        GUI.backgroundColor = before;

        if (primary && tint == null)
        {
            // A gold bar on the left marks the main choice (art: a gold paper tab)
            GUI.backgroundColor = Gold;
            if (crafted) GUI.Box(new Rect(rect.x - 10f, rect.y + 6f, 22f, rect.height - 12f), GUIContent.none, paperBox);
            else GUI.Box(new Rect(rect.x, rect.y, 6f, rect.height), GUIContent.none, box);
            GUI.backgroundColor = before;
        }
        buttonStyle.normal.textColor = hot ? Gold : (crafted ? Cream : TextBright);
        if (tint != null) buttonStyle.normal.textColor = TextBright;
        bool pressed = GUI.Button(rect, label, buttonStyle);
        if (pressed) Sfx.Ui("ui_click");   // sound
        return pressed;
    }

    void DrawPanel(Rect rect)
    {
        Color before = GUI.backgroundColor;
        GUI.backgroundColor = crafted ? FeltHot : Panel;
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
        paperBox = box;

        // Art (papercraft): painted title backdrop, stitched felt buttons, a gold paper tab
        Sprite backdrop = ArtLibrary.Get("Menu/TitleBackdrop");
        Sprite felt = ArtLibrary.Get("UI/FeltPanel");
        Sprite paper = ArtLibrary.Get("UI/PaperCard");
        if (backdrop != null) titleArt = backdrop.texture;
        Sprite logoArt = ArtLibrary.Get("Menu/Logo");
        if (logoArt != null) logo = logoArt.texture;
        if (felt != null)
        {
            box = new GUIStyle { border = new RectOffset(24, 24, 24, 24) };
            box.normal.background = felt.texture;
            crafted = true;
        }
        if (paper != null)
        {
            paperBox = new GUIStyle { border = new RectOffset(20, 20, 20, 20) };
            paperBox.normal.background = paper.texture;
        }

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Gold;
        taglineStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        taglineStyle.normal.textColor = TextDim;
        buttonStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.UpperCenter };
        bodyStyle.normal.textColor = TextBright;
        footStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        footStyle.normal.textColor = TextDim;
        if (crafted) { taglineStyle.normal.textColor = Cream; footStyle.normal.textColor = Cream; }
        if (PaperType.Display != null) titleStyle.font = PaperType.Display;   // art: hand-cut title letters
    }
}
