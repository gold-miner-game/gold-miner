using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Builds the arena from a level file made by tools/sim/gold_miner_heuristic.py (levels command).
// Put it in the Game scene: before anything else starts it removes the scene's treasure, spawns the
// level's items from the item prefabs, moves the pig, makes one miner + hook + telegraph per hook
// origin (copies of the scene's own, so they keep their look), and applies the level's speeds and
// timings. N / P load the next / previous level.
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
    public bool showLevelLabel = true;

    // The level index survives GameManager.Restart (which reloads the scene)
    private static int current = -1;

    private TextAsset[] list;
    private LevelData level;
    private string label = "";
    private readonly List<GameObject> spawned = new List<GameObject>();

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

        label = $"Level {current + 1}/{list.Length}: {level.items.Count} gold, {level.hooks.Count} hook(s)";
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

    void SpawnItems()
    {
        var byName = new Dictionary<string, GameObject>();
        foreach (GameObject p in itemPrefabs)
            if (p != null) byName[p.name] = p;

        foreach (LevelData.Item item in level.items)
        {
            if (!byName.TryGetValue(item.type, out GameObject prefab))
            {
                Debug.LogWarning("LevelLoader: no prefab named " + item.type + " in Item Prefabs");
                continue;
            }
            spawned.Add(Instantiate(prefab, item.position, Quaternion.identity));
        }
    }

    void SetUpPig()
    {
        if (pig == null) return;
        pig.transform.position = level.pigStart;
        var body = pig.GetComponent<Rigidbody2D>();
        if (body != null) body.position = level.pigStart;

        pig.moveSpeed = level.Param("pig_speed", pig.moveSpeed);
        pig.digestTime = level.Param("digest_time", 0f);
        pig.digestPerValue = level.Param("digest_per_value", 0f);
    }

    void SetUpMiners()
    {
        if (minerTemplate == null)
        {
            Debug.LogWarning("LevelLoader: no MinerAI in the scene to copy");
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

        bool kills = !level.Has("hook_kills") || level.Param("hook_kills", 1f) > 0.5f;
        miner.shootsPlayer = kills;
        miner.hook.hitsPig = kills;
        miner.hook.extendSpeed = level.Param("hook_extend", miner.hook.extendSpeed);
        miner.hook.retractSpeed = level.Param("hook_retract", miner.hook.retractSpeed);
    }

    void SetUpRound()
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return;
        gm.roundTime = level.Param("round_time", gm.roundTime);
        gm.startingLives = Mathf.RoundToInt(level.Param("starting_lives", gm.startingLives));
        gm.invincibleTime = level.Param("invincible_time", gm.invincibleTime);
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || list == null || list.Length == 0) return;
        if (keyboard.nKey.wasPressedThisFrame) Load(current + 1);
        else if (keyboard.pKey.wasPressedThisFrame) Load(current - 1);
    }

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
    }
}
