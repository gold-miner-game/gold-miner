using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The level-select map, laid out as a TREE. By default it flows LEFT -> RIGHT: the trunk's depth
// runs along the x axis and branches spread along y, so one node can split into two children (same
// parent) and two paths can merge back (one child, two parents). Set `horizontal = false` to flow
// top -> bottom instead. Node positions are stored normalised as (spread = x, depth = y); this
// controller projects them for the chosen orientation into a padded area that leaves room for the
// title and legend, and sizes the boxes from the spacing so rows/columns never overlap and a line
// never crosses a label (edges are drawn behind the opaque boxes, border to border).
// Node colour shows state: green = cleared, gold = available, grey = locked. Alpha UI: plain OnGUI.
public class WorldMapController : MonoBehaviour
{
    [Tooltip("Left-to-right tree. Off = top-to-bottom.")]
    public bool horizontal = true;
    public Vector2 maxNodeSize = new Vector2(170f, 54f);
    public bool showResetButton = true;   // dev helper

    static readonly Color BgColor    = new Color(0.11f, 0.13f, 0.18f, 1f);
    static readonly Color DoneColor  = new Color(0.26f, 0.70f, 0.36f, 1f);
    static readonly Color OpenColor  = new Color(0.98f, 0.78f, 0.26f, 1f);
    static readonly Color LockColor  = new Color(0.33f, 0.36f, 0.42f, 1f);
    static readonly Color EdgeOpen   = new Color(0.98f, 0.82f, 0.40f, 0.85f);
    static readonly Color EdgeLocked = new Color(1f, 1f, 1f, 0.14f);

    const float MarginSide = 20f;
    const float MarginTop = 66f;    // keeps the title clear
    const float MarginBottom = 48f; // keeps the legend clear

    GUIStyle title;
    GUIStyle legend;
    GUIStyle nodeStyle;

    Rect area;        // where node centres may land
    Vector2 nodeSize; // computed each frame from the spacing

    void OnGUI()
    {
        WorldMap map = GameFlow.Instance != null ? GameFlow.Instance.Map : null;

        GUI.color = BgColor;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (map == null)
        {
            GUI.Label(new Rect(20, 20, 640, 30), "No WorldMap. Run Tools > Gold Miner > Build Flow Scenes.");
            return;
        }

        EnsureStyles();
        Layout(map);
        GUI.Label(new Rect(0, 16, Screen.width, 40), "WORLD MAP", title);

        // 1) Edges first, so the opaque node boxes always sit on top of them.
        DrawEdges(map);

        // 2) Nodes.
        Color savedBg = GUI.backgroundColor;
        for (int i = 0; i < map.nodes.Count; i++)
        {
            MapNode node = map.nodes[i];
            if (node == null) continue;

            Rect rect = NodeRect(node);
            bool unlocked = map.IsUnlocked(i);
            bool done = ProgressStore.IsCompleted(node.id);

            string label = node.displayName;
            if (done) label = "\u2713 " + label;            // tick
            else if (!unlocked) label = "\u25cf " + label;  // locked dot

            GUI.backgroundColor = done ? DoneColor : unlocked ? OpenColor : LockColor;
            GUI.enabled = unlocked;
            if (GUI.Button(rect, label, nodeStyle)) GameFlow.Instance.EnterNode(node);
            GUI.enabled = true;
        }
        GUI.backgroundColor = savedBg;

        GUI.Label(new Rect(16, Screen.height - 44, Screen.width - 170, 28),
            "\u2713 cleared   \u25a0 available   \u25a0 locked   \u2014 clear a node to open the ones it points to.", legend);

        if (showResetButton && GUI.Button(new Rect(Screen.width - 150, Screen.height - 44, 134, 28), "Reset progress"))
            ProgressStore.ResetAll();
    }

    // Work out the usable area and a node size that fits the tightest spacing on each axis.
    void Layout(WorldMap map)
    {
        var depths = new List<float>();   // along the trunk  (stored as y)
        var spreads = new List<float>();  // across branches  (stored as x)
        foreach (MapNode n in map.nodes)
        {
            if (n == null) continue;
            depths.Add(n.mapPosition.y);
            spreads.Add(n.mapPosition.x);
        }

        float spanW = Mathf.Max(1f, Screen.width - 2f * MarginSide);
        float spanH = Mathf.Max(1f, Screen.height - MarginTop - MarginBottom);

        // The axis a value drives depends on orientation.
        float gapAlongX = horizontal ? MinGap(depths) : MinGap(spreads);
        float gapAlongY = horizontal ? MinGap(spreads) : MinGap(depths);

        float w = Mathf.Clamp(gapAlongX * spanW * 0.92f, 56f, maxNodeSize.x);
        float h = Mathf.Clamp(gapAlongY * spanH * 0.80f, 28f, maxNodeSize.y);
        nodeSize = new Vector2(w, h);

        area = Rect.MinMaxRect(
            MarginSide + w * 0.5f,
            MarginTop + h * 0.5f,
            Screen.width - MarginSide - w * 0.5f,
            Screen.height - MarginBottom - h * 0.5f);
    }

    Vector2 Center(MapNode node)
    {
        float depth = node.mapPosition.y;   // 0..1 along the trunk
        float spread = node.mapPosition.x;  // 0..1 across branches
        float u = horizontal ? depth : spread;   // horizontal-axis coordinate
        float v = horizontal ? spread : depth;    // vertical-axis coordinate
        return new Vector2(Mathf.Lerp(area.xMin, area.xMax, u), Mathf.Lerp(area.yMin, area.yMax, v));
    }

    Rect NodeRect(MapNode node)
    {
        Vector2 c = Center(node);
        return new Rect(c.x - nodeSize.x * 0.5f, c.y - nodeSize.y * 0.5f, nodeSize.x, nodeSize.y);
    }

    // One line per (parent -> child) edge, from the parent's trailing edge to the child's leading
    // edge (right->left when horizontal, bottom->top when vertical), so it lives in the gap between
    // the two and never runs through a box/label.
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
                bool edgeHot = childOpen || ProgressStore.IsCompleted(parent.id);
                DrawLine(ExitAnchor(parent), EntryAnchor(child), edgeHot ? EdgeOpen : EdgeLocked, 3f);
            }
        }
    }

    Vector2 ExitAnchor(MapNode node)   // where an edge leaves a parent
    {
        Vector2 c = Center(node);
        return horizontal ? new Vector2(c.x + nodeSize.x * 0.5f, c.y) : new Vector2(c.x, c.y + nodeSize.y * 0.5f);
    }

    Vector2 EntryAnchor(MapNode node)  // where an edge reaches a child
    {
        Vector2 c = Center(node);
        return horizontal ? new Vector2(c.x - nodeSize.x * 0.5f, c.y) : new Vector2(c.x, c.y - nodeSize.y * 0.5f);
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

    void EnsureStyles()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            title.normal.textColor = Color.white;
        }
        if (legend == null)
        {
            legend = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            legend.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }
        if (nodeStyle == null)
        {
            nodeStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            nodeStyle.normal.textColor = Color.white;
            nodeStyle.hover.textColor = Color.white;
            nodeStyle.active.textColor = Color.white;
        }
    }
}
