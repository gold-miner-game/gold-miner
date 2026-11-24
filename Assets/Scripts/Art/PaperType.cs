using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Art: papercraft typography, applied by rule at runtime so no scene, prefab or builder changes.
//   - Fredoka SemiBold (SIL OFL): soft rounded letters for HUD, buttons, dialogue and body text
//   - Luckiest Guy (Apache 2.0): chunky hand-cut display letters for big titles
// Only texts still on the stock fonts (LiberationSans SDF / the built-in legacy font) are switched, so
// any font a teammate picked on purpose is kept. The stock font stays as a fallback for glyphs the new
// fonts lack. IMGUI screens use the shared GUI skin font. Fonts and licences: Resources/Art/Fonts.
[DefaultExecutionOrder(-500)]
public class PaperType : MonoBehaviour
{
    const float TitleSize = 56f;   // at or above this size a text is a title

    public static Font Body { get; private set; }
    public static Font Display { get; private set; }
    static TMP_FontAsset bodyTmp, displayTmp;
    static PaperType instance;
    readonly HashSet<int> done = new HashSet<int>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { titleMaterial = null; bodyTmp = displayTmp = null; skinnedButton = null; titles.Clear(); }   // Play without a domain reload keeps statics

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        Body = Resources.Load<Font>("Art/Fonts/Fredoka-SemiBold");
        Display = Resources.Load<Font>("Art/Fonts/LuckiestGuy-Regular");
        if (Body == null || Display == null) return;
        var go = new GameObject("Paper Type");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperType>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    static TMP_FontAsset Tmp(Font font, TMP_FontAsset fallback)
    {
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
        asset.name = font.name + " SDF (runtime)";
        if (fallback != null) asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        return asset;
    }

    // A screen heading set a little smaller than TitleSize (e.g. Select's "WHO ARE U" at 48): short,
    // all capitals, with letters. Numbers (the HUD clock) and mixed-case text stay body text.
    static bool Heading(string words, float size)
    {
        if (size < 44f || string.IsNullOrEmpty(words) || words.Length > 24) return false;
        bool letters = false;
        foreach (char c in words)
        {
            if (char.IsLower(c)) return false;
            if (char.IsLetter(c)) letters = true;
        }
        return letters;
    }

    static bool StockTmp(TMP_FontAsset font) => font == null || font.name.StartsWith("LiberationSans");
    static bool StockLegacy(Font font) => font == null || font.name == "LegacyRuntime" || font.name == "Arial";

    void EnsureTmp()
    {
        if (bodyTmp != null) return;
        TMP_FontAsset stock = TMP_Settings.defaultFontAsset;
        bodyTmp = Tmp(Body, stock);
        displayTmp = Tmp(Display, stock);
        // A text that renders for the first time (a result panel switching on) is queued and restyled
        // in the next LateUpdate, at most one frame later, instead of waiting for the half-second scan.
        // (Not restyled inside the event: that fires during the canvas rebuild, where marking a graphic
        // dirty is an error.)
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

    void OnDestroy() { TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged); }

    readonly List<TMP_Text> pending = new List<TMP_Text>();

    void OnTextChanged(Object changed)
    {
        if (changed is TMP_Text text && !done.Contains(text.GetInstanceID())) pending.Add(text);
    }

    void Style(TMP_Text text)
    {
        if (text == null || !done.Add(text.GetInstanceID())) return;
        // Code that builds a label by copying an already restyled one's font (the HUD's Pause, end card
        // and round banner copy the clock's) arrives on the body face: still ours to judge, so its
        // titles are titles too
        bool copied = bodyTmp != null && text.font == bodyTmp;
        if (!copied && !StockTmp(text.font)) return;
        bool popupTitle = copied && PopupTitle(text);
        bool title = text.fontSize >= TitleSize || Heading(text.text, text.fontSize) || popupTitle;
        text.font = title ? displayTmp : bodyTmp;
        if (!title) return;
        Gild(text);
        if (copied)
        {
            // the capitals face is wider: a long result line ("DIDN'T ACHIEVE THE TARGET") shrinks to
            // stay on its one line in the card instead of wrapping over the row below
            text.enableAutoSizing = true;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = text.fontSize * 0.6f;
        }
    }

    // A pop-up card's headline: the label the HUD names Title or Result, at banner size or more
    static bool PopupTitle(TMP_Text text)
    {
        string n = text.name;
        return (n == "Title" || n == "Result") && text.fontSize >= 40f;
    }

    void Start() { if (Body != null) EnsureTmp(); }

    void LateUpdate()
    {
        if (pending.Count > 0)
        {
            foreach (TMP_Text text in pending) Style(text);
            pending.Clear();
        }
        Stamp();
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;
        EnsureTmp();
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) Style(text);
        foreach (Text text in FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (!done.Add(text.GetInstanceID()) || !StockLegacy(text.font)) continue;
            bool title = text.fontSize >= TitleSize;
            text.font = title ? Display : Body;
            if (title && text.GetComponent<Shadow>() == null)
            {
                // same title treatment for legacy uGUI text: umber cut outline, paper drop shadow, gold leaf
                Outline cut = text.gameObject.AddComponent<Outline>();
                cut.effectColor = new Color(0.23f, 0.16f, 0.11f);
                cut.effectDistance = new Vector2(2.5f, -2.5f);
                Shadow drop = text.gameObject.AddComponent<Shadow>();
                drop.effectColor = new Color(0.08f, 0.05f, 0.03f, 0.75f);
                drop.effectDistance = new Vector2(5f, -6f);
                Color c = text.color;
                if (c.r > 0.9f && c.g > 0.9f && c.b > 0.9f) text.color = new Color(GoldLeaf.r, GoldLeaf.g, GoldLeaf.b, c.a);
            }
        }
    }

    // Titles get the logo's treatment: an umber cut outline and a paper drop shadow below-right, and a
    // plain white title turns gold leaf. A title in any other colour keeps it (it may mean something).
    // Uses the mobile SDF shader with OUTLINE_ON + UNDERLAY_ON, the same variant as TMP's "Drop Shadow"
    // preset in Resources, so a build always has it.
    static Material titleMaterial;

    // Titles stamp in when they appear or change (results, rewards): big, smaller, a small overshoot,
    // settled, on twos in unscaled time (result screens may pause the game). Skipped while any other
    // code is scaling the title, so it never fights an existing animation.
    class Title { public TMP_Text text; public Vector3 scale; public bool shown; public string words; public float at = -100f; public bool settled = true; }
    static readonly List<Title> titles = new List<Title>();
    static readonly float[] StampPoses = { 1.32f, 1.1f, 0.96f, 1f };

    static void Stamp()
    {
        for (int i = titles.Count - 1; i >= 0; i--)
        {
            Title t = titles[i];
            if (t.text == null) { titles.RemoveAt(i); continue; }
            bool shown = t.text.isActiveAndEnabled;
            if (shown && (!t.shown || t.words != t.text.text)) { t.at = Time.unscaledTime; t.settled = false; }
            t.shown = shown;
            t.words = t.text.text;
            if (!shown) continue;
            int step = Mathf.FloorToInt((Time.unscaledTime - t.at) * StopMotion.Pose);
            if (t.settled) continue;
            Transform tr = t.text.rectTransform;
            Vector3 now = tr.localScale;
            if (step >= StampPoses.Length)
            {
                // over: back to the rest scale once (a long frame may have skipped the last pose)
                if (IsStampOf(now, t.scale)) tr.localScale = t.scale;
                t.settled = true;
                continue;
            }
            bool ours = step == 0 ? (now - t.scale).sqrMagnitude < 1e-6f || IsStampOf(now, t.scale) : IsStampOf(now, t.scale);
            if (!ours) { t.settled = true; continue; }   // something else is scaling it: stay out of the way
            tr.localScale = t.scale * StampPoses[step];
        }
    }

    static bool IsStampOf(Vector3 now, Vector3 baseScale)
    {
        foreach (float k in StampPoses) if ((now - baseScale * k).sqrMagnitude < 1e-6f) return true;
        return (now - baseScale).sqrMagnitude < 1e-6f;
    }
    static readonly Color GoldLeaf = new Color(0.98f, 0.8f, 0.32f);

    static void Gild(TMP_Text text)
    {
        if (titleMaterial == null)
        {
            Shader mobile = Shader.Find("TextMeshPro/Mobile/Distance Field");
            titleMaterial = new Material(displayTmp.material) { name = "Paper Title (runtime)" };
            if (mobile != null) titleMaterial.shader = mobile;
            titleMaterial.EnableKeyword("OUTLINE_ON");
            titleMaterial.EnableKeyword("UNDERLAY_ON");
            titleMaterial.SetColor("_OutlineColor", new Color(0.23f, 0.16f, 0.11f));
            titleMaterial.SetFloat("_OutlineWidth", 0.22f);
            titleMaterial.SetColor("_UnderlayColor", new Color(0.08f, 0.05f, 0.03f, 0.75f));
            titleMaterial.SetFloat("_UnderlayOffsetX", 0.55f);
            titleMaterial.SetFloat("_UnderlayOffsetY", -0.65f);
            titleMaterial.SetFloat("_UnderlayDilate", 0.25f);
            titleMaterial.SetFloat("_UnderlaySoftness", 0.15f);
            ShaderUtilities.UpdateShaderRatios(titleMaterial);
        }
        text.fontSharedMaterial = titleMaterial;
        text.fontStyle |= FontStyles.UpperCase;   // the display face is a capitals face; its lowercase looks broken
        titles.Add(new Title { text = text, scale = text.rectTransform.localScale, shown = false, words = null });
        Color c = text.color;
        if (c.r > 0.9f && c.g > 0.9f && c.b > 0.9f) text.color = new Color(GoldLeaf.r, GoldLeaf.g, GoldLeaf.b, c.a);
    }

    void OnGUI()
    {
        // The built-in skin is shared by every OnGUI: styles without their own font use this one.
        if (GUI.skin.font != Body) GUI.skin.font = Body;
        // Default-skin IMGUI buttons (end panels, prompts) become paper cards with ink text instead of
        // Unity's grey bevel. Buttons with a style of their own (the menu's felt) are untouched.
        if (skinnedButton != GUI.skin)
        {
            skinnedButton = GUI.skin;
            Sprite paper = ArtLibrary.Get("UI/PaperCard");
            if (paper != null)
            {
                GUIStyle b = GUI.skin.button;
                b.normal.background = b.hover.background = b.active.background = b.focused.background = paper.texture;
                b.onNormal.background = b.onHover.background = b.onActive.background = paper.texture;
                b.border = new RectOffset(16, 16, 16, 16);
                b.normal.textColor = b.focused.textColor = b.onNormal.textColor = Ink;
                b.hover.textColor = b.onHover.textColor = HoverInk;
                b.active.textColor = b.onActive.textColor = Color.black;
            }
            // Default-skin boxes (hint boxes like the world map's) become stitched felt with cream text.
            // Styles copied from the skin before this, or given their own background, keep theirs.
            Sprite felt = ArtLibrary.Get("UI/FeltPanel");
            if (felt != null)
            {
                GUIStyle box = GUI.skin.box;
                box.normal.background = felt.texture;
                box.border = new RectOffset(22, 22, 22, 22);
                box.normal.textColor = ArtLibrary.Cream;
            }
        }
    }

    static GUISkin skinnedButton;
    static readonly Color Ink = new Color(0.16f, 0.12f, 0.1f);
    static readonly Color HoverInk = new Color(0.55f, 0.33f, 0.05f);
}
