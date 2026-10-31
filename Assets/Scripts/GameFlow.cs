using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// The one persistent object that moves the player between scenes. Scenes never reference each
// other; they all talk to GameFlow. It holds the WorldMap and drives:
//   Map -> (node) -> Game / RolePick / Store -> back to Map.
// See docs/level-map-tutorial-design.md.
public class GameFlow : MonoBehaviour
{
    public const string BootScene = "Boot";
    public const string MapScene = "Map";
    public const string GameScene = "Game";
    public const string RolePickScene = "Select";
    public const string StoreScene = "Store";
    public const string SurvivalScene = "Survival";

    [Tooltip("Seconds the win result stays on screen before returning to the map.")]
    public float returnDelay = 2.5f;

    enum Flow { None, Map, Game, RolePick, Store, Survival }

    static GameFlow instance;
    public static GameFlow Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GameFlow");
                instance = go.AddComponent<GameFlow>();
            }
            return instance;
        }
    }

    public WorldMap Map { get; private set; }
    public MapNode CurrentNode { get; private set; }

    Flow flow = Flow.None;
    bool winHandled;
    float winAt;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

        if (Map == null) Map = Resources.Load<WorldMap>("WorldMap");
        if (Map == null) Debug.LogWarning("GameFlow: no WorldMap in Resources. Run Tools > Gold Miner > Build Flow Scenes.");
    }

    // Called by the Boot scene (or anyone) to show the map.
    // The maze (tut_maze) IS the world map hub — always return there, never the LEVELS tree.
    public void OpenMap()
    {
        ClearGameHooks();
        CurrentNode = null;

        MapNode maze = Map != null ? Map.Find("tut_maze") : null;
        if (maze != null)
        {
            EnterNode(maze);
            return;
        }

        flow = Flow.Map;
        SceneManager.LoadScene(MapScene);
    }

    // Maze level portal → battle Game scene. Snapshot the hub first so return restores it.
    public void EnterBattleFromMaze(string nodeId)
    {
        if (Map == null || string.IsNullOrEmpty(nodeId)) return;
        MapNode node = Map.Find(nodeId);
        if (node == null || node.type != NodeType.Battle)
        {
            Debug.LogWarning("GameFlow: no battle node '" + nodeId + "'");
            return;
        }
        MazeHubState.CaptureFromScene();
        EnterNode(node);
    }

    // Called by the map when the player clicks an unlocked node.
    public void EnterNode(MapNode node)
    {
        if (node == null) return;
        CurrentNode = node;
        winHandled = false;
        winAt = 0f;

        switch (node.type)
        {
            case NodeType.Tutorial:
                LevelLoader.ScriptedTutorial = true;   // tutorials are always Mr. Hyde
                LevelLoader.Requested = node.levelAsset;
                LevelLoader.WaitToAdvance = true;   // GameFlow handles the transition, not LevelLoader
                LevelLoader.AdvanceNow = false;
                flow = Flow.Game;
                SceneManager.LoadScene(GameScene);
                break;

            case NodeType.Battle:
                // For now every level is the pig — no other champions.
                ForcePig();
                LevelLoader.ScriptedTutorial = true;
                LevelLoader.Requested = node.levelAsset;
                LevelLoader.WaitToAdvance = true;
                LevelLoader.AdvanceNow = false;
                flow = Flow.Game;
                SceneManager.LoadScene(GameScene);
                break;

            case NodeType.RolePick:
                flow = Flow.RolePick;
                SceneManager.LoadScene(RolePickScene);   // the Select scene ("WHO ARE U")
                break;

            case NodeType.Store:
                ApplyChosenCharacter();             // the store reads the run record (wallet/skills)
                flow = Flow.Store;
                SceneManager.LoadScene(StoreScene);
                break;

            case NodeType.Survival:
                ApplyChosenCharacter();             // the real mode plays as whoever was picked
                flow = Flow.Survival;
                SceneManager.LoadScene(SurvivalScene);
                break;
        }
    }

    // Mark the current node cleared and go back to the map. Store / RolePick call this on "leave";
    // Game levels reach it by themselves once the win result has shown.
    public void CompleteCurrentNode()
    {
        MapNode node = CurrentNode;
        if (node != null)
        {
            // Battles: bank each level's best (player−miner) margin. World-map MONEY =
            // maze bank (pre-battle eats) + sum of those best margins — never the battle's raw $.
            if (node.type == NodeType.Battle)
            {
                GameManager gm = GameManager.Instance;
                if (gm != null && gm.PlayerWon)
                {
                    int margin = gm.PlayerMoney - gm.MinerMoney;
                    // One key per map node — recording asset name too would double MapMoney.
                    ProgressStore.RecordBestMargin(node.id, margin);
                    Debug.Log("GameFlow: best margin for " + node.id + " = " + margin
                              + " (margins sum $" + ProgressStore.MapMoney
                              + ", hub display $" + MazeHubState.DisplayMoney + ")");
                }
            }
            // Maze hub stays replayable; do not treat it as a cleared tree node that opens LEVELS.
            if (node.id != "tut_maze")
                ProgressStore.MarkCompleted(node.id);
        }
        OpenMap();
    }

    // Leave a node without clearing it (e.g. a store the player decides not to buy from).
    public void ReturnToMap()
    {
        OpenMap();
    }

    // Temporary: all map battles use Mr. Hyde only (no role pick).
    void ForcePig()
    {
        ChampionSpawner.Chosen = ChampionSpawner.MrHyde;
        ProgressStore.SelectedCharacter = ChampionSpawner.MrHyde.ToString();
        System.Type want = ChampionSpawner.RecordType(ChampionSpawner.MrHyde);
        if (CrossLevelStatus.Active == null || CrossLevelStatus.Active.GetType() != want)
            CrossLevelStatus.StartFresh(want);
    }

    // Make the saved character choice stick. ChampionSpawner.Chosen is a static that is wiped every
    // time Play starts, while the pick lives in ProgressStore (PlayerPrefs). So before any battle we
    // re-seed Chosen from the save, and (re)create the run record if it is missing or the wrong kind
    // (e.g. after a restart). An existing record of the right type is kept so wallet/skills carry on.
    void ApplyChosenCharacter()
    {
        // Until role pick returns to the map, always the pig.
        ForcePig();
    }

    void ClearGameHooks()
    {
        LevelLoader.Requested = null;
        LevelLoader.ScriptedTutorial = null;
        LevelLoader.WaitToAdvance = false;
        LevelLoader.AdvanceNow = false;
    }

    void Update()
    {
        if (flow != Flow.Game || CurrentNode == null) return;

        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        if (gm.IsPlaying)
        {
            // Still playing, or the player pressed R to retry: reset the win timer.
            winHandled = false;
            return;
        }
        if (!gm.PlayerWon) return;   // a loss stays put; R retries the same level

        if (!winHandled)
        {
            winHandled = true;
            winAt = Time.unscaledTime + returnDelay;
        }
        if (Time.unscaledTime >= winAt) CompleteCurrentNode();
    }
}
