using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

    public const string MusicStoreId = "music_store";
    [Tooltip("The green rock that becomes the Music Store portal (world position; the nearest green rock " +
             "within half a tile is used). Default: the lower tooth just right of Level 2")]
    public Vector2 musicStoreAt = MusicStoreDefaultAt;
    public static readonly Vector2 MusicStoreDefaultAt = new Vector2(0.525f, -2.94f);
    float hintUntil;
    GUIStyle hintStyle;

    void Awake() { Active = this; }
    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (focusCircle != null) Destroy(focusCircle);
        if (focusTexture != null) Destroy(focusTexture);
        if (focusCoverSprite != null) Destroy(focusCoverSprite);
    }

    // Share one cover and one hole per newly revealed portal, so simultaneous unlocks all get light.
    const int FocusOrder = 90;
    Transform focusRoot;
    SpriteRenderer focusCover;
    Sprite focusCircle;
    Texture2D focusTexture;
    Sprite focusCoverSprite;
    float focusStartedAt;
    float focusUntil;

    public void FocusPortal(HubPortal portal)
    {
        if (focusRoot == null)
        {
            focusRoot = new GameObject("New portal focus").transform;
            focusRoot.SetParent(transform, false);
            var cover = new GameObject("Dark cover");
            cover.transform.SetParent(focusRoot, false);
            cover.transform.localScale = Vector3.one * 1000f;
            focusCover = cover.AddComponent<SpriteRenderer>();
            focusCoverSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            focusCover.sprite = focusCoverSprite;
            focusCover.sortingOrder = FocusOrder;
            focusCover.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

            const int size = 128;
            focusTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            focusTexture.filterMode = FilterMode.Bilinear;
            focusTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * size * 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(size * 0.5f - distance));
                }
            focusTexture.SetPixels(pixels);
            focusTexture.Apply();
            focusCircle = Sprite.Create(focusTexture, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
            focusStartedAt = Time.unscaledTime;
        }

        var light = new GameObject("Light " + portal.nodeId);
        light.transform.SetParent(focusRoot, false);
        light.transform.position = portal.transform.position;
        light.transform.localScale = Vector3.one * tileSize * 4f;
        SpriteMask mask = light.AddComponent<SpriteMask>();
        mask.sprite = focusCircle;
        TutorialStep.LimitLightMask(mask, focusCover.sortingLayerID, FocusOrder);
        focusUntil = Time.unscaledTime + 3.5f;
    }

    void LateUpdate()
    {
        if (focusRoot == null) return;
        float now = Time.unscaledTime;
        // Match dialogue advance: a fresh key press or left click, after its input grace period.
        bool pressed = (!DeveloperConsole.CapturesInput && Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (!DeveloperConsole.CapturesInput && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (pressed && now - focusStartedAt > 0.15f) focusUntil = now;
        float fade = Mathf.Min(Mathf.Clamp01((now - focusStartedAt) / 0.3f),
                               Mathf.Clamp01((focusUntil - now) / 0.6f));
        focusCover.color = new Color(0f, 0f, 0f, 0.7f * fade);
        if (now < focusUntil) return;
        Destroy(focusRoot.gameObject);
        focusRoot = null;
        Destroy(focusCircle);
        Destroy(focusTexture);
        Destroy(focusCoverSprite);
    }

    // After Build, and after the scene's TutorialSteps are listening: the map's story events
    void Start()
    {
        TutorialEvents.Fire(TutorialEvents.MapOpen);
        QuitChrome.Create(QuitChrome.MapOffset(), () =>
        {
            Time.timeScale = 1f;
            GameFlow.Instance.OpenMenu();
        });
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
        if (baked != null && !all.Exists(p => p.nodeId == MusicStoreId))
        {
            HubPortal music = PortalFromGreenRock(baked, MusicStoreId, musicStoreAt, tile);
            if (music != null) all.Add(music);
        }
        else
            foreach (LevelData.Portal p in level.portals)
            {
                var go = new GameObject("Portal_" + p.target);
                go.transform.SetParent(root, false);
                go.transform.position = p.position;
                HubPortal portal = go.AddComponent<HubPortal>();
                portal.nodeId = p.target;
                portal.hidden = p.hidden;
                portal.labelAbove = p.labelAbove;
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
        MapRockDamage.Bind(this, baked);
    }

    // A node without a portal in the baked map takes the place of one of its green rocks: the rock is
    // switched off and a hidden portal (a green rock itself until the node unlocks) stands in its spot.
    // Used for the Music Store, so the world map scene needs no edit.
    public static HubPortal PortalFromGreenRock(Transform baked, string nodeId, Vector2 near, float tile)
    {
        Rock best = null;
        float bestDistance = tile * 0.5f;
        foreach (Rock rock in baked.GetComponentsInChildren<Rock>())
        {
            if (rock.GetComponentInParent<HubPortal>() != null || !IsGreen(rock)) continue;
            float distance = Vector2.Distance(rock.transform.position, near);
            if (distance <= bestDistance) { best = rock; bestDistance = distance; }
        }
        if (best == null)
        {
            Debug.LogWarning("MapHub: no green rock near " + near + " for the '" + nodeId + "' portal");
            return null;
        }

        Transform portals = baked.Find("Portals");
        var go = new GameObject("Portal_" + nodeId);
        go.transform.SetParent(portals != null ? portals : baked, false);
        go.transform.position = best.transform.position;
        go.layer = best.gameObject.layer;
        HubPortal portal = go.AddComponent<HubPortal>();
        portal.nodeId = nodeId;
        portal.hidden = true;   // Init draws it like the other portals: a green stone until it opens
        best.gameObject.SetActive(false);
        return portal;
    }

    static bool IsGreen(Rock rock)
    {
        if (rock.hookPasses) return true;
        SpriteRenderer sprite = rock.GetComponent<SpriteRenderer>();
        if (sprite == null) return false;
        Color c = sprite.color;
        return Mathf.Abs(c.r - GreenRock.r) < 0.05f && Mathf.Abs(c.g - GreenRock.g) < 0.05f && Mathf.Abs(c.b - GreenRock.b) < 0.05f;
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
    public const string HpKey = "gm.map.hp";   // core HP left on the map; none = full
    int fullLives;                              // full core HP this visit

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
        MapRockDamage.ResetDamage();
        RefillHp();
        if (gm != null)
        {
            gm.SetBonusLives(0);
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
        // The music store opens right here on the map; the player stays on its portal
        if (portal.Node.type == NodeType.MusicStore) { MusicStorePanel.Show(portal.Node, transform); return; }
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
        ScreenFrame.BeginGUI();
        if (hint == "" || Time.time > hintUntil) return;
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
