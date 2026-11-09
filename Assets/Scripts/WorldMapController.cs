using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// The level-select map. It is a normal uGUI canvas saved in the Map scene (built once by
// Tools > Gold Miner > Build Map UI), so the cards, title, buttons and background can all be moved
// and restyled in the Scene view. Each card is a MapNodeView whose nodeId points at a node in
// WorldMap.asset; the asset still owns the gameplay side (type, level, prerequisites).
//
// A node opens when it has no parents, is already cleared, or ANY of its prerequisites is cleared,
// so one node can split into two and two paths can merge back. State reads from the accent + card
// tone: green = cleared, amber = available (with a soft glow), grey = locked. MapEdges draws the
// connectors between the cards, and follows them while you drag them in the editor.
[ExecuteAlways]
public class WorldMapController : MonoBehaviour
{
    public enum NodeState { Locked, Available, Cleared }

    [Tooltip("The node list. Empty = Resources/WorldMap (the one GameFlow uses).")]
    public WorldMap map;
    [Tooltip("Parent of the MapNodeView cards.")]
    public RectTransform nodeLayer;
    public MapEdges edges;
    [Tooltip("Connectors leave a card on its right and enter on the left. Off = bottom to top.")]
    public bool horizontal = true;
    public Button menuButton;
    [Tooltip("Dev helper: wipes saved progress.")]
    public Button resetButton;

    [Header("Palette")]
    public Color cardAvailable = new Color(0.17f, 0.20f, 0.27f, 1f);
    public Color cardHover     = new Color(0.22f, 0.26f, 0.34f, 1f);
    public Color cardCleared   = new Color(0.14f, 0.21f, 0.18f, 1f);
    public Color cardLocked    = new Color(0.115f, 0.13f, 0.17f, 1f);
    public Color accentCleared = new Color(0.33f, 0.82f, 0.48f, 1f);
    public Color accentAvailable = new Color(0.98f, 0.80f, 0.33f, 1f);
    public Color accentLocked  = new Color(0.32f, 0.35f, 0.42f, 1f);
    public Color textBright    = new Color(0.94f, 0.96f, 0.98f, 1f);
    public Color textDim       = new Color(0.52f, 0.55f, 0.63f, 1f);
    public Color edgeHot       = new Color(0.98f, 0.80f, 0.33f, 0.8f);
    public Color edgeCold      = new Color(1f, 1f, 1f, 0.09f);

    MapNodeView[] views = new MapNodeView[0];

    public WorldMap Map => map != null ? map : Resources.Load<WorldMap>("WorldMap");

    void Start()
    {
        if (!Application.isPlaying) return;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        if (menuButton != null) menuButton.onClick.AddListener(() => GameFlow.Instance.OpenMenu());
        if (resetButton != null) resetButton.onClick.AddListener(() => { ProgressStore.ResetAll(); Refresh(); });

        WorldMap m = Map;
        if (m == null)
        {
            Debug.LogWarning("WorldMapController: no WorldMap. Run Tools > Gold Miner > Build Flow Scenes.");
            return;
        }

        views = FindViews();
        foreach (MapNodeView view in views)
        {
            MapNode node = m.Find(view.nodeId);
            if (node == null)
            {
                Debug.LogWarning("WorldMapController: card '" + view.name + "' has nodeId '" + view.nodeId + "', which is not in the WorldMap.", view);
                continue;
            }
            if (view.button != null) view.button.onClick.AddListener(() => GameFlow.Instance.EnterNode(node));
        }
        foreach (MapNode node in m.nodes)
            if (node != null && Find(node.id) == null)
                Debug.LogWarning("WorldMapController: WorldMap node '" + node.id + "' has no card in the Map scene. Run Tools > Gold Miner > Build Map UI to add it.");

        Refresh();
    }

    // Colour every card by its saved state
    public void Refresh()
    {
        WorldMap m = Map;
        if (m == null) return;
        foreach (MapNodeView view in views)
        {
            int index = m.IndexOf(m.Find(view.nodeId));
            if (index < 0) { view.Show(this, NodeState.Locked); continue; }
            view.Show(this, StateOf(m, index));
        }
        if (edges != null) edges.SetVerticesDirty();
    }

    static NodeState StateOf(WorldMap m, int index)
    {
        if (ProgressStore.IsCompleted(m.nodes[index].id)) return NodeState.Cleared;
        return m.IsUnlocked(index) ? NodeState.Available : NodeState.Locked;
    }

    public Color AccentFor(NodeState s) => s == NodeState.Cleared ? accentCleared : s == NodeState.Available ? accentAvailable : accentLocked;
    public Color CardFor(NodeState s) => s == NodeState.Cleared ? cardCleared : s == NodeState.Available ? cardAvailable : cardLocked;

    // --- connectors (used by MapEdges) -----------------------------------------------------------

    // Every parent -> child pair that has a card at both ends. The Store hangs off the tree on its
    // own, so it gets no line. In the editor every line is drawn "hot" so the layout is easy to see.
    public void ForEachEdge(Action<MapNodeView, MapNodeView, bool> draw)
    {
        WorldMap m = Map;
        if (m == null) return;
        if (!Application.isPlaying) views = FindViews();

        foreach (MapNodeView childView in views)
        {
            if (childView == null || !childView.isActiveAndEnabled) continue;
            MapNode child = m.Find(childView.nodeId);
            if (child == null || child.type == NodeType.Store || child.prerequisites == null) continue;
            bool childOpen = Application.isPlaying && (m.IsUnlocked(child) || ProgressStore.IsCompleted(child.id));

            foreach (string parentId in child.prerequisites)
            {
                MapNodeView parentView = Find(parentId);
                if (parentView == null || !parentView.isActiveAndEnabled) continue;
                bool hot = !Application.isPlaying || childOpen || ProgressStore.IsCompleted(parentId);
                draw(parentView, childView, hot);
            }
        }
    }

    // Changes when a card moves, resizes or is added/removed, so MapEdges knows to redraw
    public int LayoutHash()
    {
        if (!Application.isPlaying) views = FindViews();
        int hash = views.Length + (horizontal ? 7 : 0);
        foreach (MapNodeView view in views)
        {
            if (view == null) continue;
            RectTransform r = view.Rect;
            hash = hash * 31 + r.position.GetHashCode();
            hash = hash * 31 + r.rect.size.GetHashCode();
            hash = hash * 31 + r.lossyScale.GetHashCode();
            hash = hash * 31 + (view.isActiveAndEnabled ? 1 : 0);
            hash = hash * 31 + (view.nodeId != null ? view.nodeId.GetHashCode() : 0);
        }
        if (edges != null) hash = hash * 31 + edges.rectTransform.position.GetHashCode();
        return hash;
    }

    MapNodeView Find(string id)
    {
        foreach (MapNodeView view in views)
            if (view != null && view.nodeId == id) return view;
        return null;
    }

    MapNodeView[] FindViews()
    {
        Transform root = nodeLayer != null ? nodeLayer : transform;
        return root.GetComponentsInChildren<MapNodeView>(true);
    }

    // Also used by MainMenu, so the menu matches the map
    internal static Texture2D MakeRounded(int radius)
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
