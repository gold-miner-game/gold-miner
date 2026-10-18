using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The level-select map, drawn as a TREE. By default it flows LEFT -> RIGHT (set horizontal = false
// for top -> bottom). A node opens when it has no parents, is already cleared, or ANY of its
// prerequisites is cleared, so one node can split into two and two paths can merge back (e.g. the
// Store hangs off Pick Character on its own spur).
//
// The look is pure OnGUI but intentionally not bare: a vertical gradient backdrop, soft smooth
// bezier connectors behind the nodes, and rounded "cards" with a drop shadow, an accent outline and
// a hover lift. All of its textures are generated once at runtime (no art assets). State reads from
// the accent + card tone: green = cleared, amber = available (with a soft glow), grey = locked.
public class WorldMapController : MonoBehaviour
{
    [Tooltip("Left-to-right tree. Off = top-to-bottom.")]
    public bool horizontal = true;
    public Vector2 maxNodeSize = new Vector2(200f, 78f);
    public bool showResetButton = true;   // dev helper

    // Palette ------------------------------------------------------------------------------------
    static readonly Color BgTop      = new Color(0.10f, 0.12f, 0.17f, 1f);
    static readonly Color BgBottom   = new Color(0.05f, 0.06f, 0.09f, 1f);
    static readonly Color CardAvail  = new Color(0.17f, 0.20f, 0.27f, 1f);
    static readonly Color CardHover  = new Color(0.22f, 0.26f, 0.34f, 1f);
    static readonly Color CardDone   = new Color(0.14f, 0.21f, 0.18f, 1f);
    static readonly Color CardLock   = new Color(0.115f, 0.13f, 0.17f, 1f);
    static readonly Color AccDone    = new Color(0.33f, 0.82f, 0.48f, 1f);
    static readonly Color AccAvail   = new Color(0.98f, 0.80f, 0.33f, 1f);
    static readonly Color AccLock    = new Color(0.32f, 0.35f, 0.42f, 1f);
    static readonly Color TextBright = new Color(0.94f, 0.96f, 0.98f, 1f);
    static readonly Color TextDim    = new Color(0.52f, 0.55f, 0.63f, 1f);
    static readonly Color Shadow     = new Color(0f, 0f, 0f, 0.33f);

    const float MarginSide = 36f;
    const float MarginTop = 64f;     // title only
    const float MarginBottom = 28f;
    const int Radius = 12;

    GUIStyle title;
    GUIStyle nodeText;
    GUIStyle roundBox;   // 9-sliced rounded background, tinted by GUI.backgroundColor

    Texture2D gradientTex;
    Texture2D roundTex;

    Rect area;
    Vector2 nodeSize;
    float depthMin, depthMax, spreadMin, spreadMax;
    bool built;

    void OnGUI()
    {
        Build();
        WorldMap map = GameFlow.Instance != null ? GameFlow.Instance.Map : null;

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), gradientTex, ScaleMode.StretchToFill);

        if (map == null)
        {
            GUI.Label(new Rect(24, 24, 680, 30), "No WorldMap. Run Tools > Gold Miner > Build Flow Scenes.");
            return;
        }

        Layout(map);

        GUI.Label(new Rect(0, 16, Screen.width, 36), "WORLD MAP", title);

        DrawEdges(map);                 // behind the cards
        for (int i = 0; i < map.nodes.Count; i++) DrawNode(map, i);

        if (showResetButton && GUI.Button(new Rect(Screen.width - 150, Screen.height - 40, 134, 28), "Reset progress"))
            ProgressStore.ResetAll();
    }

    // --- nodes ----------------------------------------------------------------------------------

    void DrawNode(WorldMap map, int i)
    {
        MapNode node = map.nodes[i];
        if (node == null) return;

        Rect rect = NodeRect(node);
        bool unlocked = map.IsUnlocked(i);
        bool done = ProgressStore.IsCompleted(node.id);
        bool hover = unlocked && rect.Contains(Event.current.mousePosition);

        Color accent = done ? AccDone : unlocked ? AccAvail : AccLock;
        Color card = done ? CardDone : !unlocked ? CardLock : hover ? CardHover : CardAvail;

        DrawRoundRect(Shift(rect, 0f, 5f), Shadow);                       // drop shadow
        if (unlocked && !done)                                            // soft glow on the next steps
            DrawRoundRect(Expand(rect, 6f), new Color(accent.r, accent.g, accent.b, 0.16f));
        DrawRoundRect(Expand(rect, 1.5f), done || unlocked ? accent : new Color(accent.r, accent.g, accent.b, 0.55f));
        DrawRoundRect(rect, card);                                        // card face

        nodeText.normal.textColor = unlocked ? TextBright : TextDim;
        string label = (done ? "\u2713  " : "") + node.displayName;
        GUI.Label(rect, label, nodeText);

        GUI.enabled = unlocked;
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) GameFlow.Instance.EnterNode(node);
        GUI.enabled = true;
    }

    // --- connectors -----------------------------------------------------------------------------

    void DrawEdges(WorldMap map)
    {
        for (int i = 0; i < map.nodes.Count; i++)
        {
            MapNode child = map.nodes[i];
            if (child == null || child.prerequisites == null) continue;
            bool childOpen = map.IsUnlocked(i) || ProgressStore.IsCompleted(child.id);

            for (int p = 0; p < child.prerequisites.Length; p++)
            {
                MapNode parent = map.Find(child.prerequisites[p]);
                if (parent == null) continue;
                bool hot = childOpen || ProgressStore.IsCompleted(parent.id);
                Color c = hot ? new Color(AccAvail.r, AccAvail.g, AccAvail.b, 0.8f) : new Color(1f, 1f, 1f, 0.09f);
                DrawBezier(ExitAnchor(parent), EntryAnchor(child), c, 3f);
            }
        }
    }

    void DrawBezier(Vector2 p0, Vector2 p3, Color color, float width)
    {
        Vector2 flow = horizontal ? Vector2.right : Vector2.up;
        float reach = horizontal ? Mathf.Abs(p3.x - p0.x) : Mathf.Abs(p3.y - p0.y);
        float handle = Mathf.Max(26f, reach * 0.55f);
        Vector2 p1 = p0 + flow * handle;
        Vector2 p2 = p3 - flow * handle;

        const int seg = 26;
        Vector2 prev = p0;
        for (int i = 1; i <= seg; i++)
        {
            float t = i / (float)seg;
            float u = 1f - t;
            Vector2 pt = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            DrawLine(prev, pt, color, width);
            prev = pt;
        }
    }

    // --- layout ---------------------------------------------------------------------------------

    void Layout(WorldMap map)
    {
        var depths = new List<float>();
        var spreads = new List<float>();
        foreach (MapNode n in map.nodes)
        {
            if (n == null) continue;
            depths.Add(n.mapPosition.y);
            spreads.Add(n.mapPosition.x);
        }

        // Stretch whatever range the nodes actually use across the whole area, so a tree that only
        // occupies the middle of 0..1 still gets real gaps instead of sitting in a tight cluster.
        depthMin = Min(depths); depthMax = Max(depths);
        spreadMin = Min(spreads); spreadMax = Max(spreads);

        float spanW = Mathf.Max(1f, Screen.width - 2f * MarginSide);
        float spanH = Mathf.Max(1f, Screen.height - MarginTop - MarginBottom);
        float depthSpan = Mathf.Max(0.0001f, depthMax - depthMin);
        float spreadSpan = Mathf.Max(0.0001f, spreadMax - spreadMin);
        float gapX = (horizontal ? MinGap(depths) / depthSpan : MinGap(spreads) / spreadSpan);
        float gapY = (horizontal ? MinGap(spreads) / spreadSpan : MinGap(depths) / depthSpan);

        // Stay under the full gap so neighbouring cards don't touch once they get bigger.
        float w = Mathf.Clamp(gapX * spanW * 0.78f, 110f, maxNodeSize.x);
        float h = Mathf.Clamp(gapY * spanH * 0.58f, 56f, maxNodeSize.y);
        nodeSize = new Vector2(w, h);
        area = Rect.MinMaxRect(MarginSide + w * 0.5f, MarginTop + h * 0.5f,
                               Screen.width - MarginSide - w * 0.5f, Screen.height - MarginBottom - h * 0.5f);
    }

    Vector2 Center(MapNode node)
    {
        float depth = Mathf.InverseLerp(depthMin, depthMax, node.mapPosition.y);
        // Pull the branches in slightly so the outer rows clear the title and the bottom edge,
        // while still sitting much further apart than the raw 0..1 coordinates.
        float spread = Mathf.Lerp(0.06f, 0.94f, Mathf.InverseLerp(spreadMin, spreadMax, node.mapPosition.x));
        float u = horizontal ? depth : spread;
        float v = horizontal ? spread : depth;
        return new Vector2(Mathf.Lerp(area.xMin, area.xMax, u), Mathf.Lerp(area.yMin, area.yMax, v));
    }

    Rect NodeRect(MapNode node)
    {
        Vector2 c = Center(node);
        return new Rect(c.x - nodeSize.x * 0.5f, c.y - nodeSize.y * 0.5f, nodeSize.x, nodeSize.y);
    }

    Vector2 ExitAnchor(MapNode node)
    {
        Vector2 c = Center(node);
        return horizontal ? new Vector2(c.x + nodeSize.x * 0.5f, c.y) : new Vector2(c.x, c.y + nodeSize.y * 0.5f);
    }

    Vector2 EntryAnchor(MapNode node)
    {
        Vector2 c = Center(node);
        return horizontal ? new Vector2(c.x - nodeSize.x * 0.5f, c.y) : new Vector2(c.x, c.y - nodeSize.y * 0.5f);
    }

    // --- helpers --------------------------------------------------------------------------------

    static float Min(List<float> values)
    {
        float m = values.Count > 0 ? values[0] : 0f;
        for (int i = 1; i < values.Count; i++) if (values[i] < m) m = values[i];
        return m;
    }

    static float Max(List<float> values)
    {
        float m = values.Count > 0 ? values[0] : 1f;
        for (int i = 1; i < values.Count; i++) if (values[i] > m) m = values[i];
        return m;
    }

    static Rect Expand(Rect r, float p) => new Rect(r.x - p, r.y - p, r.width + 2f * p, r.height + 2f * p);
    static Rect Shift(Rect r, float dx, float dy) => new Rect(r.x + dx, r.y + dy, r.width, r.height);

    void DrawRoundRect(Rect r, Color color)
    {
        if (r.width < 2f * Radius || r.height < 2f * Radius)
        {
            Color prev = GUI.color; GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            return;
        }
        Color pb = GUI.backgroundColor; GUI.backgroundColor = color;
        GUI.Box(r, GUIContent.none, roundBox);
        GUI.backgroundColor = pb;
    }

    static float MinGap(List<float> values)
    {
        values.Sort();
        float min = 1f;
        for (int i = 1; i < values.Count; i++)
        {
            float d = values[i] - values[i - 1];
            if (d > 0.0001f && d < min) min = d;
        }
        return min;
    }

    static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 saved = GUI.matrix;
        Color savedColor = GUI.color;
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        float length = Vector2.Distance(a, b);
        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), Texture2D.whiteTexture);
        GUI.matrix = saved;
        GUI.color = savedColor;
    }

    // --- one-time texture + style build ---------------------------------------------------------

    void Build()
    {
        if (built) return;
        built = true;

        gradientTex = new Texture2D(1, 64, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int i = 0; i < 64; i++)
            gradientTex.SetPixel(0, i, Color.Lerp(BgBottom, BgTop, i / 63f));
        gradientTex.Apply();

        roundTex = MakeRounded(Radius);

        roundBox = new GUIStyle { border = new RectOffset(Radius, Radius, Radius, Radius) };
        roundBox.normal.background = roundTex;

        title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = TextBright;
        nodeText = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true,
            padding = new RectOffset(10, 10, 6, 6),
        };
    }

    static Texture2D MakeRounded(int radius)
    {
        int size = 2 * radius + 2;
        var t = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - dist + 0.5f);   // smooth 1px edge
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }
}
