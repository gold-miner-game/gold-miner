using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// Builds the arena from a level file made by tools/sim/gold_miner_heuristic.py (levels command).
// Put it in the Game scene: before anything else starts it removes the scene's treasure, spawns the
// level's items from the item prefabs, puts in the player's character (ChampionSpawner) and moves it,
// makes one miner + hook + telegraph per hook origin (copies of the scene's own, so they keep their
// look), and applies the level's speeds and timings. N / P load the next / previous level; winning a
// round moves on to the next one by itself.
// The tutorial (every level up to the last one with a guide or stages) is always played as Mr. Hyde.
// Leaving it opens the select scene, where the player picks the character for the rest of the levels.
[DefaultExecutionOrder(-1000)]
public class LevelLoader : MonoBehaviour
{
    [Header("Levels")]
    [Tooltip("Level files in order. Empty: every TextAsset in Resources/Levels, sorted by name.")]
    public TextAsset[] levels;
    [Tooltip("1-based level to start with on the first load")]
    public int startLevel = 1;

    [Header("Prefabs (matched to the level's item types by name)")]
    public GameObject[] itemPrefabs;   // Gold_Small, Gold_Medium, Gold_Large, Diamond, Rock, Life

    [Header("Scene")]
    public MinerAI minerTemplate;          // empty: the first MinerAI in the scene
    [Tooltip("Spawned once per hook when the scene has no miner to copy. Empty = Resources/Prefabs/Miner")]
    public GameObject minerPrefab;
    [FormerlySerializedAs("pig")]
    public PlayerCharacter player;         // empty: the first character in the scene
    public bool clearSceneItems = true;    // remove treasure, rocks and lives already placed in the scene
    [Tooltip("Solid walls on the screen edges, so the player can't walk out of view (ArenaWalls)")]
    public bool screenWalls = true;

    [Header("Rules")]
    [Tooltip("Use the miner policy the levels were balanced against (MinerAI.Strategy.Efficient)")]
    public bool balancedMinerPolicy = true;
    [Tooltip("Scales the level's pig and hook speeds (1 = as balanced, 0.8 = everything 20% slower)")]
    [Range(0.1f, 2f)]
    public float gameSpeed = 0.8f;
    [Tooltip("Chance each hook shot aims at the pig (red) instead of treasure; a pig shot is always " +
             "followed by a treasure shot. A level's pig_shot_chance overrides it. 0 = the level's " +
             "min/max_treasure_shots rhythm")]
    [Range(0f, 1f)]
    public float pigShotChance = 0.3f;
    [Tooltip("Seed for the hooks' random choices, so every play of a level goes the same way " +
             "(0 = different every time). A level's random_seed overrides it")]
    public int randomSeed = 0;
    [Tooltip("Lives the pig starts every level with (0 = the level file's starting_lives)")]
    public int livesPerLevel = 3;
    [Tooltip("Guide and retry text. The level's dev label is logged to the Console, not drawn")]
    public bool showLevelLabel = true;

    [Header("Tutorial")]
    [Tooltip("Seconds a new guide shows in full before it shrinks to its first line and the progress")]
    public float guideFullTime = 8f;
    [Tooltip("The guide box is at most this share of the arena's width (it wraps into more lines)")]
    [Range(0.25f, 0.9f)] public float guideMaxWidth = 0.5f;
    [Tooltip("The guide's opacity while the player is under it")]
    [Range(0f, 1f)] public float guideCoveredAlpha = 0.25f;
    [Tooltip("Steal stage: seconds the hook rests on its treasure before pulling it back")]
    public float stealHold = 1.5f;
    [Tooltip("Steal stage: seconds between one pull ending and the next starting")]
    public float stealPause = 1f;

    [Header("Character select")]
    [Tooltip("Scene opened when the tutorial is over (must be in Build Profiles > Scene List). " +
             "Empty: no select screen, keep Mr. Hyde")]
    public string selectScene = "Select";

    [Header("Progression")]
    [Tooltip("After a win, load the next level by itself")]
    public bool autoAdvance = true;
    [Tooltip("Seconds the result stays on screen before the next level loads")]
    public float advanceDelay = 3f;

    // The level index survives GameManager.Restart (which reloads the scene)
    private static int current = -1;

    const string MinerPrefabPath = "Prefabs/Miner";

    private TextAsset[] list;
    private LevelData level;
    private LevelLayout layout;      // the level scene's layout; null = everything from the level file
    private string label = "";
    private readonly List<GameObject> spawned = new List<GameObject>();
    private float advanceAt = -1f;   // time the next level loads; -1 = not scheduled
    private readonly List<MinerAI> miners = new List<MinerAI>();
    private int stage = -1;          // index in level.stages; -1 = the level has none
    private int stageStart;          // the goal counter when the stage began
    private float stageTime;         // survive stage: Time.time when the stage began
    private float nextPullAt;        // steal stage: when the hooks get their next treasure
    private GUIStyle guideStyle;
    private string shownGuide;       // the guide on screen, to notice a new level or stage
    private float guideSince;        // Time.time when it appeared
    private Rect guideRect;          // where the guide was last drawn, in ScreenFrame's GUI space
    private float guideAlpha = 1f;   // fades down while the player is under the guide
    private bool hasHud;             // the HUD's end panel shows what comes next; else OnGUI does
    private int lastTutorial = -1;   // index of the last tutorial level; -1 = no tutorial
    private float levelSpeed;        // the level's pig_speed, before game speed and the character's speedScale

    public static int CurrentLevel => current + 1;
    public int LevelCount => list != null ? list.Length : 0;   // 1 when a map node requested the level
    public bool IsMazeLevel => level != null && level.IsMaze;

    // The tutorial: every level up to the last one with a guide or stages (level_01b_block), so the
    // normal level between the two tutorials counts too. It is always played as Mr. Hyde, and the
    // cross-level record (speed, digest, shield, wallet) is left untouched; the record starts when a
    // champion is picked on the select screen
    public static bool Tutorial { get; private set; }

    // Wallet: map Battle nodes (and editor level_N files) can pay. The scripted tutorial run
    // never touches the wallet, even when it reuses a battle layout like Level 1.
    public static bool CountsMoney(string levelName)
    {
        if (GameFlow.Instance != null && GameFlow.Instance.InTutorialRun) return false;
        if (ScriptedTutorial == true || Tutorial) return false;

        if (GameFlow.Instance != null && GameFlow.Instance.CurrentNode != null
            && GameFlow.Instance.CurrentNode.type == NodeType.Battle)
            return true;

        if (string.IsNullOrEmpty(levelName) || !levelName.StartsWith("level_")) return false;
        string tail = levelName.Substring("level_".Length);
        if (tail.Length == 0) return false;
        for (int i = 0; i < tail.Length; i++)
            if (tail[i] < '0' || tail[i] > '9') return false;
        return int.Parse(tail) >= 1;
    }

    // Set by GameFlow before loading the Game scene: build exactly this level instead of walking the
    // index list. Left null in the editor so N / P dev testing keeps working. With a single-level
    // list, the tutorial check below reads this one level's guide/stages, so it is correct per level.
    public static TextAsset Requested;

    // Set by GameFlow for map nodes. True = this visit is a tutorial (always Mr. Hyde). False = a
    // battle, so the character saved at role pick is who plays, even if the level file mentions a
    // skill. Null = no map (editor N/P), fall back to the guide/stages heuristic.
    public static bool? ScriptedTutorial;

    // Play without a domain reload keeps statics: a level requested in the last play would replace
    // the open scene's own level (the world map scene ran the Eat tutorial)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Requested = null;
        ScriptedTutorial = null;
        WaitToAdvance = false;
        AdvanceNow = false;
    }

    void Awake()
    {
        // Played from a level scene (LevelLayout): that scene says which level this is
        // (found directly too: opened together in the editor, its Awake may not have run yet)
        layout = LevelLayout.Active != null ? LevelLayout.Active : FindFirstObjectByType<LevelLayout>();
        TextAsset sceneLevel = layout != null ? layout.levelFile : null;

        if (Requested != null)
        {
            list = new[] { Requested };
            current = 0;
        }
        else
        {
            list = levels != null && levels.Length > 0 ? levels : LoadFromResources();
            int inList = sceneLevel != null ? System.Array.IndexOf(list, sceneLevel) : -1;
            if (inList >= 0) current = inList;
            else if (sceneLevel != null) { list = new[] { sceneLevel }; current = 0; }
            if (current < 0 || current >= list.Length) current = Mathf.Clamp(startLevel - 1, 0, list.Length - 1);
        }
        if (list.Length == 0)
        {
            Debug.LogWarning("LevelLoader: no level files (assign them, or put them in Resources/Levels)");
            return;
        }

        lastTutorial = FindLastTutorial();
        // The map says whether this visit is a tutorial. The guide/stages scan is only for editor
        // play (N/P through the level list), where no map node made the call.
        Tutorial = ScriptedTutorial ?? current <= lastTutorial;
        level = LevelData.Parse(list[current].text);
        if (level.IsMaze) Tutorial = true;   // the hub, even played straight from its scene: the pig, no run record
        if (layout != null) UseLayout();
        int seed = Mathf.RoundToInt(level.Param("random_seed", randomSeed));
        if (seed != 0) Random.InitState(seed);
        Build();
    }

    // The level scene's PlayerStart, Hooks and Items replace the file's pig_start, hooks and items.
    // The items are already in the scene, so they are shown instead of spawned
    void UseLayout()
    {
        level.pigStart = layout.PlayerStart;
        level.hooks.Clear();
        level.hooks.AddRange(layout.HookOrigins());
        level.items.Clear();
        level.items.AddRange(layout.Items());
    }

    // The character chosen on the role-pick screen, else whatever ChampionSpawner still remembers.
    static int SavedChampion()
    {
        if (ProgressStore.HasPickedCharacter
            && int.TryParse(ProgressStore.SelectedCharacter, out int picked) && picked >= 0)
            return picked;
        return ChampionSpawner.Chosen >= 0 ? ChampionSpawner.Chosen : ChampionSpawner.MrHyde;
    }

    // The tutorial is everything up to the last level with a guide or stages (level_01b_block)
    int FindLastTutorial()
    {
        int last = -1;
        for (int i = 0; i < list.Length; i++)
        {
            LevelData data = LevelData.Parse(list[i].text);
            // Attack and survival levels have a guide too, but they are battles
            bool battle = data.Param("attack", 0f) > 0.5f || data.Param("survival", 0f) > 0.5f || data.Param("compete", 0f) > 0.5f;
            if (!battle && (!string.IsNullOrEmpty(data.guide) || data.stages.Count > 0)) last = i;
        }
        return last;
    }

    // In the world map's order (eat, dodge, block, then the battles), so playing without the map
    // goes through the levels the way the map does. By name alone level_01 came before
    // level_01a_dodge. Levels not on the map follow, by name.
    static TextAsset[] LoadFromResources()
    {
        TextAsset[] found = Resources.LoadAll<TextAsset>("Levels");
        WorldMap map = Resources.Load<WorldMap>("WorldMap");
        var order = new List<TextAsset>();
        if (map != null)
            foreach (MapNode node in map.nodes)
                if (node != null && node.levelAsset != null) order.Add(node.levelAsset);

        System.Array.Sort(found, (a, b) =>
        {
            int ia = order.IndexOf(a), ib = order.IndexOf(b);
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.name, b.name);
        });
        return found;
    }

    void Build()
    {
        // Swap in the picked character before anything looks for it (Mr. Hyde in the tutorial).
        // Battles re-read the save here, not ChampionSpawner.Chosen: that static is wiped on every
        // Play (domain reload off) and a missed restore used to leave the scene's pig in place.
        ChampionSpawner spawner = FindFirstObjectByType<ChampionSpawner>();
        if (spawner != null)
        {
            // The world map walks as the character picked on the role-pick screen (Mr. Hyde until
            // then); it still counts as a tutorial, so no run record is touched there
            // Attack mode always plays as Mr. Hyde (his block is the attack), whoever was picked; the
            // picked character's record stays, so the wallet and the store's upgrades carry on
            int picked = SavedChampion();
            bool pigOnly = level.Param("pig_only", 0f) > 0.5f;
            int champion = level.IsMaze ? (ProgressStore.HasPickedCharacter ? picked : ChampionSpawner.MrHyde)
                         : Tutorial || Attack || pigOnly ? ChampionSpawner.MrHyde : picked;
            ChampionSpawner.Chosen = champion;
            if (!Tutorial)
            {
                // Same champion's record, so wallet and skills survive the scene change. A missing
                // or wrong record (after a domain-reload-off restart) is rebuilt from the save.
                System.Type want = ChampionSpawner.RecordType(picked);
                if (CrossLevelStatus.Active == null || CrossLevelStatus.Active.GetType() != want)
                    CrossLevelStatus.StartFresh(want);
            }
            PlayerCharacter spawned = spawner.Spawn(champion);
            if (spawned != null) player = spawned;
        }
        if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
        if (minerTemplate == null) minerTemplate = FindFirstObjectByType<MinerAI>();

        if (!level.IsMaze) HUDLayout.HideTopBar = false;
        if (clearSceneItems) ClearItems();
        if (screenWalls)
        {
            ArenaWalls walls = new GameObject("ArenaWalls").AddComponent<ArenaWalls>();
            if (BigMap) walls.SetBounds(ArenaRect);   // the sector's edges, not the screen's
        }
        if (layout != null) layout.ShowItems();
        else SpawnItems();
        SetUpPlayer();
        SetUpMiners();
        foreach (MinerAI miner in miners) AddHealth(miner.gameObject, miner.hook != null ? miner.hook.gameObject : null, miner.telegraph != null ? miner.telegraph.gameObject : null);
        SetUpRound();
        if (level.stages.Count > 0) StartStage(0);
        if (level.IsMaze) SetUpHub();
        if (Attack) SetUpAttack();
        if (Survival) SetUpSurvival();
        if (Compete) SetUpCompete();
        if (level.Param("wall_rocks", 0f) > 0.5f) SealWallRocks();

        label = $"Level {current + 1}/{list.Length}: {level.items.Count} gold, {level.hooks.Count} hook(s)";
        if (player != null && player.SkillHint != "") label += "  " + player.SkillHint;
        if (level.info.TryGetValue("target_win", out object target) && target is double t)
            label += $"  (balanced for Mr. Hyde win {t:P0})";
        Debug.Log("LevelLoader: " + label + " from " + list[current].name);
    }

    void ClearItems()
    {
        var doomed = new List<GameObject>();
        foreach (Treasure t in FindObjectsByType<Treasure>(FindObjectsSortMode.None)) doomed.Add(t.gameObject);
        foreach (Rock r in FindObjectsByType<Rock>(FindObjectsSortMode.None)) doomed.Add(r.gameObject);
        foreach (LifePickup l in FindObjectsByType<LifePickup>(FindObjectsSortMode.None)) doomed.Add(l.gameObject);
        foreach (GameObject go in doomed)
        {
            if (layout != null && go.scene == layout.gameObject.scene) continue;   // the level scene's own items
            go.SetActive(false);   // leaves Treasure.Active now, not at the end of the frame
            Destroy(go);
        }
    }

    void SpawnItems() { Spawn(level.items); }

    // The world-map hub (a "grid" level): wall rocks, a view that fits the whole maze, a player
    // small enough for its corridors, then MapHub adds the one-way tiles and the portals
    void SetUpHub()
    {
        float tile = level.TileSize;
        // Baked into the world map scene (Tools > Gold Miner > Bake World Map Into Scene): its walls,
        // portals and doors are already there, edited in the Scene view
        Transform baked = layout != null ? layout.transform.Find(MapHub.BakedRootName) : null;
        GameObject rockPrefab = System.Array.Find(itemPrefabs, p => p != null && p.name == "Rock");
        Sprite hex = null;
        Color grey = new Color(0.5f, 0.5f, 0.5f);
        SpriteRenderer rockSprite = rockPrefab != null ? rockPrefab.GetComponent<SpriteRenderer>() : null;
        if (rockSprite != null) { hex = rockSprite.sprite; grey = rockSprite.color; }

        if (baked == null && rockPrefab == null) Debug.LogWarning("LevelLoader: no Rock in Item Prefabs for the hub walls");
        else if (baked == null)
        {
            var walls = new GameObject("Walls").transform;
            void Wall(Vector2 at, Color? tint, bool hookPasses = false)
            {
                GameObject go = Instantiate(rockPrefab, at, Quaternion.identity, walls);
                go.transform.localScale = Vector3.one * tile;
                Rock rock = go.GetComponent<Rock>();
                if (rock != null) { rock.MakeWall(); rock.hookPasses = hookPasses; }
                SpriteRenderer r = go.GetComponent<SpriteRenderer>();
                if (r != null && tint.HasValue) r.color = tint.Value;
            }
            foreach (Vector2 at in level.walls) Wall(at, null);
            foreach (LevelData.ColoredWall w in level.coloredWalls)   // the mode areas; green ones don't stop a hook
                Wall(w.position, MapHub.WallColor(w.color, grey), w.color == 'G');
        }

        GameplayCamera.SetArenaSize(level.MazeSize + Vector2.one * tile);
        if (player != null) player.transform.localScale = Vector3.one * tile * level.Param("player_scale", 0.9f);

        MapHub hub = GetComponent<MapHub>();
        if (hub == null) hub = gameObject.AddComponent<MapHub>();
        hub.Build(level, player, hex, grey, baked);
    }

    // --- Attack mode ("attack": 1) ----------------------------------------------------------------
    // No gold: a sector arena_width x arena_height (default twice the screen each way) with wall
    // rocks for cover and red Life dots, laid out in the level scene. The enemies are the children of
    // the level scene's "Enemies" object, by name:
    //   Red Round ...   a walking red hook hole (RedRound): slow, shoots, round_hp HP
    //   Red Square ...  a red block with no hook (RedSquare): faster, rams, square_hp HP
    // The camera follows the player. There is no clock; destroying every enemy clears the sector and
    // wins (plus clear_reward money), losing the last Life loses.

    public const string EnemiesName = "Enemies";
    public const string RoundName = "Red Round";
    public const string SquareName = "Red Square";

    public bool Attack => level != null && level.Param("attack", 0f) > 0.5f;

    public Rect ArenaRect
    {
        get
        {
            float w = level.Param("arena_width", 35.56f), h = level.Param("arena_height", 20f);
            return new Rect(-w * 0.5f, -h * 0.5f, w, h);
        }
    }

    int enemies;
    public int EnemiesLeft => Mathf.Max(0, enemies - hooksDestroyed);
    public int EnemyCount => enemies;

    Sprite redHole, redSquare;

    void SetUpAttack()
    {
        if (player != null) GameplayCamera.Follow(player.transform, ArenaRect);
        EnemyPathfinder.SetSector(ArenaRect);   // the enemies' A* grid
        Transform group = layout != null ? layout.transform.Find(EnemiesName) : null;
        if (group == null) { Debug.LogWarning("LevelLoader: attack level with no \"" + EnemiesName + "\" object in its scene"); return; }
        foreach (Transform spot in group)
        {
            if (!spot.gameObject.activeSelf) continue;
            if (spot.name.StartsWith(RoundName)) { if (AddRound(spot.position)) enemies++; }
            else if (spot.name.StartsWith(SquareName)) { if (AddSquare(spot.position)) enemies++; }
            else Debug.LogWarning("LevelLoader: '" + spot.name + "' under " + EnemiesName + " is neither a " + RoundName + " nor a " + SquareName, spot);
            spot.gameObject.SetActive(false);   // only a marker
        }
    }

    bool AddRound(Vector2 at)
    {
        SurvivalHookAI ai = AddHole(at, level.Param("start_delay", 1f));
        if (ai == null) return false;
        ai.transform.parent.name = RoundName + " " + (enemies + 1);
        if (redHole == null) redHole = HookHole.CreateSprite(new Color(0.35f, 0.04f, 0.04f), new Color(0.9f, 0.2f, 0.18f));
        SpriteRenderer look = ai.GetComponent<SpriteRenderer>();
        if (look != null) { look.sprite = redHole; look.sortingOrder = 2; }
        ai.transform.localScale = Vector3.one * level.Param("round_size", 1.1f);
        HookHealth health = ai.GetComponent<HookHealth>();
        if (health == null) health = ai.gameObject.AddComponent<HookHealth>();
        health.hp = Mathf.RoundToInt(level.Param("round_hp", 4f));
        health.parts = new[] { ai.transform.parent.gameObject };
        RedRound walker = ai.gameObject.AddComponent<RedRound>();
        walker.target = player != null ? player.transform : null;
        walker.moveSpeed = level.Param("round_speed", 1.2f);
        walker.wakeRange = level.Param("enemy_wake", walker.wakeRange);
        walker.shootRange = level.Param("round_range", walker.shootRange);
        walker.keepDistance = level.Param("round_keep", walker.keepDistance);
        return true;
    }

    bool AddSquare(Vector2 at)
    {
        if (player == null) return false;
        var go = new GameObject(SquareName + " " + (enemies + 1));
        go.transform.position = at;
        go.transform.localScale = Vector3.one * level.Param("square_size", 0.8f);
        if (redSquare == null) redSquare = RedSquare.CreateSprite();
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = redSquare;
        sr.color = RedSquare.Red;
        sr.sortingOrder = 2;
        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;
        // "square_hp": 0 makes it unkillable (survival): no HookHealth, so no HP bar and nothing hurts it
        int squareHp = Mathf.RoundToInt(level.Param("square_hp", 2f));
        if (squareHp > 0) go.AddComponent<HookHealth>().hp = squareHp;
        RedSquare walker = go.AddComponent<RedSquare>();
        walker.target = player.transform;
        walker.moveSpeed = level.Param("square_speed", 2.64f);
        walker.wakeRange = level.Param("enemy_wake", walker.wakeRange);
        return true;
    }

    void UpdateAttack()
    {
        GameManager gm = GameManager.Instance;
        if (!Attack || gm == null || !gm.IsPlaying || enemies == 0 || EnemiesLeft > 0) return;
        gm.AddPlayerMoney(Mathf.RoundToInt(level.Param("clear_reward", 0f)));
        TutorialEvents.Fire(SectorClearedEvent);
        gm.WinRound("Sector cleared!");
    }

    public const string SectorClearedEvent = "sector_cleared";

    // --- Survival levels ("survival": 1, level 2 onward) ---------------------------------------------
    // Survival mode (SurvivalLoader) made finite, on a sector like attack mode's ("arena_width" x
    // "arena_height", the camera following the player, the level scene's wall rocks in the way):
    // the survival look (its dark ground and faint grid lines), Red Squares chasing from the start
    // (the scene's Enemies markers), and survival's hook holes walking after the player (RedRound,
    // along A* paths), more and more of them: "start_hooks", one more every "seconds_per_hook" up
    // to "max_hooks", growing out of the ground "spawn_min".."spawn_max" from the player. They aim
    // like survival's (TreasureInterceptAimer), their hooks get faster ("hook_speed_start" to
    // "hook_speed_end") and their telegraphs and cooldowns shorter ("hardest_scale") as the round
    // goes on. Still alive when the clock ("round_time") runs out is a win, and the gold eaten is
    // the pay; eaten gold turns up again elsewhere after "gold_respawn" seconds.

    public bool Survival => level != null && level.Param("survival", 0f) > 0.5f;

    // Attack and survival play on a sector bigger than the screen
    bool BigMap => Attack || Survival;

    int MaxHooks => Mathf.Max(1, Mathf.RoundToInt(level.Param("max_hooks", 10f)));

    readonly Dictionary<SurvivalHookAI, float> holeBorn = new Dictionary<SurvivalHookAI, float>();
    float nextHoleAt, nextGoldAt = -1f;
    int goldTarget;

    // Survival mode's treasure mix (SurvivalLoader's weights)
    static readonly string[] GoldTypes = { "Gold_Small", "Gold_Medium", "Gold_Large", "Diamond", "Life" };
    static readonly float[] GoldWeights = { 40f, 28f, 14f, 10f, 3f };

    // SurvivalLoader's look
    static readonly Color SurvivalSky = new Color(0.12f, 0.16f, 0.22f);
    static readonly Color SurvivalLines = new Color(1f, 1f, 1f, 0.06f);
    const float SurvivalCell = 2f;

    void SetUpSurvival()
    {
        Rect area = ArenaRect;
        if (player != null) GameplayCamera.Follow(player.transform, area);
        EnemyPathfinder.SetSector(area);
        CreateSurvivalGround(area);

        // The Red Squares that chase from the start
        Transform group = layout != null ? layout.transform.Find(EnemiesName) : null;
        if (group != null)
            foreach (Transform spot in group)
            {
                if (!spot.gameObject.activeSelf) continue;
                if (spot.name.StartsWith(SquareName)) { if (AddSquare(spot.position)) enemies++; }
                else Debug.LogWarning("LevelLoader: survival levels only take " + SquareName + " markers under " + EnemiesName + " ('" + spot.name + "')", spot);
                spot.gameObject.SetActive(false);   // only a marker
            }

        nextHoleAt = level.Param("start_delay", 1.5f);
        goldTarget = Treasure.Active.Count;
    }

    // The dark survival ground with its faint grid, over the whole sector
    void CreateSurvivalGround(Rect area)
    {
        Camera cam = Camera.main;
        if (cam != null) cam.backgroundColor = SurvivalSky;

        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = x == 0 || y == 0 ? SurvivalLines : Color.clear;
        texture.SetPixels(pixels);
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / SurvivalCell, 0, SpriteMeshType.FullRect);

        var go = new GameObject("Survival Ground");
        go.transform.position = area.center;
        SpriteRenderer ground = go.AddComponent<SpriteRenderer>();
        ground.sprite = sprite;
        SpriteRenderer look = player != null ? player.body : null;
        if (look != null) ground.sharedMaterial = look.sharedMaterial;
        ground.drawMode = SpriteDrawMode.Tiled;
        ground.sortingOrder = -100;
        ground.size = area.size;
        spawned.Add(go);
    }

    int WantedHooks(float elapsed)
    {
        int start = Mathf.RoundToInt(level.Param("start_hooks", 1f));
        float every = Mathf.Max(1f, level.Param("seconds_per_hook", 12f));
        return Mathf.Min(MaxHooks, start + Mathf.FloorToInt(elapsed / every));
    }

    // 0 at the start of the round, 1 when the clock runs out
    float SurvivalProgress(GameManager gm) => gm.roundTime > 0f ? Mathf.Clamp01(gm.TimeElapsed / gm.roundTime) : 1f;

    void UpdateSurvival()
    {
        GameManager gm = GameManager.Instance;
        if (!Survival || gm == null || !gm.IsPlaying || player == null) return;
        float elapsed = gm.TimeElapsed;

        // More walking hooks as the round goes on
        if (holes.Count < WantedHooks(elapsed) && elapsed >= nextHoleAt)
        {
            if (FreeSpot(player.transform.position, level.Param("spawn_min", 5f), level.Param("spawn_max", 9f), level.Param("hole_spacing", 2f), out Vector2 at))
                AddWalkingHook(at);
            nextHoleAt = elapsed + 0.6f;
        }

        // Faster hooks and shorter waits as the round goes on, and each new hole grows out of the ground
        float progress = SurvivalProgress(gm);
        float speed = Mathf.Lerp(level.Param("hook_speed_start", 1f), level.Param("hook_speed_end", 1.3f), progress);
        float pace = Mathf.Lerp(1f, level.Param("hardest_scale", 0.55f), progress);
        float extend = level.Param("hook_extend", 18f) * gameSpeed * speed;
        float retract = level.Param("hook_retract", 20f) * gameSpeed * speed;
        float size = level.Param("hole_size", 0.9f), growTime = level.Param("grow_time", 1.2f);
        foreach (SurvivalHookAI ai in holes)
        {
            if (ai == null || ai.hook == null) continue;
            ai.hook.extendSpeed = extend;
            ai.hook.retractSpeed = retract;
            ai.telegraphMin = level.Param("telegraph_min", 1f) * pace;
            ai.telegraphMax = level.Param("telegraph_max", 1.8f) * pace;
            ai.cooldown = level.Param("cooldown", 0.8f) * pace;
            if (holeBorn.TryGetValue(ai, out float born))
            {
                float t = growTime > 0f ? Mathf.Clamp01((Time.time - born) / growTime) : 1f;
                ai.transform.localScale = Vector3.one * size * EaseOutBack(t);
                if (t >= 1f) holeBorn.Remove(ai);
            }
        }

        // Eaten gold turns up again somewhere else, a piece at a time
        if (Treasure.Active.Count < goldTarget)
        {
            if (nextGoldAt < 0f) nextGoldAt = Time.time + level.Param("gold_respawn", 3f);
            else if (Time.time >= nextGoldAt && FreeSpot(player.transform.position, 4f, 40f, 1.2f, out Vector2 spot))
            {
                Spawn(new List<LevelData.Item> { new LevelData.Item { type = PickGold(), position = spot } });
                nextGoldAt = -1f;
            }
        }
    }

    public const string HookSpawnedEvent = "hook_spawned";

    // Survival's hook hole, walking after the player and growing out of the ground
    void AddWalkingHook(Vector2 at)
    {
        float grow = level.Param("grow_time", 1.2f);
        SurvivalHookAI ai = AddHole(at, grow + Random.Range(0.2f, 0.8f));
        if (ai == null) return;
        ai.transform.parent.name = "Walking Hook " + holes.Count;
        ai.transform.localScale = Vector3.zero;
        holeBorn[ai] = Time.time;

        float reach = level.Param("hook_range", 11f);
        TreasureInterceptAimer aimer = ai.gameObject.AddComponent<TreasureInterceptAimer>();
        aimer.lureRadius = level.Param("lure_radius", 8f);
        aimer.intentAngle = level.Param("intent_angle", 30f);
        aimer.maxReach = reach;
        ai.aimer = aimer;

        RedRound walker = ai.gameObject.AddComponent<RedRound>();
        walker.target = player.transform;
        walker.moveSpeed = level.Param("miner_speed", 1.2f);
        walker.wakeRange = 1000f;   // hunting from the moment it is up
        walker.WakeUp();
        walker.shootRange = reach;
        walker.keepDistance = level.Param("miner_keep", 5f);
        TutorialEvents.Fire(HookSpawnedEvent);
    }

    string PickGold()
    {
        float total = 0f;
        foreach (float w in GoldWeights) total += w;
        float roll = Random.value * total;
        for (int i = 0; i < GoldTypes.Length; i++)
        {
            roll -= GoldWeights[i];
            if (roll <= 0f) return GoldTypes[i];
        }
        return GoldTypes[0];
    }

    // A random spot in the sector, `min`..`max` from `around`, clear of the wall rocks and at least
    // `fromHoles` from every hook hole
    bool FreeSpot(Vector2 around, float min, float max, float fromHoles, out Vector2 spot)
    {
        Rect area = ArenaRect;
        area = new Rect(area.x + 1f, area.y + 1f, area.width - 2f, area.height - 2f);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            spot = max >= 30f
                ? new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax))
                : around + Random.insideUnitCircle.normalized * Random.Range(min, max);
            if (!area.Contains(spot) || Vector2.Distance(spot, around) < min || NearWall(spot, 0.8f)) continue;
            bool clear = true;
            foreach (SurvivalHookAI h in holes)
                if (h != null && Vector2.Distance(h.transform.position, spot) < fromHoles) { clear = false; break; }
            if (clear) return true;
        }
        spot = Vector2.zero;
        return false;
    }

    static bool NearWall(Vector2 at, float radius)
    {
        foreach (Collider2D col in Physics2D.OverlapCircleAll(at, radius))
        {
            Rock rock = col.GetComponentInParent<Rock>();
            if (rock != null && rock.isWall) return true;
        }
        return false;
    }

    static float EaseOutBack(float t)
    {
        const float s = 1.7f;
        t -= 1f;
        return 1f + t * t * ((s + 1f) * t + s);
    }
    // --- Compete levels ("compete": 1, level 3 onward) -----------------------------------------------
    // Race for the gold with HookHole circle miners (Level 1 dodge-hole look: dark rim, not green).
    // MinerAI: yellow telegraph/rope at treasure, red at the player. Gold hooked goes to the miners.
    // Time moves when the player moves (TimeMovesWithPlayer): standing still slows the whole world
    // down to "still_time_scale".

    public bool Compete => level != null && level.Param("compete", 0f) > 0.5f;

    Sprite competeHole;

    void SpawnGreenMiners()
    {
        if (player == null) return;
        GameObject prefab = minerPrefab != null ? minerPrefab : Resources.Load<GameObject>(MinerPrefabPath);
        MinerAI template = prefab != null ? prefab.GetComponent<MinerAI>() : null;
        if (template == null || template.telegraph == null)
        {
            Debug.LogWarning("LevelLoader: compete miners need the miner prefab (Resources/" + MinerPrefabPath + ") for its telegraph dots");
            return;
        }
        // Same hole colours as the dodge tutorial / HookHole defaults (not green).
        if (competeHole == null) competeHole = HookHole.CreateSprite(HookHole.HoleColor, HookHole.RimColor);
        SpriteRenderer look = template.hook != null ? template.hook.GetComponent<SpriteRenderer>() : null;

        for (int i = 0; i < level.hooks.Count; i++)
        {
            // Built inactive: swap the hole's survival AI for the miner AI before anything wakes up
            SurvivalHookAI hole = HookHole.Build("Compete Miner " + (i + 1), level.hooks[i], player.transform,
                template.telegraph.dotPrefab, competeHole, look != null ? look.sharedMaterial : null,
                template.hook.extendSpeed, template.hook.retractSpeed);
            GameObject holeObject = hole.gameObject;
            holeObject.transform.localScale = Vector3.one * level.Param("hole_size", 0.9f);
            Hook hook = hole.hook;
            TelegraphLine telegraph = hole.telegraph;
            DestroyImmediate(hole);

            hook.grabsTreasure = true;
            hook.defaultColor = Color.yellow;   // treasure aim (yellow); player aim stays red via MinerAI
            MinerAI miner = holeObject.AddComponent<MinerAI>();
            miner.hook = hook;
            miner.telegraph = telegraph;
            miner.hookOrigin = holeObject.transform;
            miner.treasureColor = Color.yellow;
            miner.playerColor = Color.red;
            PlaceMiner(miner, level.hooks[i], Vector3.zero);   // the level's timings and speeds (Configure)
            holeObject.transform.parent.gameObject.SetActive(true);
            AddHealth(holeObject, holeObject.transform.parent.gameObject);
        }
    }

    void SetUpCompete()
    {
        var time = gameObject.AddComponent<TimeMovesWithPlayer>();
        time.player = player;
        time.stillScale = level.Param("still_time_scale", time.stillScale);
    }

    // Returns the treasure among what it spawned
    List<Treasure> Spawn(List<LevelData.Item> items)
    {
        var treasure = new List<Treasure>();
        var byName = new Dictionary<string, GameObject>();
        foreach (GameObject p in itemPrefabs)
            if (p != null) byName[p.name] = p;

        bool maze = level != null && level.IsMaze;
        float tile = level != null ? level.TileSize : 1f;

        foreach (LevelData.Item item in items)
        {
            if (!byName.TryGetValue(item.type, out GameObject prefab))
            {
                Debug.LogWarning("LevelLoader: no prefab named " + item.type + " in Item Prefabs");
                continue;
            }
            GameObject go = Instantiate(prefab, item.position, Quaternion.identity);
            if (item.scale > 0f) go.transform.localScale = Vector3.one * item.scale;

            if (maze)
            {
                // Battle gold (0.7) is wider than a maze corridor (tile 0.4); shrink to fit the cell.
                if (item.type.StartsWith("Gold"))
                    go.transform.localScale = Vector3.one * (tile * 0.85f);
                else if (item.type == "Diamond")
                    go.transform.localScale = Vector3.one * (tile * 0.75f);

                Rock rock = go.GetComponent<Rock>();
                if (rock != null)
                {
                    // Slightly smaller than the cell so a hook sitting on a floor tile does not
                    // instantly collide with the four neighboring wall rocks.
                    go.transform.localScale = Vector3.one * (tile * 0.88f);
                    rock.MakeWall();
                }
            }

            spawned.Add(go);
            Treasure t = go.GetComponent<Treasure>();
            if (t != null) treasure.Add(t);
        }
        return treasure;
    }

    // The level files still call the player "pig" (pig_start, pig_speed): they are shared with tools/sim
    void SetUpPlayer()
    {
        if (player == null) return;
        player.transform.position = level.pigStart;
        var body = player.GetComponent<Rigidbody2D>();
        if (body != null) body.position = level.pigStart;
        // Battle pig (~0.8) is wider than a maze corridor (tile 0.4); shrink so it fits the ASCII paths.
        if (level.IsMaze)
            player.transform.localScale = Vector3.one * (level.TileSize * 0.9f);

        levelSpeed = level.Param("pig_speed", player.moveSpeed);
        if (Tutorial)
        {
            // Every character gets the level's speed (times its own speedScale) and digest
            player.moveSpeed = levelSpeed * gameSpeed * player.speedScale;
            player.digestTime = level.Param("digest_time", 0f);
            player.digestPerValue = level.Param("digest_per_value", 0f);
            player.digestMaxTime = level.Param("digest_max", player.digestMaxTime);

            // Only Mr. Hyde blocks
            if (player is MrHydeController hyde)
            {
                hyde.canBlock = level.Param("block", 0f) > 0.5f;
                hyde.blockDuration = level.Param("block_duration", hyde.blockDuration);
                hyde.blockCooldown = level.Param("block_cooldown", hyde.blockCooldown);
            }
            return;
        }

        // Level rules go into the player record. The character in the scene only copies them out
        CrossLevelStatus record = CrossLevelStatus.Ensure();
        record.setSpeed(level.Param("pig_speed", record.getSpeed()) * gameSpeed);
        record.setDigestTime(level.Param("digest_time", 0f));
        record.setDigestPerValue(level.Param("digest_per_value", 0f));
        record.setDigestMaxTime(level.Param("digest_max", record.getDigestMaxTime()));
        // Keep the record's block (Mr. Hyde's default, or what the store set) unless the level says so
        if (level.Has("block")) record.setCanBlock(level.Param("block", 0f) > 0.5f);
        record.setBlockDuration(level.Param("block_duration", record.getBlockDuration()));
        record.setBlockCooldown(level.Param("block_cooldown", record.getBlockCooldown()));
        record.onLevelStarted();

        player.moveSpeed = record.getSpeed() * player.speedScale;
        player.digestTime = record.getDigestTime();
        player.digestPerValue = record.getDigestPerValue();
        player.digestMaxTime = record.getDigestMaxTime();
        if (player is MrHydeController blocker)
        {
            blocker.canBlock = record.getCanBlock();
            blocker.blockDuration = record.getBlockDuration();
            blocker.blockCooldown = record.getBlockCooldown();
        }
    }

    void SetUpMiners()
    {
        if (Compete && level.Param("classic_hooks", 0f) <= 0.5f)
        {
            if (minerTemplate != null)
            {
                minerTemplate.gameObject.SetActive(false);
                if (minerTemplate.hook != null) minerTemplate.hook.gameObject.SetActive(false);
                if (minerTemplate.telegraph != null) minerTemplate.telegraph.gameObject.SetActive(false);
            }
            SpawnGreenMiners();
            ApplyLaneHooks();
            return;
        }
        if (Survival)
        {
            // No miners at the start: SetUpSurvival grows hook holes as the round goes on
            if (minerTemplate != null) minerTemplate.gameObject.SetActive(false);
            return;
        }
        if (level.Param("hook_holes", 0f) > 0.5f)
        {
            if (minerTemplate != null) minerTemplate.gameObject.SetActive(false);
            SpawnHoles();
            return;
        }
        if (minerTemplate == null)
        {
            SpawnMiners();
            ApplyLaneHooks();
            return;
        }
        // A level without hooks has no miner: hide the scene's one before its Start runs
        if (level.hooks.Count == 0)
        {
            minerTemplate.gameObject.SetActive(false);
            if (minerTemplate.hook != null) minerTemplate.hook.gameObject.SetActive(false);
            if (minerTemplate.telegraph != null) minerTemplate.telegraph.gameObject.SetActive(false);
            return;
        }

        // Battle miners sit above the hook; maze miners sit on their grid cell so S/H/E/M match the ASCII.
        Vector3 bodyOffset = level.IsMaze
            ? Vector3.zero
            : minerTemplate.transform.position - minerTemplate.hookOrigin.position;

        for (int i = 0; i < level.hooks.Count; i++)
        {
            MinerAI miner = minerTemplate;
            if (i > 0)
            {
                miner = Instantiate(minerTemplate.gameObject).GetComponent<MinerAI>();
                miner.name = minerTemplate.name + " " + (i + 1);
                miner.hook = Instantiate(minerTemplate.hook.gameObject).GetComponent<Hook>();
                miner.hook.name = minerTemplate.hook.name + " " + (i + 1);
                miner.telegraph = Instantiate(minerTemplate.telegraph.gameObject).GetComponent<TelegraphLine>();
                miner.telegraph.name = minerTemplate.telegraph.name + " " + (i + 1);
            }
            PlaceMiner(miner, level.hooks[i], bodyOffset);
            if (miner.pig == null) miner.pig = minerTemplate.pig;
        }
        ApplyLaneHooks();
    }

    // V1: two hostile hooks hunt the pig, the others pull gold through the pig's start,
    // and a per-hook delay keeps the caged gold from leaving with the opening diamond.
    void ApplyLaneHooks()
    {
        int hunter = Mathf.RoundToInt(level.Param("pig_hunter", -1f));
        int hunterTwo = Mathf.RoundToInt(level.Param("pig_hunter_2", -1f));
        int hunterThree = Mathf.RoundToInt(level.Param("pig_hunter_3", -1f));
        bool IsHunter(int index) => index == hunter || index == hunterTwo || index == hunterThree;
        bool crossPig = level.Param("cross_pig", 0f) > 0.5f;
        if (hunter >= 0 || hunterTwo >= 0 || hunterThree >= 0)
        {
            for (int i = 0; i < miners.Count; i++)
            {
                if (IsHunter(i))
                {
                    PaintHunter(miners[i]);
                    if (level.Has("hunter_delay"))
                        miners[i].startDelay = level.Param("hunter_delay", miners[i].startDelay);
                    miners[i].cooldown = level.Param("hunter_cooldown", miners[i].cooldown);
                    miners[i].telegraphMin = level.Param("hunter_telegraph_min", miners[i].telegraphMin);
                    miners[i].telegraphMax = level.Param("hunter_telegraph_max", miners[i].telegraphMax);
                    if (level.Param("hunter_pass_rocks", 0f) > 0.5f)
                        miners[i].hook.passRocks = true;
                    float reach = level.Param("hunter_reach", 0f);
                    if (reach > 0f) miners[i].huntReach = reach;
                }
                else
                {
                    miners[i].shootsPlayer = false;
                    // Still yellow: it aims at treasure. yellow_hits (Level 2+) makes that hook
                    // cost a life when the tip touches the pig; without it the tip passes through.
                    miners[i].hook.hitsPig = level.Param("yellow_hits", 0f) > 0.5f;
                }
            }
        }
        if (crossPig)
        {
            bool kills = !level.Has("hook_kills") || level.Param("hook_kills", 1f) > 0.5f;
            for (int i = 0; i < miners.Count; i++)
            {
                if (IsHunter(i)) continue;
                miners[i].aimThroughPig = true;
                if (kills) miners[i].hook.hitsPig = true;
            }
        }
        if (level.Param("greedy", 0f) > 0.5f)
            for (int i = 0; i < miners.Count; i++)
                if (!IsHunter(i)) miners[i].greedyValue = true;
        for (int i = 0; i < miners.Count; i++)
        {
            string key = "hook_delay_" + i;
            if (level.Has(key))
                miners[i].startDelay = level.Param(key, miners[i].startDelay);
        }
    }

    static Sprite discSprite;

    static Sprite DiscSprite()
    {
        if (discSprite != null) return discSprite;
        const int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float radius = (n - 1) * 0.5f;
        float cx = radius;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                float a = Mathf.Clamp01(radius - d + 0.75f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return discSprite;
    }

    // Black center, brown ring, red claw, red rope, red dashed aim.
    void PaintHunter(MinerAI miner)
    {
        miner.huntPigOnly = true;
        miner.shootsPlayer = true;
        miner.hook.hitsPig = true;
        miner.hook.grabsTreasure = false;   // passes gold; this hook only catches the pig
        miner.name = "Hostile Miner";
        Color red = new Color(0.92f, 0.08f, 0.06f, 1f);
        miner.playerColor = red;
        miner.treasureColor = red;
        miner.hook.Paint(red);
        miner.startDelay = 0.2f;
        miner.cooldown = 0.35f;
        miner.telegraphMin = 0.3f;
        miner.telegraphMax = 0.45f;

        SpriteRenderer body = miner.GetComponent<SpriteRenderer>();
        Sprite disc = DiscSprite();
        int order = body != null ? body.sortingOrder : 0;

        GameObject ring = new GameObject("HunterRing");
        ring.transform.SetParent(miner.transform, false);
        ring.transform.localScale = Vector3.one * 1.42f;
        SpriteRenderer ringRenderer = ring.AddComponent<SpriteRenderer>();
        ringRenderer.sprite = disc;
        ringRenderer.color = new Color(0.55f, 0.34f, 0.1f, 1f);
        ringRenderer.sortingOrder = order;

        if (body != null)
        {
            body.sprite = disc;
            body.color = new Color(0.04f, 0.03f, 0.03f, 1f);
            body.sortingOrder = order + 1;
        }
    }

    void SealWallRocks()
    {
        foreach (Rock rock in FindObjectsByType<Rock>(FindObjectsSortMode.None))
            if (rock != null) rock.MakeWall();
    }

    // No miner in the scene: one copy of the miner prefab (body, hook origin, hook and telegraph)
    // per hook in the level file, so the level doesn't depend on what the Game scene still holds
    void SpawnMiners()
    {
        if (level.hooks.Count == 0) return;
        GameObject prefab = minerPrefab != null ? minerPrefab : Resources.Load<GameObject>(MinerPrefabPath);
        if (prefab == null || prefab.GetComponent<MinerAI>() == null)
        {
            Debug.LogWarning("LevelLoader: no MinerAI in the scene and no miner prefab (Resources/" + MinerPrefabPath + ")");
            return;
        }
        // Made right where it goes, so nothing is ever drawn at the prefab's own position
        MinerAI template = prefab.GetComponent<MinerAI>();
        Vector3 bodyOffset = template.transform.position - template.hookOrigin.position;
        for (int i = 0; i < level.hooks.Count; i++)
        {
            Vector3 at = (Vector3)level.hooks[i] + bodyOffset;
            MinerAI miner = Instantiate(prefab, at, prefab.transform.rotation).GetComponent<MinerAI>();
            miner.name = prefab.name + (i > 0 ? " " + (i + 1) : "");
            PlaceMiner(miner, level.hooks[i], bodyOffset);
        }
    }

    // "hook_holes": 1 (the dodge tutorial): survival mode's hook holes instead of miners, one per
    // hook origin, shooting only at the player. The telegraph dots and the sprite material come
    // from the miner prefab, so nothing new needs assigning
    void SpawnHoles()
    {
        for (int i = 0; i < level.hooks.Count; i++)
            AddHole(level.hooks[i], level.Param("start_delay", 2f) + i * level.Param("hole_stagger", 0.6f));
    }

    // "hook_hp": the hooks can be destroyed (HookHealth): a solid box, and that many hits of Mr.
    // Hyde's Space up close. The "destroy" stage goal counts them
    int hooksDestroyed;

    void AddHealth(GameObject hook, params GameObject[] parts)
    {
        int hp = Mathf.RoundToInt(level.Param("hook_hp", 0f));
        if (hp <= 0 || hook == null || hook.GetComponent<HookHealth>() != null) return;
        HookHealth health = hook.AddComponent<HookHealth>();
        health.hp = hp;
        health.parts = parts;
    }

    void OnEnable() { HookHealth.Destroyed += CountDestroyed; }
    void OnDisable() { HookHealth.Destroyed -= CountDestroyed; }
    void CountDestroyed(HookHealth hook) { hooksDestroyed++; }

    MinerAI holeTemplate;
    Sprite holeSprite;
    readonly List<SurvivalHookAI> holes = new List<SurvivalHookAI>();

    // The hook holes in play (the level's, and any added since)
    public IReadOnlyList<SurvivalHookAI> Holes => holes;

    // One more hook hole at `at`, timed like the level's (SqueezePhase adds them mid-level).
    // Its first shot comes after startDelay seconds
    public SurvivalHookAI AddHole(Vector2 at, float startDelay)
    {
        if (player == null) return null;
        if (holeTemplate == null)
        {
            GameObject prefab = minerPrefab != null ? minerPrefab : Resources.Load<GameObject>(MinerPrefabPath);
            holeTemplate = prefab != null ? prefab.GetComponent<MinerAI>() : null;
            if (holeTemplate == null || holeTemplate.telegraph == null)
            {
                Debug.LogWarning("LevelLoader: hook holes need the miner prefab (Resources/" + MinerPrefabPath + ") for its telegraph dots");
                return null;
            }
            holeSprite = HookHole.CreateSprite(HookHole.HoleColor, HookHole.RimColor);
        }
        SpriteRenderer look = holeTemplate.hook != null ? holeTemplate.hook.GetComponent<SpriteRenderer>() : null;

        SurvivalHookAI ai = HookHole.Build("Hook Hole " + (holes.Count + 1), at, player.transform,
            holeTemplate.telegraph.dotPrefab, holeSprite, look != null ? look.sharedMaterial : null,
            level.Param("hook_extend", holeTemplate.hook.extendSpeed) * gameSpeed,
            level.Param("hook_retract", holeTemplate.hook.retractSpeed) * gameSpeed);
        ai.transform.localScale = Vector3.one * level.Param("hole_size", 0.9f);
        ai.range = 0f;   // the whole arena
        ai.startDelay = startDelay;
        ai.telegraphMin = level.Param("telegraph_min", ai.telegraphMin);
        ai.telegraphMax = level.Param("telegraph_max", ai.telegraphMax);
        ai.cooldown = level.Param("cooldown", ai.cooldown);
        ai.overshoot = level.Param("overshoot", ai.overshoot);
        ai.hook.hitsPig = !level.Has("hook_kills") || level.Param("hook_kills", 1f) > 0.5f;
        ai.transform.parent.gameObject.SetActive(true);
        AddHealth(ai.gameObject, ai.transform.parent.gameObject);
        holes.Add(ai);
        return ai;
    }

    void PlaceMiner(MinerAI miner, Vector3 origin, Vector3 bodyOffset)
    {
        miner.transform.position = origin + bodyOffset;
        miner.hookOrigin.position = origin;   // child of the miner, so set after moving the body
        miner.hook.transform.position = origin;
        if (player != null) miner.pig = player.transform;
        Configure(miner);
        miners.Add(miner);
    }

    void Configure(MinerAI miner)
    {
        miner.startDelay = level.Param("start_delay", miner.startDelay);
        miner.telegraphMin = level.Param("telegraph_min", miner.telegraphMin);
        miner.telegraphMax = level.Param("telegraph_max", miner.telegraphMax);
        miner.cooldown = level.Param("cooldown", miner.cooldown);
        miner.overshoot = level.Param("overshoot", miner.overshoot);
        miner.minTreasureShots = Mathf.RoundToInt(level.Param("min_treasure_shots", miner.minTreasureShots));
        miner.maxTreasureShots = Mathf.RoundToInt(level.Param("max_treasure_shots", miner.maxTreasureShots));
        if (balancedMinerPolicy) miner.strategy = MinerAI.Strategy.Efficient;

        miner.pigShotChance = level.Param("pig_shot_chance", pigShotChance);

        bool kills = !level.Has("hook_kills") || level.Param("hook_kills", 1f) > 0.5f;
        miner.shootsPlayer = kills;
        miner.hook.hitsPig = kills;
        // "hunt_player": every shot at the player, and the hook passes over gold (the world map's eye
        // miners guard the one-time gold instead of taking it). Red, so they read as the evil miners.
        // A yellow miner only goes for gold.
        if (level.Param("hunt_player", 0f) > 0.5f)
        {
            miner.huntPigOnly = true;
            miner.shootsPlayer = true;
            miner.hook.hitsPig = true;
            miner.hook.grabsTreasure = false;
            Color red = new Color(0.92f, 0.08f, 0.06f, 1f);
            miner.playerColor = red;
            miner.treasureColor = red;
            miner.hook.Paint(red);
            SpriteRenderer body = miner.GetComponent<SpriteRenderer>();
            if (body != null)
            {
                body.sprite = HookHole.CreateSprite(new Color(0.62f, 0.07f, 0.05f), new Color(0.95f, 0.18f, 0.1f));
                body.color = Color.white;
            }
        }
        // "hook_wall_grace": how far a shot flies before wall rocks can stop it (0 on the world map,
        // so an eye miner's hook stops at the first rock, even the ones right beside it)
        miner.hook.wallGrace = level.Param("hook_wall_grace", miner.hook.wallGrace);
        miner.hook.extendSpeed = level.Param("hook_extend", miner.hook.extendSpeed) * gameSpeed;
        miner.hook.retractSpeed = level.Param("hook_retract", miner.hook.retractSpeed) * gameSpeed;
        miner.hook.passRocks = level.Param("hooks_pass_rocks", 0f) > 0.5f;
    }

    // A level file's starting_lives wins when it is set above 0; 0 keeps the loader's livesPerLevel
    // (the old files used 0 to mean "don't override").
    int BaseLives(GameManager gm)
    {
        int fromFile = Mathf.RoundToInt(level.Param("starting_lives", 0f));
        if (fromFile > 0) return fromFile;
        if (livesPerLevel > 0) return livesPerLevel;
        return gm.startingLives;
    }

    // Brace +1 already applied this battle, or armed in the store for the next battle (hub preview).
    public static int PendingBraceBonusLives()
    {
        CrossLevelStatus record = CrossLevelStatus.Active;
        if (record == null) return 0;
        int active = record.getBraceBonusLives();
        if (active > 0) return active;
        PlayerSkill brace = record.getSkill("brace");
        return brace != null && brace.armed ? 1 : 0;
    }

    // Hub LIFE bar: core lives from the maze save + orange Brace pip when Brace is armed.
    // Does not consume the item — onLevelStarted still does that when a real battle begins.
    public void ApplyHubBracePreview()
    {
        if (level == null || !level.IsMaze) return;
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        int core = BaseLives(gm);
        MazeHubState.ReadPrefsIfNeeded();
        if (MazeHubState.HasSave && MazeHubState.Lives > 0)
            core = MazeHubState.Lives;

        int pending = PendingBraceBonusLives();
        gm.SetBonusLives(pending);
        gm.startingLives = core + pending;
        gm.SyncLivesFromStart();
    }

    void SetUpRound()
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return;
        gm.endless = level.IsMaze || Attack;   // the hub: no clock, nothing ends it; attack: clearing the sector ends it
        gm.timeUpWins = Survival;   // survival: last until the clock runs out
        gm.roundTime = level.Param("round_time", gm.roundTime);
        if (level.stages.Count > 0) gm.EndWhenTreasureGone = false;
        if (Tutorial)
        {
            // World-map hub is Tutorial=true, so Brace never runs onLevelStarted here — still show
            // an armed Brace as a pending orange life on the LIFE bar / over-head pips.
            if (level.IsMaze) ApplyHubBracePreview();
            else
            {
                gm.startingLives = BaseLives(gm);
                gm.SetBonusLives(0);
                gm.SyncLivesFromStart();
            }
            gm.invincibleTime = level.Param("invincible_time", gm.invincibleTime);
            return;
        }
        CrossLevelStatus player = CrossLevelStatus.Ensure();
        int levelLives = BaseLives(gm);
        int bonus = player.getBraceBonusLives();
        player.setLives(levelLives + player.getExtraLives());
        player.setInvincibleTime(level.Param("invincible_time", player.getInvincibleTime()));
        gm.startingLives = player.getLives();
        gm.SetBonusLives(bonus);
        gm.SyncLivesFromStart();
        gm.invincibleTime = player.getInvincibleTime();
    }

    void Update()
    {
        if (list == null || list.Length == 0) return;
        UpdateGoal();
        UpdateAttack();
        UpdateSurvival();
        UpdateAdvance();
        UpdateGuideFade();

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.nKey.wasPressedThisFrame) Load(current + 1);
        else if (keyboard.pKey.wasPressedThisFrame) Load(current - 1);
    }

    // Tutorial stages: set the hooks up for the stage's goal and put out its items
    void StartStage(int index)
    {
        stage = index;
        LevelData.Stage s = level.stages[stage];
        stageTime = Time.time;
        stageStart = GoalCount(s.goal);

        // Blocking: the miners shoot at the pig as usual. Stealing: they stop shooting and
        // UpdateSteal hands each hook a treasure it has already caught, to pull back past the pig
        bool steal = s.goal == "steal";
        bool quiet = s.goal == "eat";
        foreach (MinerAI miner in miners)
        {
            miner.autoShoot = !steal && !quiet;
            miner.shootsPlayer = !steal && !quiet;
            miner.hook.hitsPig = !steal && !quiet;
        }
        if (player != null)
            player.moveSpeed = (s.pigSpeed >= 0f ? s.pigSpeed : levelSpeed) * gameSpeed * player.speedScale;
        nextPullAt = Time.time + stealPause;
        if (!steal) Spawn(s.items);
        TutorialEvents.Fire("stage_" + s.goal);   // e.g. stage_block, stage_steal: a tutorial step can start on it
    }

    int GoalCount(string goal)
    {
        // "survive" counts whole seconds the player has lasted in this stage (dodge tutorial), so it
        // needs no counter on the character/hook/miner scripts.
        if (goal == "survive") return Mathf.FloorToInt(Mathf.Max(0f, Time.time - stageTime));
        if (player == null) return 0;
        if (goal == "destroy") return hooksDestroyed;
        if (goal == "eat") return player.Eaten;
        return goal == "steal" ? player.TreasureStolen : goal == "block" ? player.HooksBlocked : 0;
    }

    int StageProgress => Mathf.Min(GoalCount(level.stages[stage].goal) - stageStart, level.stages[stage].count);

    // Next stage once this one's count is reached; the last one wins the round. A stage whose
    // treasure is all gone (eaten or taken by the miner) gets a fresh set
    void UpdateGoal()
    {
        GameManager gm = GameManager.Instance;
        if (stage < 0 || gm == null || !gm.IsPlaying) return;

        LevelData.Stage s = level.stages[stage];
        if (StageProgress >= s.count)
        {
            if (stage + 1 < level.stages.Count) StartStage(stage + 1);
            else gm.WinRound("Tutorial done!");
            return;
        }
        if (s.goal == "steal") UpdateSteal(s);
        else if (s.items.Count > 0 && Treasure.Active.Count == 0 && Treasure.OnHooks.Count == 0) Spawn(s.items);
    }

    // Steal stage: once every hook is home, wait stealPause, clear what's left lying around, and
    // start a fresh round with each hook already holding one of the stage's items
    void UpdateSteal(LevelData.Stage s)
    {
        foreach (MinerAI miner in miners)
            if (miner.hook.IsBusy) { nextPullAt = Time.time + stealPause; return; }
        if (Time.time < nextPullAt || s.items.Count == 0) return;

        foreach (Treasure t in new List<Treasure>(Treasure.Active)) Destroy(t.gameObject);
        List<Treasure> fresh = Spawn(s.items);
        for (int i = 0; i < miners.Count && fresh.Count > 0; i++)
        {
            MinerAI miner = miners[i];
            miner.hook.PullBack(miner.hookOrigin.position, fresh[i % fresh.Count], miner.treasureColor, stealHold);
        }
    }

    // Fired once when a won level's money is stored. Listeners must not be required for the next level to load.
    public static event System.Action LevelWon;

    // A listener can hold the countdown. It loads the next level itself by setting AdvanceNow.
    public static bool WaitToAdvance { get; set; }
    public static bool AdvanceNow { get; set; }

    bool moneySettled;

    // When the round is won, wait advanceDelay seconds (the end panel shows the result), then load
    // the next level. A loss stays put so R can retry. The last level doesn't wrap around.
    void UpdateAdvance()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.IsPlaying) return;

        if (!moneySettled)
        {
            moneySettled = true;
            SettleWallet(gm);
        }

        if (!gm.PlayerWon) return;
        if (TutorialDialogue.Playing) { advanceAt = -1f; return; }   // a story after the win: wait for it

        // "win_to_store" (the last tutorial): no result, straight on to the store
        if (WinsToStore)
        {
            if (!leftForStore) { leftForStore = true; GameFlow.Instance.FinishTutorialRun(); }
            return;
        }

        if (advanceAt < 0f)
            advanceAt = Time.time + advanceDelay;
        if (!autoAdvance || IsLastLevel || WaitToAdvance) return;
        if (AdvanceNow || Time.time >= advanceAt)
        {
            AdvanceNow = false;
            Load(current + 1);
        }
    }

    // Single wallet: only a win with a positive (player − miner) margin can credit money, and
    // only the improvement over this battle's best. A loss / tie / negative margin never adds
    // (or subtracts) — negative scores must not enter the wallet.
    void SettleWallet(GameManager gm)
    {
        string name = LevelName();
        if (!CountsMoney(name)) return;

        int delta = gm.PlayerMoney - gm.MinerMoney;
        gm.SetNewRecordLine("");
        if (!gm.PlayerWon || delta <= 0) return;

        // Prefer the map node id (battle_*) so replays share one best-margin key.
        string creditId = name;
        if (GameFlow.Instance != null && GameFlow.Instance.CurrentNode != null
            && !string.IsNullOrEmpty(GameFlow.Instance.CurrentNode.id))
            creditId = GameFlow.Instance.CurrentNode.id;

        int gain = ProgressStore.TryCreditBestMargin(creditId, delta);
        if (gain > 0) gm.SetNewRecordLine("New record! You earned $" + gain + ".");

        if (!Tutorial && LevelWon != null) LevelWon();
    }

    public string LevelName()
    {
        return list != null && current >= 0 && current < list.Length && list[current] != null
            ? list[current].name
            : CurrentLevel.ToString();
    }

    bool WinsToStore => level != null && level.Param("win_to_store", 0f) > 0.5f;
    bool leftForStore;

    // The round result (YOU WIN) stays hidden: a won "win_to_store" level goes straight to the store
    public bool SkipsResult => WinsToStore && GameManager.Instance != null && GameManager.Instance.PlayerWon;

    bool IsLastLevel => current >= list.Length - 1;

    // Switch level by loading its scene (the index is static). Going from the tutorial to a normal
    // level opens the select scene first; it loads this scene again with the picked character
    public void Load(int index)
    {
        bool leavingTutorial = current <= lastTutorial;
        current = (index % list.Length + list.Length) % list.Length;
        if (leavingTutorial && current > lastTutorial && !string.IsNullOrEmpty(selectScene)
            && Application.CanStreamedLevelBeLoaded(selectScene))
        {
            SceneManager.LoadScene(selectScene);
            return;
        }
        // The level's own scene if it has one (it brings the Game scene with it), else the Game scene
        SceneManager.LoadScene(LevelLayout.SceneFor(list[current]));
    }

    void Start()
    {
        hasHud = FindFirstObjectByType<HUDController>() != null;
        if (level != null && level.Param("block", 0f) > 0.5f && player is MrHydeController hyde)
        {
            hyde.canBlock = true;
            if (!Tutorial && CrossLevelStatus.Active != null)
                CrossLevelStatus.Active.setCanBlock(true);
        }
        if (level != null && level.Param("start_carrying", 0f) > 0.5f)
            BeginAlreadyCarrying();
        // After every Awake (including GameManager): restore maze hub money / pose from the battle trip.
        // MapHub (grid world map) has no MazeRuntime — still push DisplayMoney onto GameManager.
        if (level != null && level.IsMaze)
        {
            MazeRuntime maze = GetComponent<MazeRuntime>();
            if (maze != null) maze.RestoreHubIfNeeded();
            else
            {
                MazeHubState.ReadPrefsIfNeeded();
                if (MazeHubState.HasSave) MazeHubState.ApplyToScene(null, player, GameManager.Instance);
                else MazeHubState.ApplyDisplayMoney(GameManager.Instance);
            }
            // Restore overwrites Lives; re-apply armed Brace so the map LIFE bar shows the orange +1.
            ApplyHubBracePreview();
            // Then the HP the map kept from last time (hooks cost HP there; a won level refills it)
            MapHub hub = GetComponent<MapHub>();
            if (hub != null) hub.ApplySavedHp(GameManager.Instance);
        }
    }

    // The diamond is already on the hook when the level appears. The pig is standing on that
    // line and has to block. A greedy level sends this hook back out after the pull.
    void BeginAlreadyCarrying()
    {
        if (miners.Count == 0) return;
        Treasure gem = null;
        foreach (Treasure t in FindObjectsByType<Treasure>(FindObjectsSortMode.None))
        {
            if (t != null && t.type == TreasureType.Diamond) { gem = t; break; }
        }
        if (gem == null) return;
        MinerAI miner = miners[0];
        miner.autoShoot = false;
        if (level.Param("greedy", 0f) > 0.5f)
            miner.resumeAfterScripted = true;
        miner.hook.PullBack(miner.hookOrigin.position, gem, miner.treasureColor, 0f);
    }

    // What happens next once the round is over, for the end panel. Empty while playing, or when a
    // listener (GameFlow, back to the map) handles the win
    public string NextStepHint
    {
        get
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.IsPlaying) return "";
            if (!gm.PlayerWon) return "Press R to retry this level";
            if (WaitToAdvance) return "";   // before IsLastLevel: a map node's list has one level
            if (IsLastLevel) return "All " + list.Length + " levels cleared!";
            if (advanceAt >= 0f) return "Level " + (current + 2) + " in " + Mathf.CeilToInt(advanceAt - Time.time) + "...";
            return "";
        }
    }

    void OnGUI()
    {
        // The level label is only logged when the level builds; N / P / R still work in the editor
        if (!showLevelLabel || string.IsNullOrEmpty(label)) return;
        ScreenFrame.BeginGUI();   // lay out in 1280x720, scaled to the 16:9 frame

        GameManager gm = GameManager.Instance;
        // Not under a story or a tutorial step: OnGUI draws over every canvas, so the guide box would
        // cover the story box (TutorialDialogue, set to Top) and the steps' callouts. It comes back after
        if (gm != null && gm.IsPlaying && !TutorialDialogue.Playing && !TutorialStep.AnyShowing) DrawGuide();
        if (hasHud) return;
        string hint = NextStepHint;
        if (hint != "") GUI.Label(new Rect(10, ScreenFrame.GuiHeight - 54, 700, 24), hint);
    }

    // The guide text (the stage's, else the level's) at the top of the arena, with progress. A new
    // guide shows in full for guideFullTime, then shrinks to its first line (the title) so it stops
    // covering the arena once it has been read
    void DrawGuide()
    {
        if (level == null) return;
        // Maze world map: zone hints only — the big guide banner covers the grid.
        if (level.IsMaze) return;
        string guide;
        string progress = "";
        if (stage >= 0)
        {
            LevelData.Stage s = level.stages[stage];
            if (string.IsNullOrEmpty(s.guide)) return;   // no words: TutorialSteps in the level scene teach it
            string done = s.goal == "steal" ? "Stolen" : s.goal == "survive" ? "Survived" : s.goal == "destroy" ? "Destroyed" : "Blocked";
            string unit = s.goal == "survive" ? "s" : "";
            guide = s.guide;
            progress = done + " " + StageProgress + unit + " / " + s.count + unit;
        }
        else
        {
            if (string.IsNullOrEmpty(level.guide)) return;
            guide = level.guide;
            if (Attack)
                progress = "Enemies left: " + EnemiesLeft + " / " + EnemyCount;
            else if (Survival)
                progress = "Hooks: " + holes.Count + " / " + MaxHooks;
            else if (level.items.Count > 0)
                progress = "Left: " + (Treasure.Active.Count + Treasure.OnHooks.Count);
        }

        if (guide != shownGuide)
        {
            shownGuide = guide;
            guideSince = Time.time;
        }
        int firstLine = guide.IndexOf('\n');
        if (Time.time - guideSince >= guideFullTime && firstLine >= 0) guide = guide.Substring(0, firstLine);
        string text = progress == "" ? guide : guide + "\n" + progress;

        if (guideStyle == null)
        {
            // The HUD's panel color, a little see-through so the arena shows behind it
            Texture2D back = new Texture2D(1, 1);
            back.SetPixel(0, 0, new Color(0.118f, 0.165f, 0.247f, 0.85f));
            back.Apply();
            guideStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(12, 12, 8, 8),
                border = new RectOffset(0, 0, 0, 0),
            };
            guideStyle.normal.background = back;
            guideStyle.normal.textColor = Color.white;
        }
        // Centered over the arena (HUDLayout), just under the top bar, at most guideMaxWidth of the
        // arena's width. In ScreenFrame's 1280x720 space, so it is the same share of every screen
        float arenaWidth = ScreenFrame.GuiWidth * (1f - HUDLayout.SidebarWidth);
        float width = arenaWidth * guideMaxWidth;
        float height = guideStyle.CalcHeight(new GUIContent(text), width);
        float top = ScreenFrame.GuiHeight * HUDLayout.TopBarHeight + 6f;
        guideRect = new Rect((arenaWidth - width) / 2f, top, width, height);

        Color before = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, guideAlpha);
        GUI.Box(guideRect, text, guideStyle);
        GUI.color = before;
    }

    // The guide fades out of the way while the player is under it (or close), so it never hides them
    void UpdateGuideFade()
    {
        bool covered = false;
        Camera cam = Camera.main;
        if (player != null && cam != null && guideRect.width > 0f)
        {
            Vector3 screen = cam.WorldToScreenPoint(player.transform.position);
            Vector2 at = ScreenFrame.ScreenToGui(screen);
            Rect around = new Rect(guideRect.x - 40f, guideRect.y - 40f, guideRect.width + 80f, guideRect.height + 80f);
            covered = screen.z > 0f && around.Contains(at);
        }
        float target = covered ? guideCoveredAlpha : 1f;
        guideAlpha = Mathf.MoveTowards(guideAlpha, target, Time.unscaledDeltaTime * 4f);
    }
}
