using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Art: restyles uGUI panels into the papercraft look at runtime, by rule, so no scene or builder
// needs rebuilding (HUDBuilder, MapUIBuilder... keep working as they are):
//   - full-screen backdrops          -> tiled felt mine floor
//   - dark panels                    -> stitched felt cards
//   - light / coloured panels        -> paper cards in the SAME colour (pills, accents keep meaning)
// Only plain panels are touched: Images that use Unity's default UI sprite, a rounded-rect sprite or
// none, in Simple/Sliced mode. Icons, fill bars, masks and raycast-only images are left alone.
public class PaperUI : MonoBehaviour
{
    static readonly Color FloorTint = new Color(0.9f, 0.88f, 0.86f);
    static PaperUI instance;
    readonly HashSet<int> done = new HashSet<int>();
    readonly List<Canvas> roots = new List<Canvas>();
    readonly List<Image> found = new List<Image>();
    Sprite felt, paper, floor;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper UI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperUI>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start()
    {
        felt = ArtLibrary.Get("UI/FeltPanel");
        paper = ArtLibrary.Get("UI/PaperCard");
        floor = ArtLibrary.Get("MineFloor");
    }

    void LateUpdate()
    {
        if (felt == null || paper == null) return;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;   // UI built later (store, dialogue) is picked up too
            foreach (Image image in FindObjectsByType<Image>(FindObjectsSortMode.None)) Skin(image);
            roots.Clear();
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas) roots.Add(canvas);
            return;
        }
        // Every frame, the known canvases too: a pop-up built and shown this frame (Pause, the round
        // banner, the end card) is skinned before it is drawn, never seen flat for the half second
        foreach (Canvas canvas in roots)
        {
            if (canvas == null || !canvas.isActiveAndEnabled) continue;
            canvas.GetComponentsInChildren(false, found);
            foreach (Image image in found) Skin(image);
        }
        found.Clear();
    }

    static bool PlainPanel(Image image)
    {
        if (image.type == Image.Type.Filled || image.type == Image.Type.Tiled) return false;
        if (image.GetComponent<Mask>() != null) return false;
        if (image.color.a < 0.3f) return false;   // invisible hit areas, faint highlights
        Sprite s = image.sprite;
        if (s == null) return true;
        if (s.name == "FeltPanel" || s.name == "PaperCard") return false;   // already papercraft (art code set it)
        string n = s.name;
        if (n == "" && image.type == Image.Type.Sliced) return true;   // a code-made rounded box (HUD pop-ups)
        return n == "UISprite" || n == "Background" || n == "InputFieldBackground" || n.Contains("Rounded") || n.Contains("Panel");
    }

    void Skin(Image image)
    {
        if (image == null || !done.Add(image.GetInstanceID())) return;
        if (MaskedFill(image))
        {
            // A meter's fill inside a rounded mask (the HUD's SKILL / DIGEST pills): a strip of paper in
            // the same colour, so a refilling pill reads as paper sliding in. The mask keeps the shape.
            image.sprite = paper;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 4f;
            return;
        }
        if (!PlainPanel(image)) return;
        Color c = image.color;
        Canvas canvas = image.canvas;
        RectTransform rect = image.rectTransform;
        bool fullScreen = canvas != null && IsBackdrop(rect, canvas);
        if (fullScreen && c.a < 0.9f) return;   // a see-through dimmer over the game (result screens): keep it a dimmer
        if (fullScreen && floor != null)
        {
            image.sprite = floor;
            image.type = Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = 0.6f;
            image.color = new Color(FloorTint.r, FloorTint.g, FloorTint.b, c.a);
            return;
        }
        Color.RGBToHSV(c, out _, out float sat, out float val);
        bool dark = val < 0.32f && sat < 0.55f;
        if (image.sprite == null && !dark) return;   // a coloured blank image may be an icon slot code fills later
        image.sprite = dark ? felt : paper;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = Mathf.Clamp(64f / Mathf.Max(8f, Mathf.Min(rect.rect.width, rect.rect.height)), 1f, 4f);   // small pills: thinner edges
        // felt carries its own colour; paper takes the panel's colour so colour codes still read
        image.color = dark ? new Color(Mathf.Lerp(0.85f, 1.1f, val / 0.32f), Mathf.Lerp(0.85f, 1.1f, val / 0.32f), Mathf.Lerp(0.85f, 1.1f, val / 0.32f), c.a) : c;
    }

    static bool MaskedFill(Image image)
    {
        if (image.sprite != null || image.type != Image.Type.Simple || image.color.a < 0.3f) return false;
        Transform parent = image.transform.parent;
        if (parent == null || parent.GetComponent<Mask>() == null) return false;
        RectTransform rect = image.rectTransform;
        return rect.anchorMin == Vector2.zero;   // anchored to the track's left edge, grown by anchorMax
    }

    static bool IsBackdrop(RectTransform rect, Canvas canvas)
    {
        var root = canvas.rootCanvas.transform as RectTransform;
        if (root == null) return false;
        Rect r = rect.rect, full = root.rect;
        Vector3 scale = rect.lossyScale, rootScale = root.lossyScale;
        float area = r.width * scale.x * r.height * scale.y;
        float fullArea = full.width * rootScale.x * full.height * rootScale.y;
        return fullArea > 0f && area >= fullArea * 0.6f;
    }
}
