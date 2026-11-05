using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The world map as a playable place (after Penny's maze hub on feature/penny-level-map): the hub
// level (a "grid" level file, see LevelJson) is loaded into the Game scene like any level, and this
// adds what only a hub has: one-way tiles, a portal per map node, and the hint line. Walking into
// an open portal enters that node through GameFlow, and coming back puts the player on it again.
public class MapHub : MonoBehaviour
{
    [Tooltip("Seconds a hint stays on screen")]
    public float hintTime = 2.5f;

    // The portal the player last went through, so the hub puts them back there. Kept for the
    // session only: a fresh launch starts at the grid's P.
    static string lastPortal;

    public static MapHub Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // Play without a domain reload keeps statics
    {
        lastPortal = null;
        Active = null;
    }

    // The object under the world map scene's Level root that holds the baked walls, portals, doors
    // and labels (HubSceneBaker)
    public const string BakedRootName = "Hub";

    // The mode areas' rocks, and the hidden portals among them: R attack, Y survival, G compete
    public static readonly Color RedRock    = new Color(0.72f, 0.20f, 0.18f);
    public static readonly Color YellowRock = new Color(0.86f, 0.70f, 0.18f);
    public static readonly Color GreenRock  = new Color(0.25f, 0.62f, 0.30f);

    public static Color WallColor(char kind, Color plain)
    {
        switch (kind)
        {
            case 'R': return RedRock;
            case 'Y': return YellowRock;
            case 'G': return GreenRock;
            default: return plain;
        }
    }

    readonly List<HubPortal> portals = new List<HubPortal>();
    PlayerCharacter walker;
    float tileSize;
    string hint = "";

    [Tooltip("Where a player caught by a miner's hook on the map comes back: in front of this node's portal")]
    public string reviveAt = "role_pick";
    float hintUntil;
    GUIStyle hintStyle;

    void Awake() { Active = this; }
    void OnDestroy() { if (Active == this) Active = null; }

    // After Build, and after the scene's TutorialSteps are listening: the map's story events
    void Start()
    {
        TutorialEvents.Fire(TutorialEvents.MapOpen);
        if (lastPortal == null) return;
        TutorialEvents.Fire(TutorialEvents.MapBackFrom(lastPortal));
        if (ProgressStore.IsCompleted(lastPortal)) TutorialEvents.Fire(TutorialEvents.MapCleared(lastPortal));
    }

    // baked: the world map scene's "Hub" object (HubSceneBaker), whose walls, portals, doors and
    // labels are edited in the Scene view. Without it everything is built from the grid.
    public void Build(LevelData level, PlayerCharacter player, Sprite hexSprite, Color wallColor, Transform baked)
    {
        float tile = level.TileSize;
        walker = player;
        tileSize = tile;
        Transform root = baked != null ? baked : new GameObject("Hub").transform;

        // Portals: the scene's, else one per grid portal
        var all = new List<HubPortal>();
        if (baked != null) all.AddRange(baked.GetComponentsInChildren<HubPortal>());
        else
            foreach (LevelData.Portal p in level.portals)
            {
                var go = new GameObject("Portal_" + p.target);
                go.transform.SetParent(root, false);
                go.transform.position = p.position;
                HubPortal portal = go.AddComponent<HubPortal>();
                portal.nodeId = p.target;
                portal.hidden = p.hidden;
                portal.stoneColor = WallColor(p.stone, wallColor);
                all.Add(portal);
            }

        // One-way tiles: the scene's keep their Allowed Dir
        if (baked != null)
            foreach (OneWayDoor door in baked.GetComponentsInChildren<OneWayDoor>()) door.Init(door.allowedDir, tile);
        else
            foreach (LevelData.OneWay one in level.oneWays)
            {
                var go = new GameObject("OneWay");
                go.transform.SetParent(root, false);
                go.transform.position = one.position;
                go.AddComponent<OneWayDoor>().Init(one.dir, tile);
            }

        if (baked == null)
            foreach (LevelData.Label l in level.labels) AddLabel(root, l.text, l.position, tile);

        // Back from a node: stand on its portal (it waits until the player steps off)
        Vector2 playerAt = level.pigStart;
        HubPortal cameFrom = all.Find(p => p.nodeId == lastPortal);
        if (cameFrom != null) playerAt = cameFrom.transform.position;
        if (player != null)
        {
            player.transform.position = playerAt;
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null) body.position = playerAt;
        }

        WorldMap map = GameFlow.Instance.Map;
        foreach (HubPortal portal in all)
        {
            MapNode node = map != null ? map.Find(portal.nodeId) : null;
            if (node == null)
            {
                Debug.LogWarning("MapHub: portal '" + portal.name + "' enters '" + portal.nodeId + "', which is not a node in the WorldMap", portal);
                portal.gameObject.SetActive(false);
                continue;
            }
            portal.Init(node, tile, hexSprite, this, playerAt);
            portals.Add(portal);
        }

        if (map != null)
            foreach (MapNode node in map.nodes)
                if (node != null && !portals.Exists(p => p.Node == node))
                    Debug.LogWarning("MapHub: WorldMap node '" + node.id + "' has no portal in the hub level");

        if (baked != null) HideRetiredModes(baked);
        if (baked != null) LetHooksThroughGreen(baked);
    }

    // Green rocks (levels not disclosed yet) are no obstacle to a hook. The bake marks them already;
    // this also covers a map baked before Rock.hookPasses existed (found by their green tint)
    static void LetHooksThroughGreen(Transform baked)
    {
        foreach (Rock rock in baked.GetComponentsInChildren<Rock>(true))
        {
            SpriteRenderer sprite = rock.GetComponent<SpriteRenderer>();
            if (sprite == null) continue;
            Color c = sprite.color;
            if (Mathf.Abs(c.r - GreenRock.r) < 0.05f && Mathf.Abs(c.g - GreenRock.g) < 0.05f && Mathf.Abs(c.b - GreenRock.b) < 0.05f)
                rock.hookPasses = true;
        }
    }

    // Attack and survival are out of this build. Their walls and floor labels stay in the baked
    // map until it is rebaked; hide them so only the green compete area is left to walk.
    static void HideRetiredModes(Transform baked)
    {
        foreach (SpriteRenderer sprite in baked.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite.GetComponentInParent<HubPortal>() != null) continue;
            Color c = sprite.color;
            bool red = c.r > 0.6f && c.g < 0.35f && c.b < 0.35f;
            bool yellow = c.r > 0.7f && c.g > 0.55f && c.g < 0.9f && c.b < 0.35f;
            if (red || yellow) sprite.gameObject.SetActive(false);
        }
        foreach (TMPro.TextMeshPro text in baked.GetComponentsInChildren<TMPro.TextMeshPro>(true))
        {
            if (text.text == "ATTACK" || text.text == "SURVIVAL")
                text.gameObject.SetActive(false);
        }
    }

    // Words on the floor (an area's name), sized to the tiles
    public static GameObject AddLabel(Transform root, string words, Vector2 position, float tile)
    {
        var go = new GameObject("Label " + words);
        go.transform.SetParent(root, false);
        go.transform.position = position;
        go.transform.localScale = Vector3.one * tile;
        var text = go.AddComponent<TMPro.TextMeshPro>();
        text.text = words;
        text.fontSize = 7f;
        text.fontStyle = TMPro.FontStyles.Bold;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(12f, 2f);
        text.color = new Color(1f, 1f, 1f, 0.55f);
        text.sortingOrder = 1;
        return go;
    }

    // --- HP on the map ----------------------------------------------------------------------------
    // A miner's hook costs 1 HP (GameManager.PlayerHit). The map's HP is kept between visits and only
    // refills when a level is won (RefillHp, from GameFlow) or when the player revives here.
    public const string HpKey = "gm.map.hp";   // core HP left on the map (no Brace pip); none = full
    int fullLives;                              // full HP this visit, with any Brace pip

    // LevelLoader.Start, after the hub's lives are set up: carry over the HP left from last time
    public void ApplySavedHp(GameManager gm)
    {
        if (gm == null) return;
        fullLives = gm.startingLives;
        if (!PlayerPrefs.HasKey(HpKey)) return;
        int fullCore = Mathf.Max(1, gm.startingLives - gm.BonusLives);
        int core = Mathf.Clamp(PlayerPrefs.GetInt(HpKey, fullCore), 1, fullCore);
        gm.SetLives(core + gm.BonusLives);
    }

    public void SaveHp(GameManager gm)
    {
        if (gm == null) return;
        PlayerPrefs.SetInt(HpKey, Mathf.Max(1, gm.Lives - gm.BonusLives));
        PlayerPrefs.Save();
    }

    // A won level: the map's HP is full again next time
    public static void RefillHp()
    {
        PlayerPrefs.DeleteKey(HpKey);
        PlayerPrefs.Save();
    }

    // The hook took the last HP: the player revives on the floor just above the Pick Character
    // portal with full HP. Without that portal they revive where they are
    public void Revive(GameManager gm)
    {
        RefillHp();
        if (gm != null)
        {
            gm.SetBonusLives(LevelLoader.PendingBraceBonusLives());
            gm.SetLives(fullLives > 0 ? fullLives : gm.startingLives);
        }
        HubPortal at = portals.Find(p => p != null && p.nodeId == reviveAt);
        if (at == null || walker == null) return;
        Vector2 spot = (Vector2)at.transform.position + Vector2.up * tileSize;
        walker.transform.position = spot;
        var body = walker.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = spot; body.linearVelocity = Vector2.zero; }
        ShowHint("Caught! Back to " + (at.Node != null ? at.Node.displayName : reviveAt) + " with full HP");
    }

    public void Enter(HubPortal portal)
    {
        lastPortal = portal.Node.id;
        GameFlow.Instance.EnterNode(portal.Node);
    }

    public void ShowHint(string text)
    {
        hint = text ?? "";
        hintUntil = Time.time + hintTime;
    }

    void OnGUI()
    {
        if (hint == "" || Time.time > hintUntil) return;
        ScreenFrame.BeginGUI();
        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = Color.white;
        }
        float arenaWidth = ScreenFrame.GuiWidth * (1f - HUDLayout.SidebarWidth);
        Vector2 size = hintStyle.CalcSize(new GUIContent(hint)) + new Vector2(32f, 14f);
        GUI.Box(new Rect((arenaWidth - size.x) * 0.5f, ScreenFrame.GuiHeight - size.y - 24f, size.x, size.y), hint, hintStyle);
    }
}
