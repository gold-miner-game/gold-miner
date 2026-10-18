using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The level-select map. Draws one button per WorldMap node at its position. Unlocked nodes are
// clickable and enter the node through GameFlow; locked ones are greyed out; cleared ones show a
// tick. Alpha UI: plain OnGUI, matching HUDController and LevelLoader.
public class WorldMapController : MonoBehaviour
{
    public Vector2 nodeSize = new Vector2(170f, 54f);
    public bool showResetButton = true;   // dev helper

    GUIStyle title;
    GUIStyle legend;

    void OnGUI()
    {
        WorldMap map = GameFlow.Instance.Map;
        if (map == null)
        {
            GUI.Label(new Rect(20, 20, 600, 30), "No WorldMap. Run Tools > Gold Miner > Build Flow Scenes.");
            return;
        }

        EnsureStyles();
        GUI.Label(new Rect(0, 16, Screen.width, 40), "WORLD MAP", title);

        DrawConnections(map);

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

            GUI.enabled = unlocked;
            if (GUI.Button(rect, label)) GameFlow.Instance.EnterNode(node);
            GUI.enabled = true;
        }

        GUI.Label(new Rect(16, Screen.height - 46, Screen.width - 32, 30),
            "Tick = cleared.  Grey = locked.  Clear a node to unlock the next.", legend);

        if (showResetButton && GUI.Button(new Rect(Screen.width - 150, Screen.height - 44, 134, 30), "Reset progress"))
            ProgressStore.ResetAll();
    }

    Rect NodeRect(MapNode node)
    {
        float x = node.mapPosition.x * Screen.width - nodeSize.x * 0.5f;
        float y = node.mapPosition.y * Screen.height - nodeSize.y * 0.5f;
        return new Rect(x, y, nodeSize.x, nodeSize.y);
    }

    // Thin lines between consecutive nodes, so the path reads as a chain.
    void DrawConnections(WorldMap map)
    {
        for (int i = 1; i < map.nodes.Count; i++)
        {
            if (map.nodes[i] == null || map.nodes[i - 1] == null) continue;
            Vector2 a = Center(map.nodes[i - 1]);
            Vector2 b = Center(map.nodes[i]);
            DrawLine(a, b, new Color(1f, 1f, 1f, 0.35f), 3f);
        }
    }

    Vector2 Center(MapNode node)
    {
        return new Vector2(node.mapPosition.x * Screen.width, node.mapPosition.y * Screen.height);
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
            legend = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            legend.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }
    }
}
