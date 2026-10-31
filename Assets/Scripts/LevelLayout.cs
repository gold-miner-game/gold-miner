using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// The root of a level scene (Assets/Scenes/Levels/<level>.unity, made by Tools > Gold Miner >
// Build Level Scenes). The scene is the level's layout, edited in the Scene view:
//   PlayerStart   where the player's character starts
//   Hooks         one child per miner; each child is a hook origin
//   Items         the treasure, rocks and lives, as prefab instances
// Anything else you drop in the scene (decoration, an extra prop) shows up in this level only.
// The level file still holds the rules (speeds, timings, guide, tutorial stages and their items).
//
// The shared gameplay (camera, HUD, GameManager, LevelLoader, miner) stays in the Game scene,
// which this loads on top when the level starts, so pressing Play in a level scene plays it.
// LevelLoader then reads the layout from here instead of the file's pig_start / hooks / items.
[DefaultExecutionOrder(-2000)]
public class LevelLayout : MonoBehaviour
{
    public const string GameScene = "Game";

    [Tooltip("The level file (Resources/Levels) with this level's rules. Its name is this scene's name.")]
    public TextAsset levelFile;
    public Transform playerStart;
    public Transform hooks;
    public Transform items;

    // The level in play, set while its scene is loaded
    public static LevelLayout Active { get; private set; }

    void Awake()
    {
        Active = this;
        // Already open next to this one (the editor opens them together): LevelLoader may have
        // built the level before this Awake, and it leaves this scene's items alone either way
        if (SceneManager.GetSceneByName(GameScene).isLoaded) return;

        // Hide the items until LevelLoader has cleared the Game scene's placeholder treasure,
        // so they are not counted or cleared with it (LevelLoader shows them again)
        if (items != null) items.gameObject.SetActive(false);
    }

    // Not in Awake: pressing Play with Game open next to this scene loads the two one after the
    // other, so in Awake Game can still be on its way, and loading it here made a second copy
    // (two LevelLoaders, so two miners on every hook). By Start every scene that opens with Play is in
    void Start()
    {
        if (!SceneManager.GetSceneByName(GameScene).IsValid())
            SceneManager.LoadScene(GameScene, LoadSceneMode.Additive);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    public Vector2 PlayerStart => playerStart != null ? (Vector2)playerStart.position : new Vector2(0f, -1f);

    public List<Vector2> HookOrigins()
    {
        var list = new List<Vector2>();
        if (hooks == null) return list;
        foreach (Transform h in hooks)
            if (h.gameObject.activeSelf) list.Add(h.position);
        return list;
    }

    // The items as level entries (type = object name without Unity's " (1)" suffix), for counting
    public List<LevelData.Item> Items()
    {
        var list = new List<LevelData.Item>();
        if (items == null) return list;
        foreach (Transform t in items)
        {
            if (!t.gameObject.activeSelf) continue;
            string type = t.name;
            int space = type.IndexOf(" (");
            if (space > 0) type = type.Substring(0, space);
            list.Add(new LevelData.Item { type = type, position = t.position });
        }
        return list;
    }

    public void ShowItems()
    {
        if (items != null) items.gameObject.SetActive(true);
    }

    // The scene a level is played in: its own level scene when there is one in the build, else Game
    public static string SceneFor(TextAsset level)
    {
        if (level != null && Application.CanStreamedLevelBeLoaded(level.name)) return level.name;
        return GameScene;
    }

    // The arena the levels are balanced for (GameplayCamera.arenaSize), around the camera at the origin
    static readonly Vector2 ArenaSize = new Vector2(17.78f, 10f);

    void OnDrawGizmos()
    {
        DrawScreenLayout();
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawWireCube(Vector3.zero, ArenaSize);

        if (playerStart != null)
        {
            Gizmos.color = new Color(1f, 0.72f, 0.82f);
            Gizmos.DrawWireSphere(playerStart.position, 0.5f);
        }
        if (hooks != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.4f);
            foreach (Transform h in hooks)
            {
                Gizmos.DrawWireSphere(h.position, 0.3f);
                Gizmos.DrawLine(h.position, h.position + Vector3.down * 0.8f);
            }
        }
    }

    // The 16:9 screen around the arena in world units: the camera's view and the HUD's top bar and
    // sidebar, the way GameplayCamera fits them. The HUD canvas itself draws at pixel scale far
    // away in the Scene view (the Game view shows it in place), so this is where its panels fall
    void DrawScreenLayout()
    {
        float frameW = ScreenFrame.Aspect * (1f - HUDLayout.SidebarWidth);
        float frameH = 1f - HUDLayout.TopBarHeight;
        float aspect = frameW / frameH;   // the camera's share of a 16:9 screen
        float halfH = Mathf.Max(ArenaSize.y * 0.5f, ArenaSize.x * 0.5f / aspect);
        Rect view = new Rect(-halfH * aspect, -halfH, 2f * halfH * aspect, 2f * halfH);
        float screenW = view.width / (1f - HUDLayout.SidebarWidth);
        float screenH = view.height / (1f - HUDLayout.TopBarHeight);
        Rect topBar = Rect.MinMaxRect(view.xMin, view.yMax, view.xMin + screenW, view.yMin + screenH);
        Rect sidebar = Rect.MinMaxRect(view.xMax, view.yMin, view.xMin + screenW, view.yMax);

        Color panel = new Color(0.118f, 0.165f, 0.247f, 0.85f);   // the HUD's panel colour
        Gizmos.color = panel;
        Gizmos.DrawCube(topBar.center, topBar.size);
        Gizmos.DrawCube(sidebar.center, sidebar.size);
        Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
        Gizmos.DrawWireCube(topBar.center, topBar.size);
        Gizmos.DrawWireCube(sidebar.center, sidebar.size);
        Gizmos.DrawWireCube(view.center, view.size);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(new Vector3(topBar.xMin + 0.2f, topBar.yMax - 0.1f), "HUD top bar (level, money, timer)");
        UnityEditor.Handles.Label(new Vector3(sidebar.xMin + 0.2f, sidebar.yMax - 0.1f), "HUD sidebar\n(life, wallet,\nskill, items)");
#endif
    }
}
