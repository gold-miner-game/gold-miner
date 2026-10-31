using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// The part of a TutorialStep's dark that covers the UI. The arena's dark is a sprite the game
// camera draws, so it can't reach the HUD (which is drawn after it); this is a full-screen image on
// a canvas above everything, see-through over the arena (the sprite dark is there) and inside each
// TutorialUIHole. TutorialStep drives it; it makes itself the first time it is needed.
public class TutorialUIDark : MonoBehaviour
{
    const int MaxHoles = 8;       // the shader's array size, arena included
    const int SortingOrder = 500; // above the HUD and the pause popup's canvas

    static TutorialUIDark instance;
    static bool warned;

    Canvas canvas;
    RectTransform canvasRect;
    Image image;
    Material material;
    readonly Vector4[] rects = new Vector4[MaxHoles];
    readonly Vector4[] styles = new Vector4[MaxHoles];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // Play without a domain reload keeps statics
    {
        instance = null;
        warned = false;
    }

    // Darken the UI at `alpha`, except over the arena and around the holes. alpha 0 hides it
    public static void Show(float alpha, List<TutorialUIHole> holes)
    {
        if (instance == null)
        {
            if (alpha <= 0f) return;
            instance = Create();
            if (instance == null) return;
        }
        instance.Apply(alpha, holes);
    }

    static TutorialUIDark Create()
    {
        Shader shader = Resources.Load<Shader>("Shaders/TutorialDark");
        if (shader == null)
        {
            if (!warned) Debug.LogWarning("TutorialUIDark: missing Resources/Shaders/TutorialDark.shader; the UI won't darken.");
            warned = true;
            return null;
        }

        var go = new GameObject("TutorialUIDark", typeof(RectTransform), typeof(Canvas));
        var dark = go.AddComponent<TutorialUIDark>();
        dark.canvas = go.GetComponent<Canvas>();
        dark.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        dark.canvas.sortingOrder = SortingOrder;
        dark.canvasRect = (RectTransform)go.transform;

        var imageObject = new GameObject("Dark", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)imageObject.transform;
        rect.SetParent(go.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-4000f, -4000f);   // past the screen, whatever ScreenFrame does
        rect.offsetMax = new Vector2(4000f, 4000f);
        dark.image = imageObject.GetComponent<Image>();
        dark.image.raycastTarget = false;   // the dark never blocks a click
        dark.material = new Material(shader);
        dark.image.material = dark.material;
        return dark;
    }

    void Apply(float alpha, List<TutorialUIHole> holes)
    {
        canvas.enabled = alpha > 0f;
        if (!canvas.enabled) return;
        image.color = new Color(0f, 0f, 0f, alpha);

        int count = 0;
        // The arena: its own (sprite) dark is there, with the world holes cut out of it
        Camera cam = Camera.main;
        if (cam != null) AddHole(ref count, cam.pixelRect, 0f, 0f);

        float scale = Screen.height / 1080f;   // the holes' sizes are in 1080p pixels
        if (holes != null)
            foreach (TutorialUIHole hole in holes)
            {
                if (count >= MaxHoles) break;
                RectTransform target = hole != null ? hole.Find() : null;
                if (target == null) continue;
                Rect screen = ScreenRect(target);
                float pad = hole.padding * scale;
                screen = Rect.MinMaxRect(screen.xMin - pad, screen.yMin - pad, screen.xMax + pad, screen.yMax + pad);
                AddHole(ref count, screen, hole.cornerRadius * scale, hole.feather * scale);
            }

        material.SetVectorArray("_Rects", rects);
        material.SetVectorArray("_RectStyle", styles);
        material.SetFloat("_RectCount", count);
    }

    // A screen-pixel rect as a hole, in this canvas's space (what the shader's vertices are in)
    void AddHole(ref int count, Rect screen, float corner, float feather)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.min, null, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.max, null, out Vector2 max);
        float k = canvas.scaleFactor > 0f ? 1f / canvas.scaleFactor : 1f;
        rects[count] = new Vector4(min.x, min.y, max.x, max.y);
        styles[count] = new Vector4(corner * k, feather * k, 0f, 0f);
        count++;
    }

    // Where a UI element is on the screen, in pixels (origin bottom left)
    static Rect ScreenRect(RectTransform target)
    {
        Canvas owner = target.GetComponentInParent<Canvas>();
        Camera cam = owner != null && owner.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? owner.rootCanvas.worldCamera : null;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (instance == this) instance = null;
    }
}
