using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Bake World Map Into Scene: turns the world map's grid (Resources/Hub/
// world_map.json) into real objects in its scene (Scenes/Levels/world_map.unity), under Level > Hub:
//   Walls     a Rock per wall tile (Is Wall on, tinted for the attack / survival / compete areas)
//   Portals   one per map node (HubPortal: Node Id, Hidden, Stone Color)
//   OneWays   the one-way tiles (OneWayDoor: Allowed Dir; an arrow in the Scene view)
//   Labels    the area names on the floor
// and sets Level > Hooks (outside Hub) to one hook origin per "miners" spot in the file, and puts a
// one-time small gold (MapGold) under Level > Items per "gold" spot.
// From then on the scene is the map: move, add or delete walls and portals in the Scene view, and
// put story (Tools > Gold Miner > Tutorial Step / Tutorial Story) anywhere in it. The map fires
// TutorialEvents for story (map_open, map_cleared_<node>, ..., see TutorialEvents.MapEvents).
// Baking again replaces Hub (asks first); everything outside Hub, like the story, is kept.
// Opening the world map scene bakes it by itself when it has no Hub yet, and frames the map in the
// Scene view (also Tools > Gold Miner > Frame World Map In Scene View).
[InitializeOnLoad]
public static class HubSceneBaker
{
    static HubSceneBaker()
    {
        EditorSceneManager.sceneOpened += (scene, mode) =>
        {
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.delayCall += () =>
            {
                if (!scene.isLoaded) return;
                LevelLayout layout = FindHubLayout();
                if (layout == null) return;
                if (layout.transform.Find(MapHub.BakedRootName) == null) Bake(false);
                FrameMap();
            };
        };
    }

    // Points the Scene view at the baked map: 2D, the whole maze in view
    [MenuItem("Tools/Gold Miner/Frame World Map In Scene View")]
    public static void FrameMap()
    {
        LevelLayout layout = FindHubLayout();
        Transform hub = layout != null ? layout.transform.Find(MapHub.BakedRootName) : null;
        if (hub == null) return;
        Renderer[] all = hub.GetComponentsInChildren<Renderer>();
        if (all.Length == 0) return;
        Bounds b = all[0].bounds;
        foreach (Renderer r in all) b.Encapsulate(r.bounds);
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null) return;
        view.in2DMode = true;
        view.Frame(b, false);
        view.Repaint();
    }

    [MenuItem("Tools/Gold Miner/Frame World Map In Scene View", true)]
    static bool CanFrame() => FindHubLayout() != null;
    const string ScenePath = "Assets/Scenes/Levels/world_map.unity";
    const string PrefabDir = "Assets/Prefabs";
    static readonly Color PortalColor = new Color(0.62f, 0.28f, 0.85f, 1f);

    [MenuItem("Tools/Gold Miner/Bake World Map Into Scene")]
    public static void Bake() => Bake(true);

    // ask: confirm before replacing an existing Hub (a bake on scene open has none to replace)
    static void Bake(bool ask)
    {
        LevelLayout layout = FindHubLayout();
        if (layout == null)
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Bake World Map", "No " + ScenePath + ".", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            layout = FindHubLayout();
        }
        if (layout == null || layout.levelFile == null)
        {
            EditorUtility.DisplayDialog("Bake World Map", "The world map scene has no Level (LevelLayout) with a grid level file.", "OK");
            return;
        }

        Transform old = layout.transform.Find(MapHub.BakedRootName);
        if (old != null && ask && !EditorUtility.DisplayDialog("Bake World Map",
                "Replace the baked map (Level > Hub) with the grid in " + layout.levelFile.name + ".json?\n\n" +
                "Walls, portals and labels you changed in the scene are lost, Level > Hooks is set to the file's miners and its one-time gold is placed again. Story and anything else outside Hub is kept.",
                "Replace", "Cancel"))
            return;

        LevelData level = LevelData.Parse(layout.levelFile.text);
        GameObject rockPrefab = FindPrefab("Rock");
        if (rockPrefab == null) { EditorUtility.DisplayDialog("Bake World Map", "No Rock prefab under " + PrefabDir + ".", "OK"); return; }
        SpriteRenderer rockSprite = rockPrefab.GetComponent<SpriteRenderer>();
        Color grey = rockSprite != null ? rockSprite.color : new Color(0.5f, 0.5f, 0.5f);
        WorldMap map = Resources.Load<WorldMap>("WorldMap");
        float tile = level.TileSize;
        Scene scene = layout.gameObject.scene;

        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var hub = new GameObject(MapHub.BakedRootName).transform;
        hub.SetParent(layout.transform, false);
        Undo.RegisterCreatedObjectUndo(hub.gameObject, "Bake World Map");
        Transform walls = Group(hub, "Walls"), portals = Group(hub, "Portals"), oneWays = Group(hub, "OneWays"), labels = Group(hub, "Labels");

        void Wall(Vector2 at, Color? tint, bool hookPasses = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, scene);
            go.name = "Wall";
            go.transform.SetParent(walls, false);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * tile;
            Rock rock = go.GetComponent<Rock>();
            if (rock != null) { rock.isWall = true; rock.hookPasses = hookPasses; }
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && tint.HasValue) sr.color = tint.Value;
        }
        foreach (Vector2 at in level.walls) Wall(at, null);
        // green rocks (levels not disclosed yet) are no obstacle to a hook
        foreach (LevelData.ColoredWall w in level.coloredWalls) Wall(w.position, MapHub.WallColor(w.color, grey), w.color == 'G');

        foreach (LevelData.Portal p in level.portals)
        {
            MapNode node = map != null ? map.Find(p.target) : null;
            var go = new GameObject("Portal_" + p.target);
            go.transform.SetParent(portals, false);
            go.transform.position = p.position;
            go.transform.localScale = Vector3.one * tile * 0.95f;
            HubPortal portal = go.AddComponent<HubPortal>();
            portal.nodeId = p.target;
            portal.hidden = p.hidden;
            portal.stoneColor = MapHub.WallColor(p.stone, grey);
            SpriteRenderer hex = HubPortal.Visuals(go, rockSprite != null ? rockSprite.sprite : null,
                                                   node != null ? node.displayName : p.target, out _);
            hex.color = p.hidden ? portal.stoneColor : PortalColor;   // how it starts out
        }

        foreach (LevelData.OneWay one in level.oneWays)
        {
            var go = new GameObject("OneWay");
            go.transform.SetParent(oneWays, false);
            go.transform.position = one.position;
            go.transform.localScale = Vector3.one * tile;
            go.AddComponent<OneWayDoor>().allowedDir = one.dir;
        }

        foreach (LevelData.Label l in level.labels) MapHub.AddLabel(labels, l.text, l.position, tile);

        if (layout.playerStart != null) layout.playerStart.position = level.pigStart;

        // The map file's "miners" (the evil cat's eyes): one hook origin each under Level > Hooks,
        // which LevelLoader reads for the scene's miners
        if (layout.hooks != null)
        {
            for (int i = layout.hooks.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(layout.hooks.GetChild(i).gameObject);
            for (int i = 0; i < level.hooks.Count; i++)
            {
                var origin = new GameObject("Miner " + (i + 1));
                Undo.RegisterCreatedObjectUndo(origin, "Bake World Map");
                origin.transform.SetParent(layout.hooks, false);
                origin.transform.position = level.hooks[i];
            }
        }

        // The map file's "gold": one-time small gold (MapGold) under Level > Items. Other items stay
        GameObject goldPrefab = FindPrefab("Gold_Small");
        if (layout.items != null && goldPrefab != null)
        {
            foreach (MapGold g in layout.items.GetComponentsInChildren<MapGold>(true)) Undo.DestroyObjectImmediate(g.gameObject);
            for (int i = 0; i < level.mapGold.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(goldPrefab, scene);
                Undo.RegisterCreatedObjectUndo(go, "Bake World Map");
                go.name = "Gold_Small";   // LevelLayout.Items reads the type from the name
                go.transform.SetParent(layout.items, false);
                go.transform.position = level.mapGold[i];
                go.transform.localScale = Vector3.one * tile * 0.85f;   // like the maze's gold: fits a tile
                go.AddComponent<MapGold>().goldId = "map_gold_" + (i + 1);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (ask) Selection.activeGameObject = hub.gameObject;
        Debug.Log("HubSceneBaker: baked " + (level.walls.Count + level.coloredWalls.Count) + " walls, " + level.portals.Count +
                  " portals, " + level.oneWays.Count + " one-way tiles, " + level.labels.Count + " labels and " +
                  level.hooks.Count + " miners and " + level.mapGold.Count + " gold into " + scene.path);
    }

    [MenuItem("Tools/Gold Miner/Bake World Map Into Scene", true)]
    static bool CanBake() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // The open scene's LevelLayout whose level file is a grid (the world map)
    static LevelLayout FindHubLayout()
    {
        foreach (LevelLayout l in Object.FindObjectsByType<LevelLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.levelFile == null) continue;
            try { if (LevelData.Parse(l.levelFile.text).IsMaze) return l; }
            catch (System.Exception) { }
        }
        return null;
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }
}
