using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Builds the arena from a level file made by tools/sim/gold_miner_heuristic.py (levels command).
// Put it in the Game scene: before anything else starts it removes the scene's treasure, spawns the
// level's items from the item prefabs, moves the pig, makes one miner + hook + telegraph per hook
// origin (copies of the scene's own, so they keep their look), and applies the level's speeds and
// timings. N / P load the next / previous level; winning a round moves on to the next one by itself.
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
    public PigController pig;              // empty: the first PigController in the scene
    public bool clearSceneItems = true;    // remove treasure, rocks and lives already placed in the scene

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
    public bool showLevelLabel = true;

    [Header("Tutorial")]
    [Tooltip("Steal stage: seconds the hook rests on its treasure before pulling it back")]
    public float stealHold = 1.5f;
    [Tooltip("Steal stage: seconds between one pull ending and the next starting")]
    public float stealPause = 1f;

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
    private float nextPullAt;        // steal stage: when the hooks get their next treasure
    private GUIStyle guideStyle;

    public static int CurrentLevel => current + 1;

    void Awake()
    {
        list = levels != null && levels.Length > 0 ? levels : LoadFromResources();
        if (list.Length == 0)
        {
            Debug.LogWarning("LevelLoader: no level files (assign them, or put them in Resources/Levels)");
            return;
        }
        if (current < 0 || current >= list.Length) current = Mathf.Clamp(startLevel - 1, 0, list.Length - 1);

        level = LevelData.Parse(list[current].text);
        int seed = Mathf.RoundToInt(level.Param("random_seed", randomSeed));
        if (seed != 0) Random.InitState(seed);
        Build();
    }

    static TextAsset[] LoadFromResources()
    {
        TextAsset[] found = Resources.LoadAll<TextAsset>("Levels");
        System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
        return found;
    }

    void Build()
    {
        if (pig == null) pig = FindFirstObjectByType<PigController>();
        if (minerTemplate == null) minerTemplate = FindFirstObjectByType<MinerAI>();

        if (clearSceneItems) ClearItems();
        SpawnItems();
        SetUpPig();
        SetUpMiners();
        SetUpRound();
        if (level.stages.Count > 0) StartStage(0);

        label = $"Level {current + 1}/{list.Length}: {level.items.Count} gold, {level.hooks.Count} hook(s)";
        if (pig != null && pig.canBlock) label += "  [Space] block";
        if (level.info.TryGetValue("target_win", out object target) && target is double t)
            label += $"  (balanced for pig win {t:P0})";
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

    void SetUpPig()
    {
        if (pig == null) return;
        pig.transform.position = level.pigStart;
        var body = pig.GetComponent<Rigidbody2D>();
        if (body != null) body.position = level.pigStart;

        pig.moveSpeed = level.Param("pig_speed", pig.moveSpeed) * gameSpeed;
        pig.digestTime = level.Param("digest_time", 0f);
        pig.digestPerValue = level.Param("digest_per_value", 0f);

        pig.canBlock = level.Param("block", 0f) > 0.5f;
        pig.blockDuration = level.Param("block_duration", pig.blockDuration);
        pig.blockCooldown = level.Param("block_cooldown", pig.blockCooldown);
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
            miner.pig = pig != null ? pig.transform : minerTemplate.pig;
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

    void SetUpRound()
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return;
        gm.roundTime = level.Param("round_time", gm.roundTime);
        gm.startingLives = livesPerLevel > 0
            ? livesPerLevel
            : Mathf.RoundToInt(level.Param("starting_lives", gm.startingLives));
        gm.invincibleTime = level.Param("invincible_time", gm.invincibleTime);
    }

    void Update()
    {
        if (list == null || list.Length == 0) return;
        UpdateGoal();
        UpdateAdvance();

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
        if (pig != null)
            pig.moveSpeed = (s.pigSpeed >= 0f ? s.pigSpeed : level.Param("pig_speed", pig.moveSpeed)) * gameSpeed;
        nextPullAt = Time.time + stealPause;
        if (!steal) Spawn(s.items);
    }

    int GoalCount(string goal)
    {
        if (pig == null) return 0;
        return goal == "steal" ? pig.TreasureStolen : goal == "block" ? pig.HooksBlocked : 0;
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

    // When the round is won, wait advanceDelay seconds (the end panel shows the result), then load
    // the next level. A loss stays put so R can retry. The last level doesn't wrap around.
    void UpdateAdvance()
    {
        GameManager gm = GameManager.Instance;
        if (!autoAdvance || gm == null || gm.IsPlaying || !gm.PlayerWon || IsLastLevel) return;

        if (advanceAt < 0f) advanceAt = Time.time + advanceDelay;
        else if (Time.time >= advanceAt) Load(current + 1);
    }

    bool IsLastLevel => current >= list.Length - 1;

    // Switch level by reloading the scene (the index is static)
    public void Load(int index)
    {
        current = (index % list.Length + list.Length) % list.Length;
        if (GameManager.Instance != null) GameManager.Instance.Restart();
        else UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        if (!showLevelLabel || string.IsNullOrEmpty(label)) return;
        GUI.Label(new Rect(10, Screen.height - 30, 700, 24), label + "   [N] next  [P] previous  [R] restart");

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.IsPlaying) DrawGuide();
        if (gm == null || gm.IsPlaying) return;
        string status;
        if (!gm.PlayerWon) status = "Press R to retry this level";
        else if (IsLastLevel) status = "All " + list.Length + " levels cleared!";
        else if (advanceAt >= 0f)
            status = "Level " + (current + 2) + " in " + Mathf.CeilToInt(advanceAt - Time.time) + "...";
        else return;
        GUI.Label(new Rect(10, Screen.height - 54, 700, 24), status);
    }

    // The guide text (the stage's, else the level's) in a box under the HUD's money bar, with progress
    void DrawGuide()
    {
        if (level == null) return;
        string text;
        if (stage >= 0)
        {
            LevelData.Stage s = level.stages[stage];
            string done = s.goal == "steal" ? "Stolen" : "Blocked";
            text = s.guide + "\n" + done + " " + StageProgress + " / " + s.count;
        }
        else
        {
            if (string.IsNullOrEmpty(level.guide)) return;
            text = level.guide;
            if (level.items.Count > 0)
                text += "\nLeft: " + (Treasure.Active.Count + Treasure.OnHooks.Count);
        }

        if (guideStyle == null)
        {
            guideStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(16, 16, 12, 12),
            };
            guideStyle.normal.textColor = Color.white;
        }
        float width = Mathf.Min(760f, Screen.width - 40f);
        float height = guideStyle.CalcHeight(new GUIContent(text), width);
        GUI.Box(new Rect((Screen.width - width) / 2f, 54f, width, height), text, guideStyle);
    }
}
