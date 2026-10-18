using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The level-select map, laid out as a top-down TREE. Each node draws an edge up to every one of
// its prerequisites (its parents), so a node can branch into two children or two paths can merge
// back into one. Edges run in the vertical gaps between rows and are drawn behind the opaque node
// boxes, so a line never cuts across a label. Node colour shows state: green = cleared,
// gold = available, grey = locked. Alpha UI: plain OnGUI, matching HUDController and LevelLoader.
public class WorldMapController : MonoBehaviour
{
    public Vector2 nodeSize = new Vector2(150f, 46f);
    public bool showResetButton = true;   // dev helper

    static readonly Color BgColor        = new Color(0.11f, 0.13f, 0.18f, 1f);
    static readonly Color PanelColor     = new Color(1f, 1f, 1f, 0.04f);
    static readonly Color DoneColor      = new Color(0.26f, 0.70f, 0.36f, 1f);
    static readonly Color OpenColor      = new Color(0.98f, 0.78f, 0.26f, 1f);
    static readonly Color LockColor      = new Color(0.33f, 0.36f, 0.42f, 1f);
    static readonly Color EdgeOpen       = new Color(0.98f, 0.82f, 0.40f, 0.85f);
    static readonly Color EdgeLocked     = new Color(1f, 1f, 1f, 0.14f);

    GUIStyle title;
    GUIStyle legend;
    GUIStyle nodeStyle;

    void OnGUI()
    {
        WorldMap map = GameFlow.Instance != null ? GameFlow.Instance.Map : null;

        // Backdrop so the empty space doesn't read as "broken".
        GUI.color = BgColor;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (map == null)
        {
            GUI.Label(new Rect(20, 20, 640, 30), "No WorldMap. Run Tools > Gold Miner > Build Flow Scenes.");
            return;
        }

        EnsureStyles();
        GUI.Label(new Rect(0, 14, Screen.width, 40), "WORLD MAP", title);

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
            if (done) label = "\u2713 " + label;        // tick
            else if (!unlocked) label = "\u25cf " + label;  // locked dot

            GUI.backgroundColor = done ? DoneColor : unlocked ? OpenColor : LockColor;
            GUI.enabled = unlocked;
            if (GUI.Button(rect, label, nodeStyle)) GameFlow.Instance.EnterNode(node);
            GUI.enabled = true;
        }
        GUI.backgroundColor = savedBg;

        GUI.Label(new Rect(16, Screen.height - 44, Screen.width - 32, 28),
            "\u2713 cleared   \u25a0 available   \u25a0 locked   \u2014 clear a node to open the ones it points to.", legend);

        if (showResetButton && GUI.Button(new Rect(Screen.width - 150, Screen.height - 44, 134, 28), "Reset progress"))
            ProgressStore.ResetAll();
    }

    Rect NodeRect(MapNode node)
    {
        float x = node.mapPosition.x * Screen.width - nodeSize.x * 0.5f;
        float y = node.mapPosition.y * Screen.height - nodeSize.y * 0.5f;
        return new Rect(x, y, nodeSize.x, nodeSize.y);
    }

    // One line per (parent -> child) edge. We draw from the bottom edge of the parent box to the
    // top edge of the child box, so the segment lives in the gap between the two rows and never
    // runs through a box/label.
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
                DrawLine(BottomAnchor(parent), TopAnchor(child), edgeHot ? EdgeOpen : EdgeLocked, 3f);
            }
        }
    }

    Vector2 Center(MapNode node) =>
        new Vector2(node.mapPosition.x * Screen.width, node.mapPosition.y * Screen.height);

    Vector2 BottomAnchor(MapNode node)
    {
        Vector2 c = Center(node);
        return new Vector2(c.x, c.y + nodeSize.y * 0.5f);
    }

    Vector2 TopAnchor(MapNode node)
    {
        Vector2 c = Center(node);
        return new Vector2(c.x, c.y - nodeSize.y * 0.5f);
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
                fontSize = 13,
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
