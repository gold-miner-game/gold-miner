using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

// The level HUD's Quit: a 132x48 white rounded button, 10 px corners, bold 27 px dark label,
// on the 1920x1080 frame. Map and Survival use the same chrome.
public static class QuitChrome
{
    public const float Width = 132f;
    public const float Height = 48f;
    public static readonly Color Ink = new Color(0.10f, 0.12f, 0.11f);

    // 16 px in from the right of the map area (the sidebar, when there is one) and 10 px down,
    // in the 1280x720 frame. That is 24 and 15 on this 1920x1080 canvas.
    public static Vector2 MapOffset()
    {
        float sidebar = 1920f * HUDLayout.SidebarWidth;
        return new Vector2(-(sidebar + 24f), -15f);
    }

    public static Vector2 CornerOffset() => new Vector2(-24f, -15f);

    public static Button Create(Vector2 topRightOffset, UnityAction onClick)
    {
        var canvasGo = new GameObject("QuitCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        EventSystem events = EventSystem.current != null
            ? EventSystem.current
            : Object.FindFirstObjectByType<EventSystem>();
        if (events == null)
            events = new GameObject("EventSystem").AddComponent<EventSystem>();
        var legacy = events.GetComponent<StandaloneInputModule>();
        var modern = events.GetComponent<InputSystemUIInputModule>();
        if (modern == null)
        {
            if (legacy != null) Object.Destroy(legacy);
            modern = events.gameObject.AddComponent<InputSystemUIInputModule>();
            modern.AssignDefaultActions();
        }
        else if (legacy != null)
            legacy.enabled = false;

        var go = new GameObject("Quit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(canvasGo.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(Width, Height);
        rect.anchoredPosition = topRightOffset;

        var image = go.GetComponent<Image>();
        image.sprite = Face();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 3.2f;   // 32 px sprite corner draws as 10 px, same as the level button
        image.color = Color.white;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.94f, 0.95f, 0.94f);
        colors.pressedColor = new Color(0.82f, 0.84f, 0.82f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        label.text = "Quit";
        label.fontSize = 27f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Ink;
        label.raycastTarget = false;
        return button;
    }

    static Sprite face;

    // Same sliced circle the level button uses (HUDRounded: 32 px corners).
    static Sprite Face()
    {
        if (face != null) return face;
        const int size = 64;
        const float radius = 32f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius)));
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        face = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return face;
    }
}
