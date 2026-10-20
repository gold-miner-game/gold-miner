using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Owner: Butian (Survival mode)
// Runs the Survival scene, the map node after Level 8. It does not use LevelLoader or a level file:
// the map has no edge and the camera follows the picked champion wherever it goes. Hooks keep growing
// out of the ground around the player, more of them the longer the run lasts, and gold and diamonds
// keep turning up nearby. A survival hook has one aim, the player: it never takes treasure, so all
// of it is the player's to eat. The run ends with the last Life. The score is the time survived and
// the gold eaten, and the gold goes into the wallet.
// Everything is built at runtime. The scene only needs a camera and this component with its prefabs
// (Tools > Gold Miner > Build Flow Scenes wires them).
[DefaultExecutionOrder(-1000)]
public class SurvivalLoader : MonoBehaviour
{
    [System.Serializable]
    public class ItemChance
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
    }

    [Header("Prefabs")]
    [Tooltip("Same order as ChampionSpawner: 0 Mr. Hyde, 1 JK. Rolling, 2 Scrat, 3 Rero Rero")]
    public GameObject[] champions = new GameObject[4];
    [Tooltip("What turns up on the map, and how often")]
    public ItemChance[] items;
    public GameObject telegraphDot;

    [Header("Player")]
    public float playerSpeed = 5f;          // like a level's pig_speed: times gameSpeed and the character's speedScale
    [Range(0.1f, 2f)] public float gameSpeed = 0.8f;   // scales the player and the hooks, as in LevelLoader
    public int startingLives = 3;           // plus the record's extra Lives from the store
    public float invincibleTime = 1.5f;
    public float digestTime = 0.48f;
    public float digestPerValue = 0.0045f;

    [Header("Camera")]
    [Tooltip("Orthographic half height of the view, in world units")]
    public float viewSize = 6f;
    public float followSharpness = 6f;      // higher = the camera keeps up more tightly

    [Header("Hooks")]
    public int startHooks = 1;
    [Tooltip("One more hook every this many seconds")]
    public float secondsPerExtraHook = 15f;
    public int maxHooks = 12;
    public float spawnMinDistance = 5f;     // from the player
    public float spawnMaxDistance = 9f;
    public float hookSpacing = 2f;          // no two holes closer than this
    [Tooltip("A hook only shoots while the player is this close to it")]
    public float hookRange = 11f;
    [Tooltip("A hook this far behind the player sinks back into the ground")]
    public float despawnDistance = 20f;
    [Tooltip("Seconds a new hook takes to grow out of the ground before its first shot")]
    public float growTime = 1.2f;
    public float hookExtend = 14f;
    public float hookRetract = 16f;
    [Tooltip("Survival hooks fly this much faster than the speeds above (extend and retract)")]
    public float hookSpeedScale = 1.3f;
    public float telegraphMin = 1f;
    public float telegraphMax = 1.8f;
    public float cooldown = 0.8f;
    public float overshoot = 0.6f;
    [Tooltip("Telegraphs and cooldowns shrink to this fraction of the above...")]
    [Range(0.1f, 1f)] public float hardestScale = 0.55f;
    [Tooltip("...after this many seconds")]
    public float secondsToHardest = 180f;

    [Header("Aim")]
    [Tooltip("Intercept: catch the player halfway to the gold it is heading for, else shoot straight at it " +
             "(TreasureInterceptAimer). Lead: aim where its current velocity takes it (LinearHookAimer). " +
             "Direct: aim at where it is")]
    public AimMode aim = AimMode.Intercept;
    [Tooltip("Intercept: treasure this close to the player can be its goal")]
    public float lureRadius = 8f;
    [Tooltip("Intercept: treasure within this angle (degrees) of the player's movement can be its goal")]
    [Range(5f, 90f)] public float intentAngle = 30f;
    [Tooltip("Lead: how much of the movement to lead by (0 = aim at where it is, 1 = the full guess)")]
    [Range(0f, 1f)] public float leadAmount = 1f;
    [Tooltip("Lead: never guess further ahead than this many seconds")]
    public float maxLeadTime = 3f;

    public enum AimMode { Intercept, Lead, Direct }

    [Header("Treasure")]
    [Tooltip("Treasure kept within reach of the player")]
    public int treasureNearby = 14;
    public float treasureMinDistance = 6f;
    public float treasureMaxDistance = 15f;
    public float treasureDespawn = 22f;

    [Header("Look")]
    public Color groundLines = new Color(1f, 1f, 1f, 0.06f);
    public float groundCell = 2f;
    public Color holeColor = new Color(0.07f, 0.05f, 0.04f);
    public Color holeRim = new Color(0.42f, 0.3f, 0.18f);
    public float holeSize = 0.9f;

    public const string BestTimeKey = "survival_best_time";   // PlayerPrefs; the main menu shows it too

    class Hunter
    {
        public GameObject root;
        public Transform hole;
        public SurvivalHookAI ai;
        public float born;
    }

    GameManager gm;
    PlayerCharacter player;
    Camera cam;
    Material spriteMaterial;
    Sprite holeSprite;
    SpriteRenderer ground;
    readonly List<Hunter> hunters = new List<Hunter>();
    readonly List<GameObject> loot = new List<GameObject>();
    float nextHookAt;
    int hunterCount;
    bool runOver;
    float bestTime;
    bool newBest;
    GUIStyle boxStyle;
    GUIStyle titleStyle;
    GUIStyle textStyle;

    float Elapsed => gm != null ? gm.TimeSurvived : 0f;

    void Awake()
    {
        // A battle's random_seed may have fixed the sequence; every run here should differ
        Random.InitState(System.Environment.TickCount);
        bestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);

        SetUpRound();
        SetUpPlayer();
        SetUpCamera();
        CreateGround();
        holeSprite = CreateHoleSprite();

        // A few treasures close by from the start, the rest further out
        for (int i = 0; i < treasureNearby; i++)
            SpawnTreasure(i < treasureNearby / 3 ? 2.5f : treasureMinDistance, treasureMaxDistance);
        nextHookAt = 0.5f;
    }

    // The round: endless, with the starting Lives (plus any the store sold) and no clock
    void SetUpRound()
    {
        CrossLevelStatus record = EnsureRecord();
        int lives = startingLives + (record != null ? record.getExtraLives() : 0);
        if (record != null)
        {
            record.setLives(lives);
            record.setInvincibleTime(invincibleTime);
        }

        // Inactive while it is set up, so its Awake reads these values
        var go = new GameObject("GameManager");
        go.SetActive(false);
        gm = go.AddComponent<GameManager>();
        gm.endless = true;
        gm.startingLives = lives;
        gm.invincibleTime = invincibleTime;
        go.SetActive(true);
    }

    // The champion picked on the role-pick screen, else whatever ChampionSpawner remembers
    static int SavedChampion()
    {
        if (ProgressStore.HasPickedCharacter
            && int.TryParse(ProgressStore.SelectedCharacter, out int picked) && picked >= 0)
            return picked;
        return ChampionSpawner.Chosen >= 0 ? ChampionSpawner.Chosen : ChampionSpawner.MrHyde;
    }

    // GameFlow sets the record up before loading this scene; this covers opening the scene directly
    static CrossLevelStatus EnsureRecord()
    {
        int champion = SavedChampion();
        ChampionSpawner.Chosen = champion;
        System.Type want = ChampionSpawner.RecordType(champion);
        if (CrossLevelStatus.Active == null || CrossLevelStatus.Active.GetType() != want)
            CrossLevelStatus.StartFresh(want);
        return CrossLevelStatus.Active;
    }

    void SetUpPlayer()
    {
        int champion = ChampionSpawner.Chosen;
        GameObject prefab = champion >= 0 && champion < champions.Length ? champions[champion] : null;
        if (prefab == null && champions.Length > 0) prefab = champions[0];
        if (prefab == null)
        {
            Debug.LogWarning("SurvivalLoader: no champion prefabs assigned");
            return;
        }

        GameObject go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        go.name = prefab.name;
        player = go.GetComponent<PlayerCharacter>();
        if (player == null) return;

        // Hooks and the ground share the champion's sprite material, so they render the same way
        SpriteRenderer body = player.body != null ? player.body : go.GetComponentInChildren<SpriteRenderer>();
        if (body != null) spriteMaterial = body.sharedMaterial;

        // The same rules a battle gives the record (LevelLoader.SetUpPlayer), from this script instead
        CrossLevelStatus record = CrossLevelStatus.Active;
        if (record != null)
        {
            record.setSpeed(playerSpeed * gameSpeed);
            record.setDigestTime(digestTime);
            record.setDigestPerValue(digestPerValue);
            record.onLevelStarted();
        }
        player.moveSpeed = (record != null ? record.getSpeed() : playerSpeed * gameSpeed) * player.speedScale;
        player.digestTime = record != null ? record.getDigestTime() : digestTime;
        player.digestPerValue = record != null ? record.getDigestPerValue() : digestPerValue;
        if (player is MrHydeController hyde)
        {
            hyde.canBlock = true;
            if (record != null)
            {
                hyde.blockDuration = record.getBlockDuration();
                hyde.blockCooldown = record.getBlockCooldown();
            }
        }
    }

    // The whole screen is the arena here (no sidebar), and the camera starts on the player
    void SetUpCamera()
    {
        cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = viewSize;
        cam.rect = ScreenFrame.Viewport;
        Vector3 at = player != null ? player.transform.position : Vector3.zero;
        cam.transform.position = new Vector3(at.x, at.y, cam.transform.position.z);
    }

    void Update()
    {
        if (gm == null) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            LeaveToMap();
            return;
        }

        if (!gm.IsPlaying)
        {
            if (!runOver) EndRun();
            return;
        }

        UpdateHunters();
        UpdateTreasure();
    }

    void LateUpdate()
    {
        if (cam == null) return;
        if (player != null)
        {
            Vector3 from = cam.transform.position;
            Vector3 to = new Vector3(player.transform.position.x, player.transform.position.y, from.z);
            cam.transform.position = Vector3.Lerp(from, to, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        }
        if (ground != null)
        {
            // Snapped to whole cells under the camera, so the lines look fixed to the map
            Vector3 c = cam.transform.position;
            ground.transform.position = new Vector3(Mathf.Floor(c.x / groundCell) * groundCell,
                                                    Mathf.Floor(c.y / groundCell) * groundCell, 0f);
        }
    }

    // ---- Hooks ----

    int WantedHooks => Mathf.Min(maxHooks, startHooks + Mathf.FloorToInt(Elapsed / Mathf.Max(1f, secondsPerExtraHook)));

    // 0 at the start, 1 once the run reaches secondsToHardest
    float Difficulty => secondsToHardest > 0f ? Mathf.Clamp01(Elapsed / secondsToHardest) : 1f;

    void UpdateHunters()
    {
        if (player == null) return;
        Vector2 at = player.transform.position;
        float pace = Mathf.Lerp(1f, hardestScale, Difficulty);

        for (int i = hunters.Count - 1; i >= 0; i--)
        {
            Hunter h = hunters[i];
            if (h.root == null) { hunters.RemoveAt(i); continue; }

            // Left far behind: sink back into the ground; a new one grows nearer the player
            if (Vector2.Distance(h.hole.position, at) > despawnDistance)
            {
                Destroy(h.root);
                hunters.RemoveAt(i);
                continue;
            }

            float grow = growTime > 0f ? Mathf.Clamp01((Time.time - h.born) / growTime) : 1f;
            h.hole.localScale = Vector3.one * (holeSize * EaseOutBack(grow));

            h.ai.telegraphMin = telegraphMin * pace;
            h.ai.telegraphMax = telegraphMax * pace;
            h.ai.cooldown = cooldown * pace;
        }

        if (hunters.Count < WantedHooks && Time.time >= nextHookAt)
        {
            if (SpawnHunter(at)) nextHookAt = Time.time + 0.6f;
        }
    }

    bool SpawnHunter(Vector2 around)
    {
        if (telegraphDot == null)
        {
            Debug.LogWarning("SurvivalLoader: no TelegraphDot prefab assigned");
            nextHookAt = float.PositiveInfinity;
            return false;
        }
        if (!FindHoleSpot(around, out Vector2 spot)) return false;

        hunterCount++;
        // Built inactive, so every Awake/OnEnable sees its references already set
        var root = new GameObject("Hunter " + hunterCount);
        root.SetActive(false);
        root.transform.position = spot;

        var holeObject = new GameObject("Hole");
        holeObject.transform.SetParent(root.transform, false);
        holeObject.transform.localScale = Vector3.zero;
        SpriteRenderer holeRenderer = holeObject.AddComponent<SpriteRenderer>();
        holeRenderer.sprite = holeSprite;
        if (spriteMaterial != null) holeRenderer.sharedMaterial = spriteMaterial;
        holeRenderer.sortingOrder = -50;   // under treasure and the player

        // The hook's own sprite is hidden: it only lends the material and sorting to the rope
        var hookObject = new GameObject("Hook");
        hookObject.transform.SetParent(root.transform, false);
        SpriteRenderer hookRenderer = hookObject.AddComponent<SpriteRenderer>();
        if (spriteMaterial != null) hookRenderer.sharedMaterial = spriteMaterial;
        hookRenderer.sortingOrder = 5;
        Hook hook = hookObject.AddComponent<Hook>();
        hook.extendSpeed = hookExtend * hookSpeedScale * gameSpeed;
        hook.retractSpeed = hookRetract * hookSpeedScale * gameSpeed;
        hook.hitsPig = true;
        hook.grabsTreasure = false;
        hook.defaultColor = Color.red;

        var telegraphObject = new GameObject("Telegraph");
        telegraphObject.transform.SetParent(root.transform, false);
        TelegraphLine telegraph = telegraphObject.AddComponent<TelegraphLine>();
        telegraph.dotPrefab = telegraphDot;

        // Survival's own AI: every shot is a red one at the player, none at treasure
        SurvivalHookAI ai = holeObject.AddComponent<SurvivalHookAI>();
        ai.hook = hook;
        ai.telegraph = telegraph;
        ai.hookOrigin = holeObject.transform;
        ai.target = player.transform;
        ai.range = hookRange;
        ai.startDelay = growTime + Random.Range(0.2f, 0.8f);
        ai.overshoot = overshoot;
        float pace = Mathf.Lerp(1f, hardestScale, Difficulty);
        ai.telegraphMin = telegraphMin * pace;
        ai.telegraphMax = telegraphMax * pace;
        ai.cooldown = cooldown * pace;
        if (aim == AimMode.Intercept)
        {
            TreasureInterceptAimer aimer = holeObject.AddComponent<TreasureInterceptAimer>();
            aimer.lureRadius = lureRadius;
            aimer.intentAngle = intentAngle;
            aimer.maxReach = hookRange;
            ai.aimer = aimer;
        }
        else if (aim == AimMode.Lead)
        {
            LinearHookAimer aimer = holeObject.AddComponent<LinearHookAimer>();
            aimer.leadAmount = leadAmount;
            aimer.maxLeadTime = maxLeadTime;
            ai.aimer = aimer;
        }

        root.SetActive(true);
        hunters.Add(new Hunter { root = root, hole = holeObject.transform, ai = ai, born = Time.time });
        return true;
    }

    // A spot in the ring around the player, away from the other holes
    bool FindHoleSpot(Vector2 around, out Vector2 spot)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            spot = around + Random.insideUnitCircle.normalized * Random.Range(spawnMinDistance, spawnMaxDistance);
            bool clear = true;
            foreach (Hunter h in hunters)
                if (h.root != null && Vector2.Distance(h.hole.position, spot) < hookSpacing) { clear = false; break; }
            if (clear) return true;
        }
        spot = Vector2.zero;
        return false;
    }

    static float EaseOutBack(float t)
    {
        const float s = 1.7f;
        t -= 1f;
        return 1f + t * t * ((s + 1f) * t + s);
    }

    // ---- Treasure ----

    void UpdateTreasure()
    {
        if (player == null) return;
        Vector2 at = player.transform.position;

        for (int i = loot.Count - 1; i >= 0; i--)
        {
            GameObject item = loot[i];
            if (item == null) { loot.RemoveAt(i); continue; }   // eaten
            if (Vector2.Distance(item.transform.position, at) <= treasureDespawn) continue;
            Treasure t = item.GetComponent<Treasure>();
            if (t != null && t.IsTaken) continue;   // in the frog's mouth
            Destroy(item);
            loot.RemoveAt(i);
        }

        // One per frame at most, so a burst of eating doesn't drop a pile all at once
        if (loot.Count < treasureNearby) SpawnTreasure(treasureMinDistance, treasureMaxDistance);
    }

    void SpawnTreasure(float minDistance, float maxDistance)
    {
        GameObject prefab = PickItem();
        if (prefab == null) return;
        Vector2 around = player != null ? (Vector2)player.transform.position : Vector2.zero;
        Vector2 spot = around + Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistance);
        GameObject go = Instantiate(prefab, spot, Quaternion.identity);
        go.name = prefab.name;
        loot.Add(go);
    }

    GameObject PickItem()
    {
        if (items == null) return null;
        float total = 0f;
        foreach (ItemChance c in items) if (c != null && c.prefab != null) total += c.weight;
        if (total <= 0f) return null;

        float roll = Random.value * total;
        foreach (ItemChance c in items)
        {
            if (c == null || c.prefab == null) continue;
            roll -= c.weight;
            if (roll <= 0f) return c.prefab;
        }
        return null;
    }

    // ---- End of the run ----

    // The gold eaten goes into the wallet (the store spends it), and the best time is kept
    void EndRun()
    {
        runOver = true;
        if (CrossLevelStatus.Active != null) CrossLevelStatus.Active.addWallet(gm.PlayerMoney);
        if (gm.TimeSurvived > bestTime)
        {
            bestTime = gm.TimeSurvived;
            newBest = true;
            PlayerPrefs.SetFloat(BestTimeKey, bestTime);
            PlayerPrefs.Save();
        }
    }

    void LeaveToMap()
    {
        // Leaving mid-run still banks the gold eaten so far
        if (gm != null && !runOver && CrossLevelStatus.Active != null) CrossLevelStatus.Active.addWallet(gm.PlayerMoney);
        runOver = true;
        GameFlow.Instance.ReturnToMap();
    }

    // ---- Look ----

    // Faint grid lines, tiled over the view and moved with the camera, so moving reads on an open map
    void CreateGround()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = x == 0 || y == 0 ? groundLines : Color.clear;
        texture.SetPixels(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      size / groundCell, 0, SpriteMeshType.FullRect);
        var go = new GameObject("Ground");
        ground = go.AddComponent<SpriteRenderer>();
        ground.sprite = sprite;
        if (spriteMaterial != null) ground.sharedMaterial = spriteMaterial;
        ground.drawMode = SpriteDrawMode.Tiled;
        ground.sortingOrder = -100;
        // Enough to cover the widest screen, plus a cell on each side for the snapping
        float height = viewSize * 2f + groundCell * 2f;
        ground.size = new Vector2(height * 3f, height);
    }

    // A dark round hole with a lighter rim, one world unit across
    Sprite CreateHoleSprite()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                Color c = d < 0.72f ? holeColor : holeRim;
                c.a *= Mathf.Clamp01((1f - d) * r);   // soft edge
                pixels[y * size + x] = c;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // ---- HUD ----

    void OnGUI()
    {
        if (gm == null) return;
        ScreenFrame.BeginGUI();
        EnsureStyles();

        var lines = new List<string>
        {
            "Time  " + Clock(gm.TimeSurvived),
            "Gold  $" + gm.PlayerMoney.ToString("N0"),
            "Lives  x" + gm.Lives,
            "Hooks  " + hunters.Count,
        };
        if (player != null && player.SkillHint != "")
        {
            string state = player.SkillActive ? "active"
                         : player.SkillReadyIn > 0f ? player.SkillReadyIn.ToString("0.0") + "s" : "ready";
            lines.Add(player.SkillHint + "  " + state);
        }
        if (player != null && player.UsesDigest && !player.CanEat)
            lines.Add("Digesting  " + player.DigestLeft.ToString("0.0") + "s");
        if (bestTime > 0f) lines.Add("Best  " + Clock(bestTime));
        string hud = string.Join("\n", lines);
        float hudHeight = boxStyle.CalcHeight(new GUIContent(hud), 220f);
        GUI.Box(new Rect(12f, 12f, 220f, hudHeight), hud, boxStyle);

        if (gm.IsPlaying && gm.TimeSurvived < 6f)
        {
            const string hint = "SURVIVE\nEvery hook is hunting you, and they keep growing out of the ground.\n" +
                                "The gold is all yours. WASD move, Space skill, Esc back to the map.";
            float w = Mathf.Min(620f, ScreenFrame.GuiWidth - 40f);
            float h = boxStyle.CalcHeight(new GUIContent(hint), w);
            GUI.Box(new Rect((ScreenFrame.GuiWidth - w) * 0.5f, 16f, w, h), hint, boxStyle);
        }

        if (!gm.IsPlaying) DrawEndPanel();
    }

    void DrawEndPanel()
    {
        float w = 380f;
        float h = 250f;
        Rect panel = new Rect((ScreenFrame.GuiWidth - w) * 0.5f, (ScreenFrame.GuiHeight - h) * 0.5f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUI.Label(new Rect(panel.x, panel.y + 18f, w, 36f), "CAUGHT", titleStyle);
        string detail = "Survived " + Clock(gm.TimeSurvived) + (newBest ? "  (new best!)" : "")
                      + "\nGold eaten $" + gm.PlayerMoney.ToString("N0") + " (added to your wallet)"
                      + "\nBest " + Clock(bestTime);
        GUI.Label(new Rect(panel.x + 16f, panel.y + 62f, w - 32f, 90f), detail, textStyle);

        float bw = (w - 48f) * 0.5f;
        if (GUI.Button(new Rect(panel.x + 16f, panel.y + h - 60f, bw, 40f), "Try again (R)")) gm.Restart();
        if (GUI.Button(new Rect(panel.x + 32f + bw, panel.y + h - 60f, bw, 40f), "Back to map")) LeaveToMap();
    }

    static string Clock(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    void EnsureStyles()
    {
        if (boxStyle != null) return;

        // The HUD's panel color, a little see-through (as LevelLoader's guide box)
        var back = new Texture2D(1, 1);
        back.SetPixel(0, 0, new Color(0.118f, 0.165f, 0.247f, 0.85f));
        back.Apply();
        boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 17,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(14, 14, 10, 10),
        };
        boxStyle.normal.background = back;
        boxStyle.normal.textColor = Color.white;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
        };
        titleStyle.normal.textColor = new Color(1f, 0.4f, 0.35f);

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16, alignment = TextAnchor.UpperCenter, wordWrap = true,
        };
        textStyle.normal.textColor = Color.white;
    }
}
