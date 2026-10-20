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

    private TextAsset[] list;
    private LevelData level;
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

    // The tutorial: every level up to the last one with a guide or stages (level_01b_block), so the
    // normal level between the two tutorials counts too. It is always played as Mr. Hyde, and the
    // cross-level record (speed, digest, shield, wallet) is left untouched; the record starts when a
    // champion is picked on the select screen
    public static bool Tutorial { get; private set; }

    // Wallet and recorded round money start at Level 1. The three tutorials
    // (eat, dodge, block) and the character pick do not pay or charge.
    public static bool CountsMoney(string levelName)
    {
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

    void Awake()
    {
        if (Requested != null)
        {
            list = new[] { Requested };
            current = 0;
        }
        else
        {
            list = levels != null && levels.Length > 0 ? levels : LoadFromResources();
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
        int seed = Mathf.RoundToInt(level.Param("random_seed", randomSeed));
        if (seed != 0) Random.InitState(seed);
        Build();
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
            if (!string.IsNullOrEmpty(data.guide) || data.stages.Count > 0) last = i;
        }
        return last;
    }

    static TextAsset[] LoadFromResources()
    {
        TextAsset[] found = Resources.LoadAll<TextAsset>("Levels");
        System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
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
            int champion = Tutorial ? ChampionSpawner.MrHyde : SavedChampion();
            ChampionSpawner.Chosen = champion;
            if (!Tutorial)
            {
                // Same champion's record, so wallet and skills survive the scene change. A missing
                // or wrong record (after a domain-reload-off restart) is rebuilt from the save.
                System.Type want = ChampionSpawner.RecordType(champion);
                if (CrossLevelStatus.Active == null || CrossLevelStatus.Active.GetType() != want)
                    CrossLevelStatus.StartFresh(want);
            }
            PlayerCharacter spawned = spawner.Spawn(champion);
            if (spawned != null) player = spawned;
        }
        if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
        if (minerTemplate == null) minerTemplate = FindFirstObjectByType<MinerAI>();

        if (clearSceneItems) ClearItems();
        if (screenWalls) new GameObject("ArenaWalls").AddComponent<ArenaWalls>();
        SpawnItems();
        SetUpPlayer();
        SetUpMiners();
        SetUpRound();
        if (level.stages.Count > 0) StartStage(0);

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
            go.SetActive(false);   // leaves Treasure.Active now, not at the end of the frame
            Destroy(go);
        }
    }

    void SpawnItems() { Spawn(level.items); }

    // Returns the treasure among what it spawned
    List<Treasure> Spawn(List<LevelData.Item> items)
    {
        var treasure = new List<Treasure>();
        var byName = new Dictionary<string, GameObject>();
        foreach (GameObject p in itemPrefabs)
            if (p != null) byName[p.name] = p;

        foreach (LevelData.Item item in items)
        {
            if (!byName.TryGetValue(item.type, out GameObject prefab))
            {
                Debug.LogWarning("LevelLoader: no prefab named " + item.type + " in Item Prefabs");
                continue;
            }
            GameObject go = Instantiate(prefab, item.position, Quaternion.identity);
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

        levelSpeed = level.Param("pig_speed", player.moveSpeed);
        if (Tutorial)
        {
            // Every character gets the level's speed (times its own speedScale) and digest
            player.moveSpeed = levelSpeed * gameSpeed * player.speedScale;
            player.digestTime = level.Param("digest_time", 0f);
            player.digestPerValue = level.Param("digest_per_value", 0f);

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
        // Keep the record's block (Mr. Hyde's default, or what the store set) unless the level says so
        if (level.Has("block")) record.setCanBlock(level.Param("block", 0f) > 0.5f);
        record.setBlockDuration(level.Param("block_duration", record.getBlockDuration()));
        record.setBlockCooldown(level.Param("block_cooldown", record.getBlockCooldown()));
        record.onLevelStarted();

        player.moveSpeed = record.getSpeed() * player.speedScale;
        player.digestTime = record.getDigestTime();
        player.digestPerValue = record.getDigestPerValue();
        if (player is MrHydeController blocker)
        {
            blocker.canBlock = record.getCanBlock();
            blocker.blockDuration = record.getBlockDuration();
            blocker.blockCooldown = record.getBlockCooldown();
        }
    }

    void SetUpMiners()
    {
        if (minerTemplate == null)
        {
            Debug.LogWarning("LevelLoader: no MinerAI in the scene to copy");
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

        // The template's body sits this far from its hook origin; copies keep the same offset
        Vector3 bodyOffset = minerTemplate.transform.position - minerTemplate.hookOrigin.position;

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
            Vector3 origin = level.hooks[i];
            miner.transform.position = origin + bodyOffset;
            miner.hookOrigin.position = origin;   // child of the miner, so set after moving the body
            miner.hook.transform.position = origin;
            miner.pig = player != null ? player.transform : minerTemplate.pig;
            Configure(miner);
            miners.Add(miner);
        }
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
        miner.hook.extendSpeed = level.Param("hook_extend", miner.hook.extendSpeed) * gameSpeed;
        miner.hook.retractSpeed = level.Param("hook_retract", miner.hook.retractSpeed) * gameSpeed;
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

    void SetUpRound()
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return;
        gm.roundTime = level.Param("round_time", gm.roundTime);
        if (Tutorial)
        {
            gm.startingLives = BaseLives(gm);
            gm.invincibleTime = level.Param("invincible_time", gm.invincibleTime);
            return;
        }
        CrossLevelStatus player = CrossLevelStatus.Ensure();
        int levelLives = BaseLives(gm);
        player.setLives(levelLives + player.getExtraLives());
        player.setInvincibleTime(level.Param("invincible_time", player.getInvincibleTime()));
        gm.startingLives = player.getLives();
        gm.invincibleTime = player.getInvincibleTime();
    }

    void Update()
    {
        if (list == null || list.Length == 0) return;
        UpdateGoal();
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
        foreach (MinerAI miner in miners)
        {
            miner.autoShoot = !steal;
            miner.shootsPlayer = !steal;
            miner.hook.hitsPig = !steal;
        }
        if (player != null)
            player.moveSpeed = (s.pigSpeed >= 0f ? s.pigSpeed : levelSpeed) * gameSpeed * player.speedScale;
        nextPullAt = Time.time + stealPause;
        if (!steal) Spawn(s.items);
    }

    int GoalCount(string goal)
    {
        // "survive" counts whole seconds the player has lasted in this stage (dodge tutorial), so it
        // needs no counter on the character/hook/miner scripts.
        if (goal == "survive") return Mathf.FloorToInt(Mathf.Max(0f, Time.time - stageTime));
        if (player == null) return 0;
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

        if (advanceAt < 0f)
            advanceAt = Time.time + advanceDelay;
        if (!autoAdvance || IsLastLevel || WaitToAdvance) return;
        if (AdvanceNow || Time.time >= advanceAt)
        {
            AdvanceNow = false;
            Load(current + 1);
        }
    }

    // Level 1 onward: a win pays how much you beat the miner by. A loss subtracts that same gap.
    // The wallet may go negative. Tutorials and character pick do not move it.
    void SettleWallet(GameManager gm)
    {
        string name = LevelName();
        if (!CountsMoney(name)) return;

        CrossLevelStatus record = CrossLevelStatus.Ensure();
        int delta = gm.PlayerMoney - gm.MinerMoney;
        if (gm.PlayerWon)
        {
            record.bank(name, delta);
            if (!Tutorial && LevelWon != null) LevelWon();
            return;
        }
        record.addWallet(-Mathf.Abs(delta));
    }

    public string LevelName()
    {
        return list != null && current >= 0 && current < list.Length && list[current] != null
            ? list[current].name
            : CurrentLevel.ToString();
    }

    bool IsLastLevel => current >= list.Length - 1;

    // Switch level by reloading the scene (the index is static). Going from the tutorial to a normal
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
        if (GameManager.Instance != null) GameManager.Instance.Restart();
        else UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    void Start()
    {
        hasHud = FindFirstObjectByType<HUDController>() != null;
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
        if (gm != null && gm.IsPlaying) DrawGuide();
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
        string guide;
        string progress = "";
        if (stage >= 0)
        {
            LevelData.Stage s = level.stages[stage];
            string done = s.goal == "steal" ? "Stolen" : s.goal == "survive" ? "Survived" : "Blocked";
            string unit = s.goal == "survive" ? "s" : "";
            guide = s.guide;
            progress = done + " " + StageProgress + unit + " / " + s.count + unit;
        }
        else
        {
            if (string.IsNullOrEmpty(level.guide)) return;
            guide = level.guide;
            if (level.items.Count > 0)
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
