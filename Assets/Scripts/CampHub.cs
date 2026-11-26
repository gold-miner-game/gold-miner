using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Owner: art lead (camp). The character camp: the world map's Pick Character node, replacing the old
// Select ("WHO ARE U") screen. A "grid" level with "camp": 1 (Resources/Hub/camp.json) is loaded like
// the world map (a maze level: no clock, no hooks, no run record, you walk as your picked character),
// and LevelLoader adds this instead of MapHub. It lays out a lamplit clearing round a campfire where
// the six heroes camp at their tents, and gives the place its functions:
//   - walk up to a hero: the talk card asks if you'll go on an adventure together
//   - [E] talks (a few lines each, in character), whether or not the pop-out is open
//   - the first [Enter] (or the card's button): their recorded skill clip pops out beside the scene
//     (just the clip; every key hint stays on the talk card)
//   - the second [Enter] (or the card's button): unlocked -> play as them (the camp re-opens with
//     you in their place); for sale (Elsa, Hulk) -> hire them with the wallet
//     (CharacterUnlocks.TryPurchase), then a further [Enter] plays as them. Two presses, so a stray
//     key never spends the wallet. Walking away closes the pop-out (Esc stays the pause key)
//   - not released (Scrat, Rero Rero): no pop-out; they're chatty, but "not ready to join yet"
//   - the gate leads back to the map and completes the node, as picking did on the old screen
// Picking keeps the old screen's rules: ChampionSpawner.Chosen, a fresh record that keeps store goods,
// the save, analytics and the tutorial's RolePickOpen / CharacterPicked / Picked(i) events.
public class CampHub : MonoBehaviour
{
    public static CampHub Active { get; private set; }
    public const string CampLevelPath = "Hub/camp";

    static readonly string[] Names = { "Mr. Hyde", "JK. Rolling", "Scrat", "Rero Rero", "Elsa", "Hulk" };
    static readonly Color[] Colors =
    {
        new Color(0.95f, 0.55f, 0.65f), new Color(0.42f, 0.36f, 0.5f), new Color(0.68f, 0.45f, 0.24f),
        new Color(0.36f, 0.68f, 0.32f), new Color(0.48f, 0.74f, 0.98f), new Color(0.8f, 0.28f, 0.22f),
    };
    enum State { Current, Ready, ForSale, NotReady }

    class Spot
    {
        public int index;
        public Vector2 at;
        public Transform npc;
        public Vector3 npcScale;
        public readonly List<SpriteRenderer> parts = new List<SpriteRenderer>();
        public readonly List<Color> colors = new List<Color>();
        public TextMeshPro name, status;
        public int line = -1;
        public State state;
        public string home;
        public Vector2 homeAnchor = new Vector2(0.5f, 0.3f);
        public float homeSize = 2.7f;
        public Vector3? homePosition;
        public Vector3? homeScale;
        public bool groupHome;
        public Vector3? homeGroupPosition;
        public Vector3? homeGroupScale;
        public Vector3? npcLocalPosition;
        public Vector3? npcLocalRotation;
        public Vector3? npcPivotPosition;
        public float? npcFacingX;
    }

    readonly List<Spot> spots = new List<Spot>();
    PlayerCharacter player;
    TextAsset source;
    Transform root;
    Vector2 gateAt;
    bool gateArmed;
    Spot near;
    Spot offer;   // the hero whose pop-out (skill clip + play/hire) is open
    string said = "", note = "";
    float saidUntil, noteUntil, openedAt;
    bool leaving;
    SpriteRenderer fire;
    Sprite[] fireFrames;
    GUIStyle titleStyle, bodyStyle, hintStyle;

    // The hero the player switched to: the camp re-opens with them standing at their own tent
    static int arriveAt = -1;
    static float fadeIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; arriveAt = -1; fadeIn = 0f; }

    public static TextAsset CampLevel => Resources.Load<TextAsset>(CampLevelPath);

    void Awake() { Active = this; }
    void OnDestroy() { if (Active == this) Active = null; CharacterSkillDemo.Hide(this); }

    static int Current => ChampionSpawner.Chosen >= 0 ? ChampionSpawner.Chosen : ChampionSpawner.MrHyde;

    // Called by LevelLoader.SetUpHub for a "camp" level, after the walls and the player are in place
    public void Build(LevelData level, TextAsset levelFile, PlayerCharacter walker)
    {
        source = levelFile;
        player = walker;
        openedAt = Time.unscaledTime;
        root = new GameObject("Camp").transform;
        var data = source != null ? LevelJson.Parse(source.text) as Dictionary<string, object> : null;

        // Keep the normal wall flags so Hulk can smash camp rocks. LevelLoader rebuilds
        // them on every entry; camp rocks do not use the world map's saved damage.

        Vector2 fireAt = Cell(level, data, "camp_fire", new Vector2(10, 5));
        gateAt = Cell(level, data, "camp_gate", new Vector2(1, 5));
        BuildFire(fireAt);
        BuildGate(gateAt);
        if (data != null && data.TryGetValue("camp_props", out object props) && props is List<object> propList)
            foreach (object o in propList)
                if (o is Dictionary<string, object> p && p.TryGetValue("art", out object art) && art is string artName)
                    Prop(artName, GridAt(level, p), p.TryGetValue("size", out object s) ? ToFloat(s) : 0.8f);

        // Heroes keep their sizes relative to each other, as in battles (each prefab's own root scale),
        // with Mr. Hyde at the camp's player size; the walker follows the same rule
        float scale = level.TileSize * level.Param("player_scale", 0.9f) / HydeScale();
        if (player != null) player.transform.localScale = PrefabScale(Current) * scale;
        if (data != null && data.TryGetValue("camp_spots", out object list) && list is List<object> spotList)
            foreach (object o in spotList)
            {
                if (!(o is Dictionary<string, object> d) || !d.TryGetValue("character", out object c)) continue;
                var spot = new Spot { index = Mathf.RoundToInt(ToFloat(c)), at = GridAt(level, d),
                                      home = d.TryGetValue("home", out object h) ? h as string : null };
                if (d.TryGetValue("home_anchor", out object ha) && ha is List<object> al && al.Count == 2)
                    spot.homeAnchor = new Vector2(ToFloat(al[0]), ToFloat(al[1]));
                if (d.TryGetValue("home_size", out object hs)) spot.homeSize = ToFloat(hs);
                if (d.TryGetValue("home_position", out object hp) && hp is List<object> homePos && homePos.Count == 3)
                    spot.homePosition = new Vector3(ToFloat(homePos[0]), ToFloat(homePos[1]), ToFloat(homePos[2]));
                if (d.TryGetValue("home_scale", out object hscale) && hscale is List<object> homeScale && homeScale.Count == 3)
                    spot.homeScale = new Vector3(ToFloat(homeScale[0]), ToFloat(homeScale[1]), ToFloat(homeScale[2]));
                spot.groupHome = d.TryGetValue("group_home", out object gh) && gh is bool groupHome && groupHome;
                if (d.TryGetValue("home_group_position", out object hgp) && hgp is List<object> groupPos && groupPos.Count == 3)
                    spot.homeGroupPosition = new Vector3(ToFloat(groupPos[0]), ToFloat(groupPos[1]), ToFloat(groupPos[2]));
                if (d.TryGetValue("home_group_scale", out object hgs) && hgs is List<object> groupScale && groupScale.Count == 3)
                    spot.homeGroupScale = new Vector3(ToFloat(groupScale[0]), ToFloat(groupScale[1]), ToFloat(groupScale[2]));
                if (d.TryGetValue("npc_local_position", out object np) && np is List<object> pos && pos.Count == 3)
                    spot.npcLocalPosition = new Vector3(ToFloat(pos[0]), ToFloat(pos[1]), ToFloat(pos[2]));
                if (d.TryGetValue("npc_local_rotation", out object nr) && nr is List<object> rot && rot.Count == 3)
                    spot.npcLocalRotation = new Vector3(ToFloat(rot[0]), ToFloat(rot[1]), ToFloat(rot[2]));
                if (d.TryGetValue("npc_pivot_position", out object pp) && pp is List<object> pivotPos && pivotPos.Count == 3)
                    spot.npcPivotPosition = new Vector3(ToFloat(pivotPos[0]), ToFloat(pivotPos[1]), ToFloat(pivotPos[2]));
                if (d.TryGetValue("npc_facing_x", out object facing)) spot.npcFacingX = ToFloat(facing) < 0f ? -1f : 1f;
                if (spot.index < 0 || spot.index >= CharacterUnlocks.Count) continue;
                BuildSpot(spot, fireAt, scale);
                spots.Add(spot);
            }

        // A cave bigger than the screen: one screen in view, following the walker inside it (Level 4's camera)
        float view = data != null && data.TryGetValue("camp_view", out object v) ? ToFloat(v) : 1f;
        GameplayCamera.SetArenaSize(new Vector2(17.78f, 10f) * Mathf.Max(0.5f, view));
        Vector2 size = level.MazeSize;
        if (player != null) GameplayCamera.Follow(player.transform, new Rect(-size * 0.5f, size));

        // Arrive by the gate, or at your own home after switching heroes
        Vector2 arrive = level.pigStart;
        Spot mine = spots.Find(s => s.index == arriveAt);
        if (mine != null) arrive = mine.at + (fireAt - mine.at).normalized * 1.1f;
        arriveAt = -1;
        Place(player, arrive);
        gateArmed = Vector2.Distance(arrive, gateAt) > 1.4f;

        TutorialEvents.Fire(TutorialEvents.RolePickOpen);
    }

    // --- Building ------------------------------------------------------------------------------------

    void BuildFire(Vector2 at)
    {
        var frames = new List<Sprite>();
        for (int i = 1; i <= 3; i++) { Sprite s = ArtLibrary.Get("Camp/Campfire_" + i); if (s != null) frames.Add(s); }
        fireFrames = frames.ToArray();
        if (fireFrames.Length == 0) return;
        fire = Piece("Campfire", fireFrames[0], at, 1.9f, 1);
        Shadow(at, 1.6f);
        PaperLamps.Lantern(fire.transform);   // a warm pool round the fire
    }

    void BuildGate(Vector2 at)
    {
        // The way back down to the mine map: the world map's own timber mine entrance (Landmarks/Mine),
        // set into the camp's west wall; the plain exit hatch if that art is missing
        Sprite gate = ArtLibrary.Get("Landmarks/Mine");
        if (gate != null) Piece("Camp Gate", gate, at + new Vector2(-0.15f, 0.25f), 1.9f, -30);
        else
        {
            Sprite hatch = ArtLibrary.Get("Objects/ExitHatch");
            if (hatch != null) Piece("Camp Gate", hatch, at, 1.1f, -30);
        }
        Label("Label Back to the Map", "Back to the Map", at + new Vector2(0.4f, 0.95f), 2.6f, Color.white);
    }

    void Prop(string art, Vector2 at, float size)
    {
        Sprite s = ArtLibrary.Get(art);
        if (s == null) return;
        SpriteRenderer sr = Piece(art.Substring(art.LastIndexOf('/') + 1), s, at, size, -20);
        if (art.EndsWith("Lantern")) PaperLamps.Lantern(sr.transform);
    }

    void BuildSpot(Spot spot, Vector2 fireAt, float scale)
    {
        // Each hero lives IN their home (Camp/Home_*): the painting is placed so its home_anchor (the
        // mud basin, the box's cushion, the stump's doorway, the lily pads, the igloo's door, the cave
        // mouth) lands on the hero's spot. The paintings are square, so the anchor maps straight across.
        Sprite home = spot.home != null ? ArtLibrary.Get(spot.home) : null;
        float tagY = -0.8f;   // the name tag goes under the hero, or under the home if that reaches lower
        if (home != null)
        {
            Vector2 homeAt = spot.at - (spot.homeAnchor - new Vector2(0.5f, 0.5f)) * spot.homeSize;
            SpriteRenderer homePiece = Piece(Names[spot.index] + " Home", home, homeAt, spot.homeSize, -10);
            if (spot.homePosition.HasValue) homePiece.transform.position = spot.homePosition.Value;
            if (spot.homeScale.HasValue) homePiece.transform.localScale = spot.homeScale.Value;
            // the home's front (the box's flap, the stump's front wall, the cave's rubble) drawn over the
            // hero, so they sit inside it: over every hero part (prefab orders reach 20), under the tags (40)
            Sprite front = ArtLibrary.Get(spot.home + "_Front");
            SpriteRenderer frontPiece = front != null
                ? Piece(Names[spot.index] + " Home front", front, homeAt, spot.homeSize, 30) : null;
            if (frontPiece != null)
            {
                if (spot.homePosition.HasValue) frontPiece.transform.position = spot.homePosition.Value;
                if (spot.homeScale.HasValue) frontPiece.transform.localScale = spot.homeScale.Value;
            }
            if (spot.groupHome)
            {
                var homeRoot = new GameObject(Names[spot.index] + " Home Group").transform;
                homeRoot.SetParent(root, false);
                homeRoot.position = homePiece.transform.position;
                homePiece.transform.SetParent(homeRoot, true);
                if (frontPiece != null) frontPiece.transform.SetParent(homeRoot, true);
                // Apply the saved group pose after parenting so both layers move and scale together.
                if (spot.homeGroupPosition.HasValue) homeRoot.localPosition = spot.homeGroupPosition.Value;
                if (spot.homeGroupScale.HasValue) homeRoot.localScale = spot.homeGroupScale.Value;
            }
            tagY = Mathf.Min(tagY, homeAt.y - spot.at.y - spot.homeSize * 0.42f);
        }
        Shadow(spot.at + Vector2.down * 0.35f, 0.9f);

        spot.state = StateOf(spot.index);
        // the name tag and status under the hero, so the home behind them stays clear
        spot.name = Label("Label " + Names[spot.index], Names[spot.index], spot.at + new Vector2(0f, tagY), 2.4f, Color.white);
        spot.status = Label("Camp Status", "", spot.at + new Vector2(0f, tagY - 0.45f), 2.4f, ArtLibrary.Cream);
        if (spot.state != State.Current) MakeNpc(spot, scale);
        Restyle(spot);
    }

    static bool HasTailAncestor(Transform t, Transform stop)
    {
        for (; t != null && t != stop; t = t.parent) if (t.name.Contains("Tail")) return true;
        return false;
    }

    static Vector3 PrefabScale(int index)
    {
        ChampionSpawner spawner = FindFirstObjectByType<ChampionSpawner>();
        GameObject prefab = spawner != null && index >= 0 && index < spawner.champions.Length ? spawner.champions[index] : null;
        return prefab != null ? prefab.transform.localScale : Vector3.one;
    }

    static float HydeScale() => Mathf.Max(0.01f, PrefabScale(ChampionSpawner.MrHyde).x);

    static State StateOf(int index)
    {
        if (index == Current) return State.Current;
        if (CharacterUnlocks.IsUnlocked(index)) return State.Ready;
        return CharacterUnlocks.Price(index) > 0 ? State.ForSale : State.NotReady;
    }

    // A hero standing at their tent: the playable prefab with everything but its look taken off
    // before it ever wakes (no PlayerCharacter, physics or scripts), so nothing mistakes it for you
    void MakeNpc(Spot spot, float scale)
    {
        ChampionSpawner spawner = FindFirstObjectByType<ChampionSpawner>();
        GameObject prefab = spawner != null && spot.index < spawner.champions.Length ? spawner.champions[spot.index] : null;
        if (prefab == null) return;
        var holder = new GameObject("Camp NPC holder");
        holder.SetActive(false);
        GameObject npc = Instantiate(prefab, holder.transform);
        // Keep only pure look scripts: the squirrel's tail is posed every frame by SquirrelTailHeading
        // (its standing-still pose; without it the tail stands at full length, a giant oval). It needs a
        // Rigidbody2D, which stays with simulation off: no physics, and nothing here is a PlayerCharacter
        foreach (MonoBehaviour m in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(m is SquirrelTailHeading)) DestroyImmediate(m);
        foreach (Joint2D j in npc.GetComponentsInChildren<Joint2D>(true)) DestroyImmediate(j);
        foreach (Collider2D c in npc.GetComponentsInChildren<Collider2D>(true)) DestroyImmediate(c);
        foreach (Rigidbody2D r in npc.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (r.GetComponent<SquirrelTailHeading>() != null) { r.simulated = false; continue; }
            DestroyImmediate(r);
        }
        npc.name = "Camp " + Names[spot.index];
        npc.transform.SetParent(root, false);
        npc.transform.position = spot.at;
        npc.transform.localScale = prefab.transform.localScale * scale;
        Destroy(holder);
        // A hero's root is not always the middle of its drawing, and facing you mirrors it about the
        // root: hang it from a pivot at the drawing's true centre (the squirrel's tall tail left out),
        // so it sits in the middle of its wallow, cushion or lily pads and turns about itself
        var pivot = new GameObject("Camp " + Names[spot.index] + " pivot").transform;
        pivot.SetParent(root, false);
        pivot.position = spot.at;
        bool any = false;
        Bounds drawn = new Bounds(spot.at, Vector3.zero);
        foreach (SpriteRenderer sr in npc.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr.sprite == null || sr.GetComponentInParent<SquirrelTailHeading>() != null && sr.name.Contains("Tail")
                || HasTailAncestor(sr.transform, npc.transform)) continue;
            if (any) drawn.Encapsulate(sr.bounds); else { drawn = sr.bounds; any = true; }
        }
        if (any) npc.transform.position += (Vector3)(spot.at - (Vector2)drawn.center);
        npc.transform.SetParent(pivot, true);
        // Optional saved Inspector pose for the artwork inside its camp pivot.
        if (spot.npcLocalPosition.HasValue) npc.transform.localPosition = spot.npcLocalPosition.Value;
        if (spot.npcLocalRotation.HasValue) npc.transform.localRotation = Quaternion.Euler(spot.npcLocalRotation.Value);
        if (spot.npcPivotPosition.HasValue) pivot.position = spot.npcPivotPosition.Value;
        if (spot.npcFacingX.HasValue) pivot.localScale = new Vector3(spot.npcFacingX.Value, 1f, 1f);
        // a solid little body, so you walk round a friend instead of through them (no rigidbody: static)
        var body = new GameObject("Camp NPC body");
        body.transform.position = pivot.position;
        body.transform.SetParent(root, true);
        body.AddComponent<CircleCollider2D>().radius = 0.3f;
        spot.npc = pivot;
        spot.npcScale = pivot.localScale;
        foreach (SpriteRenderer sr in npc.GetComponentsInChildren<SpriteRenderer>(true)) { spot.parts.Add(sr); spot.colors.Add(sr.color); }
    }

    // Unlocked heroes in full colour; for hire a little dimmed; not released dimmer still
    void Restyle(Spot spot)
    {
        float dim = spot.state == State.ForSale ? 0.62f : spot.state == State.NotReady ? 0.45f : 1f;
        for (int i = 0; i < spot.parts.Count; i++)
            if (spot.parts[i] != null)
            {
                Color c = spot.colors[i];
                spot.parts[i].color = new Color(c.r * dim, c.g * dim, c.b * dim, c.a);
            }
        if (spot.status == null) return;
        switch (spot.state)
        {
            case State.Current: spot.status.text = "Your home"; break;
            case State.Ready: spot.status.text = ""; break;
            case State.ForSale: spot.status.text = "For hire $" + CharacterUnlocks.Price(spot.index).ToString("N0"); break;
            default: spot.status.text = "Coming soon"; break;
        }
        spot.status.gameObject.SetActive(spot.status.text != "");
    }

    // --- Each frame ----------------------------------------------------------------------------------

    void Update()
    {
        if (player == null || leaving) return;
        Vector2 me = player.transform.position;

        // The fire flickers on the boil clock, like every other drawn flame
        if (fire != null && fireFrames.Length > 0) fire.sprite = fireFrames[StopMotion.BoilFrame % fireFrames.Length];

        // The heroes sway on the shared beat and turn to face you
        int rock = StopMotion.Enabled ? StopMotion.Rock : 0;
        foreach (Spot s in spots)
        {
            if (s.npc == null) continue;
            bool left = me.x < s.at.x;
            float facingX = s.npcFacingX ?? (left ? 1f : -1f);
            s.npc.localScale = new Vector3(Mathf.Abs(s.npcScale.x) * facingX, s.npcScale.y, s.npcScale.z);
            s.npc.localRotation = Quaternion.Euler(0f, 0f, -rock * StopMotion.RockDegrees);
        }

        // The nearest hero within reach is the one you talk to
        Spot best = null;
        float bestD = 2.0f;   // heroes are battle-sized here: about a body and a half away
        foreach (Spot s in spots)
        {
            if (s.state == State.Current) continue;
            float d = Vector2.Distance(me, s.npc != null ? (Vector2)s.npc.position : s.at);
            if (d < bestD) { best = s; bestD = d; }
        }
        if (best != near) { near = best; said = ""; offer = null; }

        if (!gateArmed && Vector2.Distance(me, gateAt) > 1.4f) gateArmed = true;
        if (gateArmed && Vector2.Distance(me, gateAt) < 0.7f) { StartCoroutine(Leave()); return; }

        Keyboard k = Keyboard.current;
        if (k == null || DeveloperConsole.CapturesInput || TutorialDialogue.Playing || near == null) return;
        if (k.eKey.wasPressedThisFrame) Talk(near);
        if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) Choose(near);
    }

    // First Enter: the pop-out with the skill clip and the choice. Second Enter: the choice itself
    void Choose(Spot s)
    {
        if (offer == s) Act(s);
        else if (s.state == State.Ready || s.state == State.ForSale) offer = s;
        else Act(s);   // not ready: just says so
    }

    void Talk(Spot s)
    {
        s.line++;
        said = CampDialogue.GetLine(s.index, Current, s.line);
        saidUntil = Time.unscaledTime + 6f;
    }

    void Act(Spot s)
    {
        switch (s.state)
        {
            case State.Ready: StartCoroutine(SwitchTo(s.index)); break;
            case State.ForSale:
                if (CharacterUnlocks.TryPurchase(s.index, out string message))
                {
                    s.state = State.Ready;
                    Restyle(s);
                    Note(Names[s.index] + " joins the camp! Press [Enter] to play as them.");
                }
                else Note(message);
                break;
            case State.NotReady:
                Note(Names[s.index] + " is not ready to join yet.");
                break;
        }
    }

    void Note(string text) { note = text; noteUntil = Time.unscaledTime + 3.5f; }

    // Play as another hero: the old screen's pick, then the camp re-opens with you at their tent
    IEnumerator SwitchTo(int index)
    {
        leaving = true;
        Pick(index);
        yield return null;
        while (TutorialDialogue.Playing) yield return null;
        arriveAt = index;
        fadeIn = 1f;
        LevelLoader.ScriptedTutorial = true;
        LevelLoader.Requested = source;
        LevelLoader.WaitToAdvance = true;
        LevelLoader.AdvanceNow = false;
        SceneManager.LoadScene(LevelLayout.SceneFor(source));
    }

    static void Pick(int index)
    {
        ChampionSpawner.Chosen = index;
        CrossLevelStatus.StartFreshKeepingGoods(ChampionSpawner.RecordType(index));
        ProgressStore.SelectedCharacter = index.ToString();
        PlayAnalytics.CharacterPicked(index, Active != null ? Time.unscaledTime - Active.openedAt : 0f);
        TutorialEvents.Fire(TutorialEvents.CharacterPicked);
        TutorialEvents.Fire(TutorialEvents.Picked(index));
    }

    // Through the gate: back to the map. Leaving counts as picking whoever you are, as the old screen did
    IEnumerator Leave()
    {
        leaving = true;
        if (!ProgressStore.HasPickedCharacter) Pick(Current);
        yield return null;
        while (TutorialDialogue.Playing) yield return null;
        GameFlow flow = FindFirstObjectByType<GameFlow>();
        if (flow != null && flow.CurrentNode != null && flow.CurrentNode.type == NodeType.RolePick) flow.CompleteCurrentNode();
        else if (flow != null) flow.OpenMap();
        else SceneManager.LoadScene("Menu");
    }

    // --- The talk card -------------------------------------------------------------------------------

    // The talk card and the pop-out, in ScreenFrame's 1280 x 720 OnGUI space
    const float CardW = 660f, PopW = 340f, PopEdge = 24f;   // the pop-out is a PopW square: just the clip

    void OnGUI()
    {
        if (DeveloperConsole.CapturesInput) return;
        ScreenFrame.BeginGUI();
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperLeft };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, wordWrap = true, alignment = TextAnchor.UpperLeft };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter };
            if (PaperType.Display != null) titleStyle.font = PaperType.Display;
            if (PaperType.Body != null) { bodyStyle.font = PaperType.Body; hintStyle.font = PaperType.Body; }
            titleStyle.normal.textColor = new Color(1f, 0.86f, 0.45f);
            bodyStyle.normal.textColor = hintStyle.normal.textColor = ArtLibrary.Cream;
        }
        const float W = ScreenFrame.GuiWidth, H = ScreenFrame.GuiHeight;

        Rect card = default;
        if (near != null && !leaving)
        {
            // The talk card opens on the half of the screen away from the friend, so it never hides them.
            // With the pop-out open it moves aside to share the width, so neither covers the other
            bool friendLow = FriendLow(near);
            bool popOut = offer == near && !TutorialDialogue.Playing;
            bool popRight = popOut && PopOutRight(near);
            float spare = popOut ? PopW + PopEdge : 0f;
            card = new Rect((popRight ? 0f : spare) + (W - spare - CardW) / 2, friendLow ? 104 : H - 196, CardW, 168);
            GUI.Box(card, "");
            GUI.Label(new Rect(card.x + 26, card.y + 16, 400, 34), Names[near.index], titleStyle);
            // Scrat's wordless exchanges are written action beats, not spoken sentences.
            string body = Time.unscaledTime < saidUntil && said != ""
                ? (said.StartsWith("[") ? said : "\"" + said + "\"") : Blurb(near);
            GUI.Label(new Rect(card.x + 26, card.y + 54, card.width - 52, 60), body, bodyStyle);
            // Every key hint lives here on the talk card; the pop-out is only the clip
            if (GUI.Button(new Rect(card.x + 26, card.y + 116, 170, 38), "[E] Talk")) Talk(near);
            string ask = near.state == State.NotReady ? "Not ready yet"
                       : !popOut ? "[Enter] Sure, show me!"
                       : near.state == State.Ready ? "[Enter] Play as " + Names[near.index]
                       : "[Enter] Hire  $" + CharacterUnlocks.Price(near.index).ToString("N0");
            GUI.enabled = near.state != State.NotReady;
            if (GUI.Button(new Rect(card.x + 212, card.y + 116, 300, 38), ask)) Choose(near);
            GUI.enabled = true;
            if (popOut && CharacterSkillDemo.Supports(near.index))
                CharacterSkillDemo.ShowInFrame(this, near.index,
                    new Rect(popRight ? W - PopEdge - PopW : PopEdge, (H - PopW) / 2, PopW, PopW));
            else CharacterSkillDemo.Hide(this);
        }
        else { CharacterSkillDemo.Hide(this); offer = null; }
        if (near == null && !leaving)
            GUI.Label(new Rect(0, H - 62, W, 30), "Walk up to a friend to talk or play as them.   The hatch leads back to the map.", hintStyle);

        if (Time.unscaledTime < noteUntil)
        {
            // under the talk card's middle (it moves aside for the pop-out)
            float noteY = near != null && FriendLow(near) ? 284 : H - 252;
            float noteX = near != null && !leaving ? card.center.x - 300 : W / 2 - 300;
            GUI.Box(new Rect(noteX, noteY, 600, 44), "");
            GUI.Label(new Rect(noteX, noteY, 600, 44), note, hintStyle);
        }

        // A short fade after switching heroes, so the re-opened camp doesn't pop
        if (fadeIn > 0f)
        {
            fadeIn = Mathf.Max(0f, fadeIn - Time.unscaledDeltaTime * 2.5f);
            Color was = GUI.color;
            GUI.color = new Color(0.08f, 0.06f, 0.05f, fadeIn);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = was;
        }
    }

    // Pop out on the side away from the friend (the walker is in the middle)
    static bool PopOutRight(Spot s)
    {
        Camera cam = Camera.main;
        return cam != null ? cam.WorldToViewportPoint(s.npc != null ? s.npc.position : (Vector3)s.at).x < 0.5f : true;
    }

    // Is this friend in the lower half of the view (the camera follows the walker)
    static bool FriendLow(Spot s)
    {
        Camera cam = Camera.main;
        return cam != null ? cam.WorldToViewportPoint(s.at).y < 0.5f : s.at.y < 0f;
    }

    static string Blurb(Spot s)
    {
        switch (s.state)
        {
            case State.Ready: return "Fancy an adventure down the mine together?";
            case State.ForSale: return "Fancy an adventure? I'll join you for $" + CharacterUnlocks.Price(s.index).ToString("N0") + ".";
            default: return "Still getting ready. You can chat, but they can't join yet.";
        }
    }

    // --- Helpers -------------------------------------------------------------------------------------

    SpriteRenderer Piece(string pieceName, Sprite art, Vector2 at, float size, int order)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(root, false);
        go.transform.position = at;
        float across = Mathf.Max(art.bounds.size.x, art.bounds.size.y);
        go.transform.localScale = Vector3.one * (size / Mathf.Max(0.001f, across));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        sr.sortingOrder = order;
        return sr;
    }

    SpriteRenderer Shadow(Vector2 at, float size)
    {
        Sprite soft = ArtLibrary.Get("Fx/SoftShadow");
        if (soft == null) return new GameObject("No Shadow").AddComponent<SpriteRenderer>();
        SpriteRenderer sr = Piece("Shadow", soft, at, size, -35);
        sr.color = new Color(0f, 0f, 0f, 0.45f);
        sr.transform.localScale = new Vector3(sr.transform.localScale.x, sr.transform.localScale.y * 0.45f, 1f);
        return sr;
    }

    TextMeshPro Label(string objectName, string text, Vector2 at, float width, Color color)
    {
        var go = new GameObject(objectName);   // "Label..." gets PaperTags' felt tag under it
        go.transform.SetParent(root, false);
        go.transform.position = at;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 2.6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.rectTransform.sizeDelta = new Vector2(width, 0.6f);
        tmp.sortingOrder = 40;
        return tmp;
    }

    static void Place(PlayerCharacter who, Vector2 at)
    {
        if (who == null) return;
        Rigidbody2D body = who.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = at; body.linearVelocity = Vector2.zero; }
        who.transform.position = at;
    }

    static Vector2 Cell(LevelData level, Dictionary<string, object> data, string key, Vector2 fallback)
    {
        if (data != null && data.TryGetValue(key, out object v) && v is Dictionary<string, object> d) return GridAt(level, d);
        return LevelData.GridToWorld(level, fallback.x, fallback.y);
    }

    static Vector2 GridAt(LevelData level, Dictionary<string, object> d)
        => LevelData.GridToWorld(level, d.TryGetValue("x", out object x) ? ToFloat(x) : 0f, d.TryGetValue("y", out object y) ? ToFloat(y) : 0f);

    static float ToFloat(object o) => o is double dd ? (float)dd : o is long l ? l : o is int i ? i : o is float f ? f : 0f;
}
