using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Wires the grid-maze extras after LevelLoader has spawned rocks / treasure / miners:
// one-way doors, the entry gate, and the trigger zones (hints, miner arming, steal / block / knock).
public class MazeRuntime : MonoBehaviour
{
    public readonly Dictionary<string, GameObject> ById = new Dictionary<string, GameObject>();
    public readonly Dictionary<string, MinerAI> Miners = new Dictionary<string, MinerAI>();
    public readonly Dictionary<string, Treasure> TreasureById = new Dictionary<string, Treasure>();

    LevelData level;
    PlayerCharacter player;
    string hint;
    float hintUntil;
    GUIStyle hintStyle;
    readonly List<MazeTriggerZone> zones = new List<MazeTriggerZone>();

    public int EntryCredit => level != null ? level.EntryCredit : 0;
    public float BlockRange => level != null ? level.Param("block_range", 1.5f) : 1.5f;

    public static MazeRuntime Active { get; private set; }

    public void Build(LevelData data, PlayerCharacter pig, List<MinerAI> miners)
    {
        Active = this;
        level = data;
        player = pig;
        ById.Clear();
        Miners.Clear();
        TreasureById.Clear();
        zones.Clear();

        // Match treasure to maze entity ids by nearest spawn (gold/diamond only).
        BindTreasure(data);
        BindMiners(data, miners);
        SpawnOneWays(data);
        SpawnLevelPortals(data);
        SpawnTriggers(data);
        // Hub restore is deferred to LevelLoader.Start — GameManager.Awake must run first.
    }

    public void RestoreHubIfNeeded()
    {
        MazeHubState.ReadPrefsIfNeeded();
        if (!MazeHubState.HasSave) return;
        MazeHubState.ApplyToScene(this, player, GameManager.Instance);
    }

    // Any maze gold/diamond that is missing or destroyed counts as eaten for the hub save.
    public void SyncMissingTreasureAsEaten()
    {
        if (level == null) return;
        var missing = new List<string>();
        foreach (LevelData.MazeEntity e in level.mazeEntities)
        {
            if (e.type != "Gold" && e.type != "Diamond") continue;
            if (string.IsNullOrEmpty(e.id)) continue;
            if (!TreasureById.TryGetValue(e.id, out Treasure t) || t == null
                || !t.gameObject.activeInHierarchy)
                missing.Add(e.id);
        }
        MazeHubState.MarkTreasuresEaten(missing);
    }

    // Destroy + unbind every treasure id recorded as eaten (after a fresh BindTreasure spawn).
    public void RemoveEatenTreasures()
    {
        var eaten = new List<string>(MazeHubState.EatenTreasureIds);
        int removed = 0;
        foreach (string id in eaten)
        {
            if (!TreasureById.TryGetValue(id, out Treasure t) || t == null)
            {
                TreasureById.Remove(id);
                ById.Remove(id);
                continue;
            }
            t.gameObject.SetActive(false);
            Destroy(t.gameObject);
            TreasureById.Remove(id);
            ById.Remove(id);
            removed++;
        }
        Debug.Log("MazeRuntime: removed " + removed + " eaten treasure(s); tracked="
                  + MazeHubState.EatenTreasureIds.Count);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        HUDLayout.HideTopBar = false;
    }

    void BindTreasure(LevelData data)
    {
        var loose = new List<Treasure>(FindObjectsByType<Treasure>(FindObjectsSortMode.None));
        foreach (LevelData.MazeEntity e in data.mazeEntities)
        {
            if (e.type != "Gold" && e.type != "Diamond") continue;
            Treasure best = null;
            float bestDist = 0.35f;
            foreach (Treasure t in loose)
            {
                if (t == null) continue;
                float d = Vector2.Distance(t.transform.position, e.worldPos);
                if (d >= bestDist) continue;
                bestDist = d;
                best = t;
            }
            if (best == null) continue;
            best.name = e.id;
            ById[e.id] = best.gameObject;
            TreasureById[e.id] = best;
            loose.Remove(best);
        }
    }

    void BindMiners(LevelData data, List<MinerAI> miners)
    {
        var minerEntities = new List<LevelData.MazeEntity>();
        foreach (LevelData.MazeEntity e in data.mazeEntities)
            if (e.type == "Miner") minerEntities.Add(e);

        for (int i = 0; i < minerEntities.Count && i < miners.Count; i++)
        {
            LevelData.MazeEntity e = minerEntities[i];
            MinerAI miner = miners[i];
            miner.name = e.id;
            ById[e.id] = miner.gameObject;
            Miners[e.id] = miner;
            miner.autoShoot = e.autoFire;
            bool red = e.hook == "Red";
            miner.shootsPlayer = red;
            miner.hook.hitsPig = red;
            if (e.hookSpeed > 0f)
            {
                miner.hook.extendSpeed = e.hookSpeed;
                // M's retract must be slow enough to Space-block the stolen gold.
                float retractMul = (e.id == "M") ? 0.4f : 1.2f;
                miner.hook.retractSpeed = e.hookSpeed * retractMul;
                miner.cooldown = 0.05f;
            }
            // windup 0 must apply — previously `> 0` left the battle default (~1s) telegraph.
            miner.telegraphMin = e.windup;
            miner.telegraphMax = Mathf.Max(0.01f, e.windup);
            // Scripted miners wait in Loop until Arm(true) flips autoShoot.
            if (!e.autoFire)
            {
                miner.startDelay = 0f;
                miner.autoShoot = false;
            }
        }
    }

    void SpawnOneWays(LevelData data)
    {
        foreach (LevelData.MazeEntity e in data.mazeEntities)
        {
            if (e.type != "OneWay") continue;
            var go = new GameObject(string.IsNullOrEmpty(e.id) ? "OneWay" : e.id);
            go.transform.SetParent(transform, false);
            go.transform.position = e.worldPos;
            go.AddComponent<OneWayDoor>().Init(e.dir, data.TileSize);
            ById[e.id] = go;
        }
    }

    // Gate / LevelPortal entities → world-map level markers (stone until revealed, then polygon).
    void SpawnLevelPortals(LevelData data)
    {
        foreach (LevelData.MazeEntity e in data.mazeEntities)
        {
            if (e.type != "Gate" && e.type != "LevelPortal") continue;
            string nodeId = !string.IsNullOrEmpty(e.target) ? e.target : "battle_01";
            var go = new GameObject(string.IsNullOrEmpty(e.id) ? nodeId : e.id);
            go.transform.SetParent(transform, false);
            go.transform.position = e.worldPos;

            int cost = e.unlockCost;
            if (cost <= 0 && GameFlow.Instance != null && GameFlow.Instance.Map != null)
            {
                MapNode node = GameFlow.Instance.Map.Find(nodeId);
                if (node != null) cost = node.unlockCost;
            }
            // Defaults when the WorldMap asset has not been retuned yet.
            // Level 1 maze entry credit is $900; Level 2 map-money unlock is higher.
            if (cost <= 0 && nodeId == "battle_02") cost = 1500;

            // Level 1 always uses the maze entry credit ($900). Later levels use MapMoney.
            int mazeCredit = 0;
            if (nodeId == "battle_01")
                mazeCredit = data.EntryCredit > 0 ? data.EntryCredit : 900;
            string prereq = e.requires;
            if (string.IsNullOrEmpty(prereq) && nodeId == "battle_02") prereq = "battle_01";

            var portal = go.AddComponent<LevelPortal>();
            portal.Init(nodeId, cost, mazeCredit, prereq, data.TileSize, this);
            portal.lockedHint = nodeId == "battle_01"
                ? "Level 1 needs $900"
                : "Need more money to unlock";
            ById[e.id] = go;
        }
    }

    void SpawnTriggers(LevelData data)
    {
        foreach (LevelData.MazeTrigger t in data.mazeTriggers)
        {
            var go = new GameObject(string.IsNullOrEmpty(t.id) ? "Trigger" : t.id);
            go.transform.SetParent(transform, false);
            go.transform.position = t.worldCenter;
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = t.worldSize;
            var zone = go.AddComponent<MazeTriggerZone>();
            zone.Init(t, this);
            zones.Add(zone);
        }
    }

    public void ShowHint(string text)
    {
        hint = text;
        hintUntil = Time.unscaledTime + 3.5f;
    }

    public void ClearHint()
    {
        hint = null;
    }

    public GameObject Target(string id)
    {
        return ById.TryGetValue(id, out GameObject go) ? go : null;
    }

    public MinerAI Miner(string id)
    {
        return Miners.TryGetValue(id, out MinerAI m) ? m : null;
    }

    public Treasure Treasure(string id)
    {
        return TreasureById.TryGetValue(id, out Treasure t) ? t : null;
    }

    public void ArmMiners(string[] ids, bool on)
    {
        if (ids == null) return;
        foreach (string id in ids)
        {
            MinerAI m = Miner(id);
            if (m == null) continue;
            m.Arm(on);
        }
    }

    public void NotifyEatStart(Treasure treasure)
    {
        string id = ResolveTreasureId(treasure);
        if (!string.IsNullOrEmpty(id))
        {
            MazeHubState.MarkTreasureEaten(id);
            TreasureById.Remove(id);
            ById.Remove(id);
        }
        foreach (MazeTriggerZone zone in zones) zone.OnEatStart(treasure);
    }

    public void NotifyEatProgress(string eatenId, float progress01)
    {
        foreach (MazeTriggerZone zone in zones) zone.OnEatProgress(eatenId, progress01);
    }

    string ResolveTreasureId(Treasure treasure)
    {
        if (treasure == null) return null;
        foreach (var kv in TreasureById)
            if (kv.Value == treasure) return kv.Key;
        // BindTreasure renames the instance to the maze entity id (G1, D2, …).
        return treasure.name;
    }

    public void RespawnTreasure(string id)
    {
        // Permanently collected on the world map — knock/block lessons must not bring it back.
        if (MazeHubState.TreasureWasEaten(id)) return;

        LevelData.MazeEntity ent = null;
        foreach (LevelData.MazeEntity e in level.mazeEntities)
            if (e.id == id) { ent = e; break; }
        if (ent == null) return;

        if (TreasureById.TryGetValue(id, out Treasure old) && old != null)
        {
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
        }

        string prefabName = ent.type == "Diamond" ? "Diamond" : "Gold_Medium";
        LevelLoader loader = FindFirstObjectByType<LevelLoader>();
        if (loader == null) return;
        GameObject prefab = null;
        foreach (GameObject p in loader.itemPrefabs)
            if (p != null && p.name == prefabName) { prefab = p; break; }
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, ent.worldPos, Quaternion.identity);
        go.name = id;
        float tile = level.TileSize;
        go.transform.localScale = Vector3.one * (ent.type == "Diamond" ? tile * 0.75f : tile * 0.85f);
        Treasure t = go.GetComponent<Treasure>();
        if (t == null) return;
        ById[id] = go;
        TreasureById[id] = t;
    }

    void OnGUI()
    {
        DrawResetProgressButton();

        if (string.IsNullOrEmpty(hint) || Time.unscaledTime > hintUntil) return;
        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            hintStyle.normal.textColor = Color.white;
        }
        float w = Mathf.Min(520f, Screen.width - 40f);
        GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.18f, w, 64f), hint, hintStyle);
    }

    void DrawResetProgressButton()
    {
        const float bw = 140f;
        const float bh = 32f;
        Rect r = new Rect(Screen.width - bw - 16f, Screen.height - bh - 16f, bw, bh);
        if (!GUI.Button(r, "Reset progress")) return;

        ProgressStore.ResetAll();
        if (GameFlow.Instance != null) GameFlow.Instance.OpenMap();
        else UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}
