using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: every hook now has someone pulling it. A felt Meowmeow miner (Resources/Art/Miner, painted with Meshy
// from the character sheet) peeks out of each burrow and acts out its hook, posed on twos:
//   - idle: peeking          - hook going out: leaning in, eyes wide
//   - reeling in: eyes squeezed, paws on the twine    - a red shot at you: narrow red glare
// The cat stays upright and leans toward where its hook went, so you can read who is hooking and at what.
// Like the miners of the classic gold-mining arcade games, it works a winch: each burrow is framed as a
// mine shaft (cut-paper timber planks) and a twine reel beside it turns a held pose at a time, paying out
// while the hook flies and cranking back while it reels in.
// Read-only: it follows each Hook's rope (its first point is the burrow) and the hook's own position and
// AimedAtPlayer flag. Drawn above the burrow and below the rope; never parented to the hook; no colliders.
public class PaperMiners : MonoBehaviour
{
    const float Size = 0.95f;  // world units across, a little wider than a burrow
    const float Peek = 0.22f;  // the cat sits on the rim, toward its hook: charcoal felt would vanish in the black burrow
    static PaperMiners instance;

    class Miner
    {
        public Hook hook;
        public LineRenderer rope;
        public SpriteRenderer look, frame, reel, paws;
        public float reelAngle;
        public float size = -1f;      // fitted once: smaller where rocks crowd the burrow (the world map)
        public Vector2 home;          // the burrow: remembered while the hook rests in it
        public bool homeKnown;
        public float lastReach;
        public int pose, lastFrame = -1, lastPoseFrame = -1;
        public float facing = -90f;   // degrees; the cat's top points this way
        public SpriteRenderer body;   // the old Miner prefab's paper square (non-compete levels): the miner's real body
        public MinerAI ai;            // whichever brain runs this hook, for its frozen state
        public SurvivalHookAI hole;
        public bool frozen;           // read once per pose (frost checks are not free)
        public SpriteRenderer flapL, flapR;   // the shaft's board hatch, for the level-start intro
        public int introStart;        // beat the intro began on; introDone once the cat has popped out
        public bool introDone, introSeen;   // the intro counts from the first beat the shaft is actually drawn
    }

    readonly List<Miner> miners = new List<Miner>();
    readonly List<Vector2> rockAt = new List<Vector2>();   // rock centres, refreshed each scan (no physics query)
    Sprite idle, eager, pull, glare, frameArt, reelArt;
    Sprite idlePaws, eagerPaws, pullPaws, glarePaws;   // the bottom band of each pose (paws on the rim), soft top edge
    const float PawLine = 0.16f;   // the paws band's top, below the sprite's centre, as a share of its height
    static readonly Color Ice = new Color(0.72f, 0.9f, 1f);   // the same ice as the frozen claw and twine
    float nextScan;
    Sprite hatch;   // Miner/HatchFlap, re-pivoted on its hinge edge
    // Level-start intro, in held poses from introStart: closed, closed, rattle, closed, flaps half down, flaps
    // down, the cat pops low, overshoots, settles (9 poses, ~1.1 s, inside a miner's 1.5 s start delay; if the
    // hook fires first, the intro just ends). Battle levels and Survival holes, not the world map.
    const int IntroPoses = 9;
    // Hooks whose shaft is still under its closed hatch: PaperClaws keeps their claw and twine out of sight.
    static readonly HashSet<Hook> closed = new HashSet<Hook>();
    public static bool UnderHatch(Hook hook) => closed.Contains(hook);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { closed.Clear(); SceneManager.sceneLoaded -= OnSceneLoaded; }   // domain reload is off: never stack

    static void OnSceneLoaded(Scene s, LoadSceneMode m) { closed.Clear(); if (instance != null) instance.nextScan = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Miners");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperMiners>();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        idle = ArtLibrary.Get("Miner/MinerIdle"); eager = ArtLibrary.Get("Miner/MinerEager");
        pull = ArtLibrary.Get("Miner/MinerPull"); glare = ArtLibrary.Get("Miner/MinerGlare");
        if (idle == null || eager == null || pull == null || glare == null) enabled = false;
        frameArt = ArtLibrary.Get("Miner/ShaftFrame"); reelArt = ArtLibrary.Get("Miner/WinchReel");
        idlePaws = ArtLibrary.Get("Miner/MinerIdle_paws"); eagerPaws = ArtLibrary.Get("Miner/MinerEager_paws");
        pullPaws = ArtLibrary.Get("Miner/MinerPull_paws"); glarePaws = ArtLibrary.Get("Miner/MinerGlare_paws");
        Sprite flap = ArtLibrary.Get("Miner/HatchFlap");
        if (flap != null)
        {
            Texture2D t = flap.texture;
            hatch = Sprite.Create(t, flap.rect, new Vector2(0f, 0.5f), flap.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (Hook hook in FindObjectsByType<Hook>(FindObjectsSortMode.None))
                if (!miners.Exists(m => m.hook == hook) && !Add(hook) && Time.timeSinceLevelLoad < 3f)
                    nextScan = Time.unscaledTime;   // still being built: look again next frame, so the closed hatch shows from the start
            rockAt.Clear();
            foreach (Rock rock in FindObjectsByType<Rock>(FindObjectsSortMode.None))
                if (rock.isActiveAndEnabled) rockAt.Add(rock.transform.position);
        }
        for (int i = miners.Count - 1; i >= 0; i--)
        {
            Miner m = miners[i];
            if (m.hook == null || m.rope == null)
            {
                if (m.look != null) Destroy(m.look.gameObject);
                if (m.paws != null) Destroy(m.paws.gameObject);
                if (m.flapL != null) Destroy(m.flapL.gameObject);
                if (m.flapR != null) Destroy(m.flapR.gameObject);
                if (m.frame != null) Destroy(m.frame.gameObject);
                if (m.reel != null) Destroy(m.reel.gameObject);
                miners.RemoveAt(i);
                continue;
            }
            Pose(m);
        }
    }

    bool Add(Hook hook)
    {
        Transform r = hook.transform.Find("Rope");
        LineRenderer rope = r != null ? r.GetComponent<LineRenderer>() : null;
        if (rope == null) return false;   // not built yet; the next scan tries again
        // The old Miner prefab (non-compete levels) draws its body as a 1x1 paper square 0.8 above the burrow.
        // That square is gameplay (frost freezes the miner through it), so it stays enabled at alpha 0 and the
        // felt cat stands on its spot, above the shaft, like the classic miner over the hole at his winch.
        SpriteRenderer body = null;
        MinerAI brain = null;
        foreach (MinerAI ai in FindObjectsByType<MinerAI>(FindObjectsSortMode.None))
        {
            if (ai.hook != hook) continue;
            brain = ai;
            SpriteRenderer sr = ai.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null && sr.sprite.name == "Square") body = sr;
            break;
        }
        SurvivalHookAI hole = null;
        if (brain == null)
            foreach (SurvivalHookAI h in FindObjectsByType<SurvivalHookAI>(FindObjectsSortMode.None))
                if (h.hook == hook) { hole = h; break; }
        bool intro = hatch != null && MapHub.Active == null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "world_map" && !hook.IsBusy;
        miners.Add(new Miner { hook = hook, rope = rope, body = body, ai = brain, hole = hole, look = Piece("Miner", idle), paws = Piece("Miner Paws", idlePaws), frame = Piece("Mine Shaft", frameArt), reel = Piece("Winch Reel", reelArt),
            flapL = intro ? Piece("Hatch Flap", hatch) : null, flapR = intro ? Piece("Hatch Flap", hatch) : null,
            introStart = miners.Count % 3, introDone = !intro });   // a stagger for now; shafts open a pose apart, not in unison
        return true;
    }

    SpriteRenderer Piece(string pieceName, Sprite art)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        sr.enabled = false;
        return sr;
    }

    // Distance from a point to the nearest rock centre.
    float Clearance(Vector2 at)
    {
        float best = float.PositiveInfinity;
        foreach (Vector2 r in rockAt) best = Mathf.Min(best, (r - at).sqrMagnitude);
        return Mathf.Sqrt(best);
    }

    const float RockRadius = 0.22f;   // about a drawn stone's radius on the world map

    void Pose(Miner m)
    {
        bool active = m.hook.isActiveAndEnabled;
        Vector2 at = m.hook.transform.position;
        if (!m.hook.IsBusy) { m.home = at; m.homeKnown = true; }
        else if (m.rope.enabled && m.rope.positionCount > 0) { m.home = m.rope.GetPosition(0); m.homeKnown = true; }
        m.look.enabled = active && m.homeKnown;
        if (m.frame != null) m.frame.enabled = m.look.enabled && frameArt != null;
        if (m.reel != null) m.reel.enabled = m.look.enabled && reelArt != null;
        if (m.paws != null) m.paws.enabled = m.look.enabled && idlePaws != null && eagerPaws != null && pullPaws != null && glarePaws != null;
        if (!m.look.enabled) return;

        // Fit once: the shaft frame (1.25 x size) must stay clear of the rocks round the burrow, so on the
        // tight world-map grid the miner, frame and reel shrink to the gap instead of covering stones.
        if (m.size < 0f && rockAt.Count > 0) m.size = Mathf.Clamp((Clearance(m.home) - RockRadius) * 2f / 1.25f, 0.35f, Size);
        float size = m.size > 0f ? m.size : Size;   // no rocks known yet (or none at all): full size
        float reach = Vector2.Distance(at, m.home);
        int frame = StopMotion.Frame;
        if (frame != m.lastFrame)
        {
            m.lastFrame = frame;
            m.frozen = (m.ai != null && m.ai.IsFrozen) || (m.hole != null && m.hole.IsFrozen);
        }
        if (frame == m.lastFrame && !m.frozen && m.lastPoseFrame != frame)
        {
            m.lastPoseFrame = frame;   // a frozen cat holds its last pose, reel and all, until it thaws
            // pose: 0 idle, 1 eager (going out), 2 pulling (coming back), 3 glare (a red shot at the player)
            int pose = 0;
            if (m.hook.IsBusy)
            {
                if (m.hook.AimedAtPlayer && reach >= m.lastReach) pose = 3;
                else pose = reach >= m.lastReach ? 1 : 2;
                Vector2 d = at - m.home;
                if (d.sqrMagnitude > 0.01f) m.facing = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }
            m.pose = pose;
            if (m.hook.IsBusy) m.reelAngle += reach >= m.lastReach ? -30f : 30f;   // pay out / crank back, a held pose per tick
            m.lastReach = reach;
        }
        m.look.sprite = m.pose == 1 ? eager : m.pose == 2 ? pull : m.pose == 3 ? glare : idle;

        // the art's top points up; turn it to face the hook's way. Lean the eager/glare poses a little out
        // of the burrow toward the shot, the pulling pose a little back in.
        Vector2 toward = new Vector2(Mathf.Cos(m.facing * Mathf.Deg2Rad), Mathf.Sin(m.facing * Mathf.Deg2Rad));
        float lean = (Peek + (m.pose == 1 || m.pose == 3 ? 0.08f : m.pose == 2 ? -0.05f : 0f)) * size / Size;
        // upright, as drawn (a cat turned sideways reads as a blob); it leans toward its hook instead
        // stop-motion rock (characters' mode): the cat's head and body tip on the shared beat from just above
        // its paws, while the paws stay planted on the rim: they are redrawn still on top from the same
        // drawing's bottom band (MinerX_paws), so a lying cat never slides its paws (playtest)
        Vector2 catAt = m.home + toward * lean;
        float catSize = size;
        if (m.body != null)
        {
            Color c = m.body.color;
            if (c.a > 0f) { c.a = 0f; m.body.color = c; }   // hidden, never disabled (frost eligibility)
            Bounds b = m.body.bounds;
            catAt = b.center;
            catSize = Mathf.Max(b.size.x, b.size.y) * 0.95f;
        }
        float catScale = catSize / Mathf.Max(0.01f, m.look.sprite.bounds.size.x);
        bool pawsOn = m.paws != null && m.paws.enabled;
        Quaternion tip = Quaternion.Euler(0f, 0f, pawsOn && StopMotion.Enabled && !m.frozen ? -StopMotion.Rock * StopMotion.RockDegrees : 0f);
        // Elsa's frost: the miner's real body is the hidden square (or the burrow), so its ice overlay cannot
        // show; the felt cat itself turns ice-blue while the miner is frozen, like the claw and twine do
        Color felt = m.frozen ? Ice : Color.white;
        m.look.color = felt;
        if (m.paws != null) m.paws.color = felt;
        Vector3 hinge = (Vector3)catAt + Vector3.down * (m.look.sprite.bounds.size.y * catScale * PawLine);
        m.look.transform.SetPositionAndRotation(hinge + tip * ((Vector3)catAt - hinge), tip);
        m.look.transform.localScale = Vector3.one * catScale;
        m.look.sortingLayerID = m.rope.sortingLayerID;
        // Resting, the cat is in front of its claw (the claw sits in the shaft behind it); while the hook is out,
        // the rope and claw pass over the cat. The claw art tops out 5 above the rope (PaperClaws' plate).
        int catOrder = m.hook.IsBusy ? m.rope.sortingOrder - 2 : m.rope.sortingOrder + 7;
        m.look.sortingOrder = catOrder;   // its still paws one above
        if (pawsOn)
        {
            m.paws.sprite = m.pose == 1 ? eagerPaws : m.pose == 2 ? pullPaws : m.pose == 3 ? glarePaws : idlePaws;
            m.paws.transform.SetPositionAndRotation(catAt, Quaternion.identity);
            m.paws.transform.localScale = Vector3.one * catScale;
            m.paws.sortingLayerID = m.rope.sortingLayerID;
            m.paws.sortingOrder = catOrder + 1;
        }

        // the shaft's timber frame round the burrow, and the winch reel beside it (to the cat's left)
        if (m.frame != null && frameArt != null)
        {
            m.frame.transform.SetPositionAndRotation(m.home, Quaternion.identity);
            m.frame.transform.localScale = Vector3.one * (size * 1.25f / frameArt.bounds.size.x);
            m.frame.sortingLayerID = m.rope.sortingLayerID;
            m.frame.sortingOrder = m.rope.sortingOrder - 4;
        }
        Intro(m, size, catAt, catSize, catScale);
        if (m.reel != null && reelArt != null)
        {
            // beside the shaft on whichever side has room; no room on either side: no reel (never over a rock)
            Vector2 left = new Vector2(-toward.y, toward.x);
            float reelR = size * 0.55f * 0.5f;
            Vector2 a = m.home + left * size * 0.78f, b = m.home - left * size * 0.78f;
            bool aFits = Clearance(a) >= reelR + RockRadius, bFits = Clearance(b) >= reelR + RockRadius;
            m.reel.enabled = aFits || bFits;
            m.reel.transform.SetPositionAndRotation(aFits ? a : b, Quaternion.Euler(0f, 0f, m.reelAngle));
            m.reel.transform.localScale = Vector3.one * (size * 0.55f / reelArt.bounds.size.x);
            m.reel.sortingLayerID = m.rope.sortingLayerID;
            m.reel.sortingOrder = m.rope.sortingOrder - 1;
        }
    }

    // The level-start hatch: the cat stays out of sight while the boards are closed, then pops out of the shaft.
    void Intro(Miner m, float size, Vector2 catAt, float catSize, float catScale)
    {
        if (m.introDone) { closed.Remove(m.hook); return; }
        if (!m.introSeen) { m.introSeen = true; m.introStart += StopMotion.Frame; }   // first drawn beat: closed from here
        int k = StopMotion.Frame - m.introStart;
        if (m.hook.IsBusy || k >= IntroPoses)   // done, or the hook fired first: straight to play
        {
            m.introDone = true;
            closed.Remove(m.hook);
            if (m.flapL != null) Destroy(m.flapL.gameObject);
            if (m.flapR != null) Destroy(m.flapR.gameObject);
            m.flapL = m.flapR = null;
            return;
        }
        float fw = size * 1.25f;                       // the frame's drawn width; the opening is its inner 72%
        float half = 0.36f * fw, tall = 0.72f * fw;
        Vector2 art = hatch.bounds.size;
        bool flapsOn = k < 6;
        if (k < 5) closed.Add(m.hook); else closed.Remove(m.hook);   // the claw shows as the flaps fall away
        float open = k == 4 ? 0.55f : k == 5 ? 0.18f : 1f;   // swinging down into the shaft, foreshortened on its hinge
        float shade = k == 4 ? 0.72f : k == 5 ? 0.5f : 1f;   // and darker as it falls into the dark
        float rattle = k == 2 ? 3f : 0f;
        for (int side = 0; side < 2; side++)   // no per-frame array: the two flaps in turn
        {
            SpriteRenderer f = side == 0 ? m.flapL : m.flapR;
            if (f == null) continue;
            bool right = side == 1;
            f.enabled = flapsOn && m.frame != null && m.frame.enabled;
            if (!f.enabled) continue;
            f.flipX = right;                            // pivot on the hinge edge: the mirror swings the other way
            f.transform.SetPositionAndRotation(m.home + new Vector2(right ? half : -half, 0f), Quaternion.Euler(0f, 0f, right ? -rattle : rattle));
            f.transform.localScale = new Vector3(half / art.x * open, tall / art.y, 1f);
            f.color = new Color(shade, shade, shade, 1f);
            f.sortingLayerID = m.rope.sortingLayerID;
            f.sortingOrder = m.rope.sortingOrder - 3;
        }
        if (k < 6)   // the cat is still down the shaft
        {
            m.look.enabled = false;
            if (m.paws != null) m.paws.enabled = false;
            return;
        }
        float pop = k == 6 ? 0.55f : k == 7 ? 1.15f : 1f;   // pops low, overshoots, settles
        Vector2 at = catAt + Vector2.down * (catSize * (1f - Mathf.Min(1f, pop)) * 0.5f);
        m.look.transform.SetPositionAndRotation(at, Quaternion.identity);
        m.look.transform.localScale = Vector3.one * catScale * pop;
        if (m.paws != null && m.paws.enabled)
        {
            m.paws.transform.SetPositionAndRotation(at, Quaternion.identity);
            m.paws.transform.localScale = Vector3.one * catScale * pop;
        }
    }
}
