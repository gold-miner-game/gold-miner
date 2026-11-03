using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// The one persistent object that moves the player between scenes. Scenes never reference each
// other; they all talk to GameFlow. It holds the WorldMap and drives:
//   Menu -> (first time: every tutorial level back to back) -> Map (the hub level, MapHub) -> (portal) -> level scene + Game / RolePick / Store / Survival -> back to Map.
// See docs/level-map-tutorial-design.md.
public class GameFlow : MonoBehaviour
{
    public const string BootScene = "Boot";
    public const string MenuScene = "Menu";
    public const string MapScene = "Map";
    public const string GameScene = "Game";
    public const string RolePickScene = "Select";
    public const string StoreScene = "Store";
    public const string SurvivalScene = "Survival";

    [Tooltip("Seconds the win result stays on screen before returning to the map.")]
    public float returnDelay = 2.5f;

    enum Flow { None, Menu, Map, Hub, Game, RolePick, Store, Survival }

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
    // True while the world-map hub (a walkable level, MapHub) is open
    public bool InHub => flow == Flow.Hub || MapHub.Active != null;   // or its scene was played directly

    Flow flow = Flow.None;
    int tutorialStep = -1;   // index into Map.tutorialLevels while the tutorial run plays; -1 = not in it
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

    // Called by the Boot scene, and the map's Menu button: the main menu (MainMenu)
    public void OpenMenu()
    {
        ClearGameHooks();
        CurrentNode = null;
        flow = Flow.Menu;
        SceneManager.LoadScene(MenuScene);
    }

    // Called by the main menu (or anyone) to show the map.
    public void OpenMap()
    {
        bool backFromNode = CurrentNode != null;   // already on the map, so not the first visit
        ClearGameHooks();
        CurrentNode = null;
        tutorialStep = -1;
        if (!backFromNode && !ProgressStore.TutorialDone && Map != null && Map.tutorialLevels.Count > 0)
        {
            PlayTutorial(0);   // a new player goes through every tutorial before seeing the map
            return;
        }
        if (Map != null && Map.hubLevel != null)
        {
            // The map is a place to walk around in: the hub level in the Game scene, a portal per node
            LevelLoader.ScriptedTutorial = true;   // the pig, and the run record is left alone
            LevelLoader.Requested = Map.hubLevel;
            LevelLoader.WaitToAdvance = true;
            LevelLoader.AdvanceNow = false;
            flow = Flow.Hub;
            SceneManager.LoadScene(LevelLayout.SceneFor(Map.hubLevel));
            return;
        }
        // No hub level: the card map scene
        flow = Flow.Map;
        SceneManager.LoadScene(MapScene);
    }

    // The tutorial run: level i of Map.tutorialLevels, always as Mr. Hyde. Winning moves to the next
    // one (Update); after the last, the run is saved as done and the map opens.
    void PlayTutorial(int i)
    {
        TextAsset level = Map.tutorialLevels[i];
        if (level == null) { NextTutorial(i); return; }   // an empty slot in the list: skip it
        tutorialStep = i;
        CurrentNode = null;
        winHandled = false;
        winAt = 0f;
        LevelLoader.ScriptedTutorial = true;
        LevelLoader.Requested = level;
        LevelLoader.WaitToAdvance = true;   // GameFlow handles the transition, not LevelLoader
        LevelLoader.AdvanceNow = false;
        flow = Flow.Game;
        SceneManager.LoadScene(LevelLayout.SceneFor(level));
    }

    void NextTutorial(int finished)
    {
        if (finished + 1 < Map.tutorialLevels.Count) { PlayTutorial(finished + 1); return; }
        FinishTutorialRun();
    }

    // The tutorials are over: saved as done, then straight on to the store (Map.afterTutorialNode), whose
    // leaving opens the map. The last tutorial level calls this itself on its win ("win_to_store"), so
    // it works even when that level was played straight from its scene
    public void FinishTutorialRun()
    {
        ProgressStore.MarkCompleted(ProgressStore.TutorialDoneId);
        tutorialStep = -1;
        winHandled = true;
        MapNode next = Map != null && !string.IsNullOrEmpty(Map.afterTutorialNode) ? Map.Find(Map.afterTutorialNode) : null;
        if (next != null) { ClearGameHooks(); EnterNode(next); return; }
        CurrentNode = null;
        OpenMap();
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
                SceneManager.LoadScene(LevelLayout.SceneFor(node.levelAsset));   // its level scene, else Game
                break;

            case NodeType.Battle:
                ApplyChosenCharacter();             // keep the picked champion across levels / restarts
                LevelLoader.ScriptedTutorial = false;  // battles play as whoever was picked
                LevelLoader.Requested = node.levelAsset;
                LevelLoader.WaitToAdvance = true;
                LevelLoader.AdvanceNow = false;
                flow = Flow.Game;
                SceneManager.LoadScene(LevelLayout.SceneFor(node.levelAsset));   // its level scene, else Game
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
        if (CurrentNode != null) ProgressStore.MarkCompleted(CurrentNode.id);
        OpenMap();
    }

    // Leave a node without clearing it (e.g. a store the player decides not to buy from).
    public void ReturnToMap()
    {
        OpenMap();
    }

    // Make the saved character choice stick. ChampionSpawner.Chosen is a static that is wiped every
    // time Play starts, while the pick lives in ProgressStore (PlayerPrefs). So before any battle we
    // re-seed Chosen from the save, and (re)create the run record if it is missing or the wrong kind
    // (e.g. after a restart). An existing record of the right type is kept so wallet/skills carry on.
    void ApplyChosenCharacter()
    {
        int index = ChampionSpawner.MrHyde;   // 0 = the pig, the default
        if (ProgressStore.HasPickedCharacter && int.TryParse(ProgressStore.SelectedCharacter, out int picked))
            index = picked;

        ChampionSpawner.Chosen = index;

        System.Type want = ChampionSpawner.RecordType(index);
        if (CrossLevelStatus.Active == null || CrossLevelStatus.Active.GetType() != want)
            CrossLevelStatus.StartFresh(want);
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
        if (flow != Flow.Game || (CurrentNode == null && tutorialStep < 0)) return;

        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        if (gm.IsPlaying)
        {
            // Still playing, or the player pressed R to retry: reset the win timer.
            winHandled = false;
            return;
        }
        if (!gm.PlayerWon) return;   // a loss stays put; R retries the same level
        if (TutorialDialogue.Playing) { winHandled = false; return; }   // a story after the win: wait for it

        if (!winHandled)
        {
            winHandled = true;
            winAt = Time.unscaledTime + returnDelay;
        }
        if (Time.unscaledTime < winAt) return;
        if (tutorialStep >= 0) NextTutorial(tutorialStep);
        else CompleteCurrentNode();
    }
}
