using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// A sprite object (a character prefab: body, eyes, ...) copied into UI images, so it can sit in a
// screen canvas. A canvas only draws UI, and measures in pixels, where a sprite 1 world unit tall
// would be 1 pixel. The copy keeps the parts' layout, turn and stacking, scaled to `height` pixels,
// and stands with its bottom centre on the new object's pivot.
// Used by Tools > Gold Miner > Convert Sprites To UI, and by stories shown On Screen.
public static class UISpriteCopy
{
    public static RectTransform Build(GameObject source, Transform parent, float height, Dictionary<Image, Color> colors = null)
    {
        var root = new GameObject(source.name, typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = Vector2.zero;

        Transform top = source.transform;
        Matrix4x4 toRoot = top.worldToLocalMatrix;
        var parts = new List<SpriteRenderer>();
        foreach (SpriteRenderer sr in source.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.sprite != null && sr.enabled && Shown(sr.transform, top)) parts.Add(sr);
        if (parts.Count == 0) return rect;

        // Its outline in the source's own space
        bool any = false;
        Bounds outline = new Bounds();
        foreach (SpriteRenderer sr in parts)
        {
            Matrix4x4 m = toRoot * sr.transform.localToWorldMatrix;
            Bounds b = sr.sprite.bounds;
            foreach (Vector3 corner in new[] { b.min, b.max, new Vector3(b.min.x, b.max.y), new Vector3(b.max.x, b.min.y) })
            {
                Vector3 p = m.MultiplyPoint(corner);
                if (!any) { outline = new Bounds(p, Vector3.zero); any = true; }
                else outline.Encapsulate(p);
            }
        }
        float unit = height / Mathf.Max(0.01f, outline.size.y);   // pixels per world unit
        rect.sizeDelta = new Vector2(outline.size.x, outline.size.y) * unit;

        // Back to front, as the sprites stack
        parts.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
        foreach (SpriteRenderer sr in parts)
        {
            Matrix4x4 m = toRoot * sr.transform.localToWorldMatrix;
            Sprite sprite = sr.sprite;
            var piece = new GameObject(sr.name, typeof(RectTransform), typeof(Image));
            var pr = (RectTransform)piece.transform;
            pr.SetParent(rect, false);
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
            pr.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 size = sr.drawMode == SpriteDrawMode.Simple ? sprite.rect.size / sprite.pixelsPerUnit : sr.size;
            pr.sizeDelta = size * unit;
            Vector3 at = m.MultiplyPoint(Vector3.zero);
            pr.anchoredPosition = new Vector2(at.x - outline.center.x, at.y - outline.min.y) * unit;
            Vector3 xAxis = m.GetColumn(0), yAxis = m.GetColumn(1);
            pr.localScale = new Vector3(xAxis.magnitude * (sr.flipX ? -1f : 1f), yAxis.magnitude * (sr.flipY ? -1f : 1f), 1f);
            pr.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(xAxis.y, xAxis.x) * Mathf.Rad2Deg);

            Image image = piece.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sr.drawMode == SpriteDrawMode.Simple ? Image.Type.Simple : Image.Type.Sliced;
            image.color = sr.color;
            image.raycastTarget = false;
            if (colors != null) colors[image] = sr.color;
        }
        return rect;
    }

    // Active all the way up to the source's top (a prefab asset isn't in a scene, so no activeInHierarchy)
    static bool Shown(Transform t, Transform top)
    {
        for (; t != null; t = t == top ? null : t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }
}
