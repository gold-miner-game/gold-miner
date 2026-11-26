using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Same recorded-gameplay player and reveal as the shop, without creating a live round or changing saves.
public class CharacterSkillDemo : MonoBehaviour
{
    public const float PreviewSize = 400f;

    static CharacterSkillDemo instance;
    Object owner;   // the pick-page card or the camp showing the clip
    Canvas canvas;
    RectTransform panel;
    StoreItemDemo playback;
    string shown;

    public static string ClipName(int champion) => champion == 0 ? "pig_block"
        : champion == 1 ? "cat_roll" : champion == 4 ? "elsa_frost" : champion == 5 ? "hulk_charge" : null;

    public static bool Supports(int champion) => ClipName(champion) != null;

    public static void Show(SkillPreview source, Bounds portrait)
    {
        if (source == null || !Supports(source.ChampionIndex)) return;
        if (instance == null)
        {
            var root = new GameObject("CharacterSkillDemo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = root.AddComponent<CharacterSkillDemo>();
            instance.Build();
        }
        if (!instance.Place(source, portrait))
        {
            instance.panel.gameObject.SetActive(false);
            instance.owner = null;
            return;
        }
        if (instance.owner == source && instance.panel.gameObject.activeSelf) return;
        instance.owner = source;
        instance.shown = ClipName(source.ChampionIndex);
        instance.panel.gameObject.SetActive(true);
        instance.playback.Play("CharacterDemos/" + ClipName(source.ChampionIndex), instance.panel);
    }

    // The camp: the clip plays in a spot the caller has already kept clear (a square in ScreenFrame's
    // 1280 x 720 OnGUI space), so it never covers the hero, the walker or the talk card
    public static void ShowInFrame(Object source, int champion, Rect guiSquare)
    {
        if (source == null || !Supports(champion)) return;
        if (instance == null)
        {
            var root = new GameObject("CharacterSkillDemo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = root.AddComponent<CharacterSkillDemo>();
            instance.Build();
        }
        Rect frame = ScreenFrame.Pixels;
        float s = ScreenFrame.GuiScale;
        var centre = new Vector2(frame.x + guiSquare.center.x * s, frame.yMax - guiSquare.center.y * s);
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)instance.transform,
            centre, null, out Vector3 position)) return;
        instance.panel.position = position;
        instance.panel.sizeDelta = Vector2.one * (guiSquare.height * s / Mathf.Max(.01f, instance.canvas.scaleFactor));
        string clip = ClipName(champion);
        if (instance.owner == source && instance.shown == clip && instance.panel.gameObject.activeSelf) return;
        instance.owner = source;
        instance.shown = clip;
        instance.panel.gameObject.SetActive(true);
        instance.playback.Play("CharacterDemos/" + clip, instance.panel);
    }

    public static void Hide(Object source)
    {
        if (instance == null || instance.owner != source) return;
        if (instance.panel != null) instance.panel.gameObject.SetActive(false);
        instance.owner = null;
    }

    void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 520;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        var border = new GameObject("DemoTip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel = border.GetComponent<RectTransform>();
        panel.SetParent(transform, false);
        panel.sizeDelta = Vector2.one * PreviewSize;
        Image image = border.GetComponent<Image>();
        image.color = new Color(0.15f, 0.09f, 0.05f);
        image.raycastTarget = false;
        border.GetComponent<CanvasGroup>().blocksRaycasts = false;
        var screen = new GameObject("RecordedGameplay", typeof(RectTransform), typeof(Image));
        screen.transform.SetParent(panel, false);
        screen.GetComponent<Image>().color = Color.black;
        screen.GetComponent<Image>().raycastTarget = false;
        RectTransform rect = screen.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * 3f;
        rect.offsetMax = Vector2.one * -3f;
        // StoreItemDemo adds its RawImage to its host, so keep the black background separate.
        var player = new GameObject("Playback", typeof(RectTransform));
        player.transform.SetParent(screen.transform, false);
        rect = player.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        playback = player.AddComponent<StoreItemDemo>();
    }

    bool Place(SkillPreview source, Bounds portrait)
    {
        Camera camera = Camera.main;
        if (camera == null) return false;
        Rect frame = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? ScreenFrame.Pixels : canvas.pixelRect;
        float scale = Mathf.Max(.01f, canvas.scaleFactor);
        float margin = 12f * scale;
        Rect safe = Rect.MinMaxRect(frame.xMin + margin, frame.yMin + margin, frame.xMax - margin, frame.yMax - margin);
        Rect original = Project(camera, portrait);
        // Protect the whole source card, including labels and every child of its button.
        foreach (RectTransform element in new[] { source.namePlate, source.remark, source.selectButton })
        {
            if (element == null || !element.gameObject.activeInHierarchy) continue;
            Canvas elementCanvas = element.GetComponentInParent<Canvas>();
            Camera elementCamera = elementCanvas != null && elementCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? elementCanvas.rootCanvas.worldCamera : null;
            foreach (RectTransform part in element.GetComponentsInChildren<RectTransform>())
            {
                var corners = new Vector3[4];
                TMP_Text text = part.GetComponent<TMP_Text>();
                // Text layout boxes can span beyond the visible card, especially JK's description.
                if (text != null && part.GetComponent<Image>() == null)
                {
                    text.ForceMeshUpdate();
                    Bounds ink = text.textBounds;
                    corners[0] = part.TransformPoint(ink.min);
                    corners[1] = part.TransformPoint(new Vector3(ink.min.x, ink.max.y, ink.center.z));
                    corners[2] = part.TransformPoint(ink.max);
                    corners[3] = part.TransformPoint(new Vector3(ink.max.x, ink.min.y, ink.center.z));
                }
                else part.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(elementCamera, corner);
                    original = Rect.MinMaxRect(Mathf.Min(original.xMin, point.x), Mathf.Min(original.yMin, point.y),
                        Mathf.Max(original.xMax, point.x), Mathf.Max(original.yMax, point.y));
                }
            }
        }
        Rect protectedPortrait = Rect.MinMaxRect(original.xMin - margin, original.yMin - margin,
            original.xMax + margin, original.yMax + margin);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        // Use a fixed, larger preview beside the whole source card; other cards may be covered.
        const float units = PreviewSize;
        float size = units * scale;
        float y = Mathf.Clamp(original.center.y - size * .5f, safe.yMin, safe.yMax - size);
        float x = Mathf.Clamp(original.center.x - size * .5f, safe.xMin, safe.xMax - size);
        float gap = Mathf.Max(1f, scale);
        Rect left = new Rect(protectedPortrait.xMin - size - gap, y, size, size);
        Rect right = new Rect(protectedPortrait.xMax + gap, y, size, size);
        bool preferRight = original.center.x <= frame.center.x;
        Rect[] candidates =
        {
            preferRight ? right : left, preferRight ? left : right,
            new Rect(x, protectedPortrait.yMax + gap, size, size),
            new Rect(x, protectedPortrait.yMin - size - gap, size, size)
        };
        foreach (Rect candidate in candidates)
        {
            if (candidate.xMin < safe.xMin || candidate.yMin < safe.yMin || candidate.xMax > safe.xMax || candidate.yMax > safe.yMax) continue;
            if (candidate.Overlaps(protectedPortrait)) continue;
            panel.sizeDelta = Vector2.one * units;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)transform,
                candidate.center, uiCamera, out Vector3 position)) continue;
            panel.position = position;
            return true;
        }
        return false;
    }

    static Rect Project(Camera camera, Bounds bounds)
    {
        Vector2 a = camera.WorldToScreenPoint(bounds.min);
        Vector2 b = camera.WorldToScreenPoint(bounds.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
