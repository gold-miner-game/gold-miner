using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Build Map UI: builds the world map canvas in the open scene (the Map scene)
// and wires WorldMapController, so the map can be edited in the Scene view. The first run lays the
// cards out from WorldMap.asset's mapPosition values. Later runs never move or restyle anything you
// edited; they only add a card for each WorldMap node that doesn't have one yet (e.g. a new level).
public static class MapUIBuilder
{
    const string MapAssetPath = "Assets/Resources/WorldMap.asset";

    // Laid out in the 1920x1080 frame ScreenFrame fits every screen canvas to
    const float Width = 1920f;
    const float Height = 1080f;
    const float MarginSide = 54f;
    const float MarginTop = 96f;      // title only
    const float MarginBottom = 42f;
    static readonly Vector2 MaxNodeSize = new Vector2(300f, 117f);
    static readonly Vector2 MinNodeSize = new Vector2(165f, 84f);

    static readonly Color Background = new Color(0.075f, 0.09f, 0.13f, 1f);
    static readonly Color Shadow = new Color(0f, 0f, 0f, 0.33f);
    static readonly Color ButtonColor = new Color(0.17f, 0.20f, 0.27f, 1f);

    [MenuItem("Tools/Gold Miner/Build Map UI")]
    static void BuildMenu()
    {
        Build(null);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Gold Miner/Build Map UI", true)]
    static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // Uses the scene's WorldMapController (or makes one). Safe to run again: only missing cards are added
    public static void Build(WorldMapController controller)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("MapUIBuilder: no default TMP font. Import it via Window > TextMeshPro > Import TMP Essential Resources, then try again.");
            return;
        }

        WorldMap map = AssetDatabase.LoadAssetAtPath<WorldMap>(MapAssetPath);
        if (map == null)
        {
            Debug.LogError("MapUIBuilder: no " + MapAssetPath + ". Run Tools > Gold Miner > Build Flow Scenes first.");
            return;
        }

        if (controller == null) controller = Object.FindFirstObjectByType<WorldMapController>();
        if (controller == null)
        {
            controller = new GameObject("WorldMapController").AddComponent<WorldMapController>();
            Undo.RegisterCreatedObjectUndo(controller.gameObject, "Build Map UI");
        }
        Undo.RecordObject(controller, "Build Map UI");
        controller.map = map;

        if (controller.nodeLayer == null) BuildCanvas(controller);
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(events, "Build Map UI");
        }

        int added = AddMissingCards(controller, map);
        EditorUtility.SetDirty(controller);
        Selection.activeGameObject = controller.nodeLayer.gameObject;
        Debug.Log("MapUIBuilder: map UI ready, " + added + " card(s) added. Move the cards under '" + controller.nodeLayer.name +
                  "' in the Scene view; save the scene to keep it.");
    }

    static void BuildCanvas(WorldMapController controller)
    {
        GameObject root = new GameObject("MapCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(root, "Build Map UI");
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // what ScreenFrame sets at runtime
        Transform canvas = root.transform;

        Image background = CreateImage(canvas, "Background", Background, false);
        Stretch(background.rectTransform, 0f);

        TMP_Text title = CreateText(canvas, "Title", "WORLD MAP", 42f);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(800f, 54f));
        title.fontStyle = FontStyles.Bold;

        RectTransform edgeLayer = new GameObject("Edges", typeof(RectTransform), typeof(CanvasRenderer), typeof(MapEdges)).GetComponent<RectTransform>();
        edgeLayer.SetParent(canvas, false);
        Stretch(edgeLayer, 0f);
        MapEdges edges = edgeLayer.GetComponent<MapEdges>();
        edges.controller = controller;
        edges.raycastTarget = false;

        RectTransform nodes = new GameObject("Nodes", typeof(RectTransform)).GetComponent<RectTransform>();
        nodes.SetParent(canvas, false);
        Stretch(nodes, 0f);

        Button menu = CreateButton(canvas, "MenuButton", "Menu", new Vector2(0f, 1f), new Vector2(24f, -30f), new Vector2(144f, 45f));
        Button reset = CreateButton(canvas, "ResetButton", "Reset progress", new Vector2(1f, 0f), new Vector2(-24f, 18f), new Vector2(201f, 42f));

        controller.nodeLayer = nodes;
        controller.edges = edges;
        controller.menuButton = menu;
        controller.resetButton = reset;
    }

    static int AddMissingCards(WorldMapController controller, WorldMap map)
    {
        var have = new HashSet<string>();
        foreach (MapNodeView view in controller.nodeLayer.GetComponentsInChildren<MapNodeView>(true))
            have.Add(view.nodeId);

        Layout layout = new Layout(map);
        int added = 0;
        foreach (MapNode node in map.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id) || have.Contains(node.id)) continue;
            CreateCard(controller, node, layout.RectOf(node));
            added++;
        }
        return added;
    }

    static void CreateCard(WorldMapController controller, MapNode node, Rect topLeftRect)
    {
        GameObject go = new GameObject("Node_" + node.id, typeof(RectTransform), typeof(MapNodeView), typeof(Button));
        Undo.RegisterCreatedObjectUndo(go, "Build Map UI");
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(controller.nodeLayer, false);
        // Anchored to the middle of the frame; position = card centre, y up
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = topLeftRect.size;
        rect.anchoredPosition = new Vector2(topLeftRect.center.x - Width * 0.5f, Height * 0.5f - topLeftRect.center.y);

        Image shadow = CreateImage(rect, "Shadow", Shadow, true);
        Stretch(shadow.rectTransform, 0f);
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -7.5f);
        Image glow = CreateImage(rect, "Glow", controller.accentAvailable * new Color(1f, 1f, 1f, 0.16f), true);
        Stretch(glow.rectTransform, -9f);
        Image outline = CreateImage(rect, "Outline", controller.accentAvailable, true);
        Stretch(outline.rectTransform, -2f);
        Image face = CreateImage(rect, "Face", controller.cardAvailable, true);
        Stretch(face.rectTransform, 0f);
        face.raycastTarget = true;   // the click target; the Button sits on the card root

        TMP_Text label = CreateText(rect, "Label", node.displayName, 24f);
        Stretch(label.rectTransform, 0f);
        label.fontStyle = FontStyles.Bold;
        label.margin = new Vector4(15f, 9f, 15f, 9f);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = controller.textBright;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;   // MapNodeView does the hover colour

        MapNodeView view = go.GetComponent<MapNodeView>();
        view.nodeId = node.id;
        view.button = button;
        view.glow = glow;
        view.outline = outline;
        view.face = face;
        view.label = label;
    }

    // --- the first-run layout, the same as the old OnGUI map (scaled from 1280x720 to 1920x1080) ---

    class Layout
    {
        readonly bool horizontal = true;
        readonly float depthMin, depthMax, spreadMin, spreadMax;
        readonly Vector2 size;
        readonly Rect area;

        public Layout(WorldMap map)
        {
            var depths = new List<float>();
            var spreads = new List<float>();
            foreach (MapNode n in map.nodes)
            {
                if (n == null || n.type == NodeType.Store) continue;   // the shop sits in the corner
                depths.Add(n.mapPosition.y);
                spreads.Add(n.mapPosition.x);
            }
            depthMin = Min(depths); depthMax = Max(depths);
            spreadMin = Min(spreads); spreadMax = Max(spreads);

            float spanW = Width - 2f * MarginSide;
            float spanH = Height - MarginTop - MarginBottom;
            float gapX = MinGap(depths) / Mathf.Max(0.0001f, depthMax - depthMin);
            float gapY = MinGap(spreads) / Mathf.Max(0.0001f, spreadMax - spreadMin);
            float w = Mathf.Clamp(gapX * spanW * 0.78f, MinNodeSize.x, MaxNodeSize.x);
            float h = Mathf.Clamp(gapY * spanH * 0.58f, MinNodeSize.y, MaxNodeSize.y);
            size = new Vector2(w, h);
            area = Rect.MinMaxRect(MarginSide + w * 0.5f, MarginTop + h * 0.5f,
                                   Width - MarginSide - w * 0.5f, Height - MarginBottom - h * 0.5f);
        }

        // Card rect in a top-left origin 1920x1080 space
        public Rect RectOf(MapNode node)
        {
            if (node.type == NodeType.Store)
                return new Rect(33f, 87f, size.x, size.y);
            float depth = Mathf.InverseLerp(depthMin, depthMax, node.mapPosition.y);
            float spread = Mathf.Lerp(0.06f, 0.94f, Mathf.InverseLerp(spreadMin, spreadMax, node.mapPosition.x));
            float u = horizontal ? depth : spread;
            float v = horizontal ? spread : depth;
            Vector2 c = new Vector2(Mathf.Lerp(area.xMin, area.xMax, u), Mathf.Lerp(area.yMin, area.yMax, v));
            return new Rect(c - size * 0.5f, size);
        }

        static float Min(List<float> values)
        {
            float m = values.Count > 0 ? values[0] : 0f;
            foreach (float v in values) if (v < m) m = v;
            return m;
        }

        static float Max(List<float> values)
        {
            float m = values.Count > 0 ? values[0] : 1f;
            foreach (float v in values) if (v > m) m = v;
            return m;
        }

        static float MinGap(List<float> values)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            float min = 1f;
            for (int i = 1; i < sorted.Count; i++)
            {
                float d = sorted[i] - sorted[i - 1];
                if (d > 0.0001f && d < min) min = d;
            }
            return min;
        }
    }

    // --- helpers --------------------------------------------------------------------------------

    static Image CreateImage(Transform parent, string name, Color color, bool rounded)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (rounded)
        {
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    static TMP_Text CreateText(Transform parent, string name, string text, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    static Button CreateButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size)
    {
        Image image = CreateImage(parent, name, ButtonColor, true);
        image.raycastTarget = true;
        Place(image.rectTransform, anchor, position, size);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TMP_Text label = CreateText(image.transform, "Label", text, 22f);
        Stretch(label.rectTransform, 0f);
        return button;
    }

    // Fill the parent, grown (negative inset) or shrunk by `inset` px on every side
    static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    // Pinned to one anchor of the parent (pivot on the same corner/edge)
    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
