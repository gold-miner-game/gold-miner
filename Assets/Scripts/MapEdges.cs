using UnityEngine;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// Draws every parent -> child connector of the world map as one UI mesh of smooth bezier curves.
// It runs in the editor too, so the lines follow the cards while you drag them in the Scene view.
// Put it on a full-size RectTransform behind the node cards.
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class MapEdges : MaskableGraphic
{
    public WorldMapController controller;
    public float width = 4.5f;
    [Range(4, 64)] public int segments = 26;

    int lastHash;

    void Update()
    {
        int hash = controller != null ? controller.LayoutHash() : 0;
        if (hash == lastHash) return;
        lastHash = hash;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (controller == null) return;
        controller.ForEachEdge((parent, child, hot) =>
        {
            Rect from = LocalRect(parent.Rect);
            Rect to = LocalRect(child.Rect);
            Color c = hot ? controller.edgeHot : controller.edgeCold;
            if (controller.horizontal)
                AddBezier(vh, new Vector2(from.xMax, from.center.y), new Vector2(to.xMin, to.center.y), Vector2.right, c);
            else
                AddBezier(vh, new Vector2(from.center.x, from.yMin), new Vector2(to.center.x, to.yMax), Vector2.down, c);
        });
    }

    // A card's rect in this graphic's local space
    Rect LocalRect(RectTransform card)
    {
        Rect r = card.rect;
        Vector2 min = rectTransform.InverseTransformPoint(card.TransformPoint(r.min));
        Vector2 max = rectTransform.InverseTransformPoint(card.TransformPoint(r.max));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    void AddBezier(VertexHelper vh, Vector2 p0, Vector2 p3, Vector2 flow, Color color)
    {
        float reach = Mathf.Abs(Vector2.Dot(p3 - p0, flow));
        float handle = Mathf.Max(39f, reach * 0.55f);
        Vector2 p1 = p0 + flow * handle;
        Vector2 p2 = p3 - flow * handle;

        Vector2 prev = p0;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1f - t;
            Vector2 pt = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            AddSegment(vh, prev, pt, color);
            prev = pt;
        }
    }

    void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, Color color)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 0.0001f) return;
        // Extend each piece a little so neighbouring pieces overlap at the bends instead of leaving gaps
        dir.Normalize();
        Vector2 side = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        Vector2 ext = dir * (width * 0.25f);
        a -= ext;
        b += ext;

        int start = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = a - side; vh.AddVert(v);
        v.position = a + side; vh.AddVert(v);
        v.position = b + side; vh.AddVert(v);
        v.position = b - side; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
