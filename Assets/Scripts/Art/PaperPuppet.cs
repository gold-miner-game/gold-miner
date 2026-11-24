using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: stop-motion "performance" for characters, enemies and hooks, on top of the game, never in it.
//
// How it stays out of gameplay: an additive pose layer. Before any script's Update, every part this
// moved is put back exactly as it was (PaperPuppetRestore, earliest order); after every LateUpdate
// it is re-posed for the picture (here, latest order). So gameplay, skills, Elsa's mouth, the cat's
// tired eyes and every other script always read and write the real values. Only child art parts
// (ears, eyes, shield bubble, enemy features) move: never a root, never anything with a collider.
//
// Performances (poses on twos, StopMotion; docs/art-style.md):
//   idle      blinks every few seconds; an ear flick now and then
//   move      eyes look where it is going; ears trail the motion and overshoot when it stops
//   eat       a gulp (eyes pop wide), then a content squint
//   digest    chewing: ears wiggle alternately on threes, eyes half closed
//   block     brace: a tiny wind-up, then ears pinned back and eyes squeezed shut; a dust ring
//   charge    ears flat back, felt steam puffs left behind (Hulk)
//   shield    the bubble breathes on threes
//   hit       flinch: eyes clench, ears flatten, felt heart scraps fly off
//   enemies   Evil Meowmeow's eyes narrow in a glare, teeth chatter
//   hooks     a dirt puff at the burrow when a hook shoots
// Roll dust lives in PaperSkillFx so skill effects have a single owner (Swift has no effect).
[DefaultExecutionOrder(32000)]
public class PaperPuppet : MonoBehaviour
{
    struct Saved
    {
        public Transform t;
        public Vector3 pos, scale;
        public Quaternion rot;
    }

    class Part
    {
        public Transform t;
        public float side;             // -1 left of the head, +1 right
    }

    class Actor
    {
        public PlayerCharacter player;
        public readonly List<Part> ears = new List<Part>(), eyes = new List<Part>();
        public Transform bubble;
        public readonly List<Transform> features = new List<Transform>();   // the face's top-level cut-outs, rocked together
        public Vector3 lastAt;
        public Vector2 velocity;
        public float earAngle, earSpeed;      // spring for the trailing ears
        public float nextBlink, blinkAt = -10f, nextFlick, flickAt = -10f;
        public float gulpAt = -10f, hitAt = -10f, braceAt = -10f;
        public bool wasBlocking;
        public float nextPuff;
        public float poseAt;
        public int gaitTick, settlePoses;
        public float gait, earGait;
        public Vector3 waddle;        // this pose's walk sway (world), applied to the whole puppet with the rock
        public bool wasWalking;
    }

    class Enemy
    {
        public Transform root;
        public readonly List<Transform> eyes = new List<Transform>(), teeth = new List<Transform>();
        public readonly List<Transform> features = new List<Transform>();
        public float nextGlare, glareAt = -10f;
    }

    // Absolute eye outputs hold even when another look script changes the underlying base.
    sealed class Eye
    {
        public Transform t;
        public SpriteRenderer renderer;
        public Saved pose;
        public Sprite sprite, truthSprite;
        public Color color, truthColor;
        public bool enabled, truthEnabled, applied, captured;
    }
    readonly List<Eye> eyePictures = new List<Eye>();

    static bool IsEye(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.Contains("Eye") || t.name == "Catchlight") return true;
        return false;
    }

    void Eyes(bool capture)
    {
        foreach (Eye e in eyePictures)
        {
            if (e.t == null) continue;
            if (capture || !e.captured)
            {
                e.pose = new Saved { t = e.t, pos = e.t.localPosition, rot = e.t.localRotation, scale = e.t.localScale };
                if (e.renderer != null)
                { e.sprite = e.renderer.sprite; e.color = e.renderer.color; e.enabled = e.renderer.enabled; }
                e.captured = true;
            }
            else
            {
                Remember(e.t);
                e.t.localPosition = e.pose.pos;
                e.t.localRotation = e.pose.rot;
                e.t.localScale = e.pose.scale;
                if (e.renderer != null)
                {
                    e.truthSprite = e.renderer.sprite; e.truthColor = e.renderer.color; e.truthEnabled = e.renderer.enabled;
                    e.renderer.sprite = e.sprite; e.renderer.color = e.color; e.renderer.enabled = e.enabled;
                    e.applied = true;
                }
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        instance = null;
    }
    static void OnLoaded(Scene s, LoadSceneMode m) { if (instance != null) instance.OnScene(); }
    void OnDestroy() { SceneManager.sceneLoaded -= OnLoaded; if (instance == this) instance = null; }

    static PaperPuppet instance;
    readonly List<Actor> actors = new List<Actor>();
    readonly List<Enemy> enemies = new List<Enemy>();
    readonly Dictionary<Hook, bool> hookBusy = new Dictionary<Hook, bool>();
    readonly List<Hook> hooks = new List<Hook>();
    readonly HashSet<int> known = new HashSet<int>();
    readonly List<Saved> saved = new List<Saved>();
    // Display snapshots only; Restore returns the current gameplay base before each Update.
    readonly List<Saved> held = new List<Saved>();
    int heldFrame = int.MinValue;
    int lastLives = -1;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Puppet");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperPuppet>();
        go.AddComponent<PaperPuppetRestore>().puppet = instance;
        SceneManager.sceneLoaded += OnLoaded;
    }

    void OnEnable() { Treasure.Eaten += OnEaten; }
    void OnDisable() { Treasure.Eaten -= OnEaten; PaperHolds.RestoreNow(); Restore(); held.Clear(); heldFrame = int.MinValue; }

    internal static void ResetHeldPose()
    {
        if (instance == null) return;
        instance.Restore();
        instance.held.Clear();
        instance.heldFrame = int.MinValue;
        foreach (Eye e in instance.eyePictures) e.captured = false;
        foreach (Actor a in instance.actors)
        {
            if (a.player == null) continue;
            a.lastAt = a.player.transform.position;
            a.poseAt = Time.time;
            a.gait = a.earGait = 0f;
            a.wasWalking = false;
            a.settlePoses = a.gaitTick = 0;
        }
    }

    void OnScene()
    {
        PaperHolds.RestoreNow();
        Restore();
        saved.Clear();
        held.Clear();
        heldFrame = int.MinValue;
        actors.Clear();
        enemies.Clear();
        eyePictures.Clear();
        hookBusy.Clear();
        known.Clear();
        lastLives = -1;
        nextScan = 0f;
    }

    void OnEaten(Treasure t)
    {
        foreach (Actor a in actors) a.gulpAt = Time.time;
    }

    // Undo last frame's pose, before anyone's Update (called by PaperPuppetRestore).
    internal void Restore()
    {
        for (int i = 0; i < saved.Count; i++)
        {
            Saved s = saved[i];
            if (s.t == null) continue;
            s.t.localPosition = s.pos;
            s.t.localRotation = s.rot;
            s.t.localScale = s.scale;
        }
        saved.Clear();
        foreach (Eye e in eyePictures)
        {
            if (e.applied && e.renderer != null)
            { e.renderer.sprite = e.truthSprite; e.renderer.color = e.truthColor; e.renderer.enabled = e.truthEnabled; }
            e.applied = false;
        }
    }

    void Remember(Transform t)
    {
        // once per frame per part: the first record is the gameplay pose that Restore must return to
        for (int i = 0; i < saved.Count; i++) if (saved[i].t == t) return;
        saved.Add(new Saved { t = t, pos = t.localPosition, rot = t.localRotation, scale = t.localScale });
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan) Scan();
        WatchLives();
        WatchHooks(); // Gameplay edges must still be noticed between held pictures.
        int frame = StopMotion.Frame;
        if (StopMotion.Enabled && heldFrame == frame)
        {
            for (int i = 0; i < held.Count; i++)
            {
                Saved h = held[i];
                if (h.t == null || !h.t.gameObject.activeInHierarchy) continue;
                Remember(h.t);
                h.t.localPosition = h.pos;
                h.t.localRotation = h.rot;
                h.t.localScale = h.scale;
            }
            Eyes(false);
            return;
        }
        float now = StopMotion.Enabled ? StopMotion.Time : Time.time;
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (actors[i].player == null) { actors.RemoveAt(i); continue; }
            Perform(actors[i], now);
            // Elsa mid-breath does not rock: her beak stays lined up with the breath and its freeze cone
            bool casting = actors[i].player is PenguinController penguin && penguin.SkillSlot != null
                && penguin.SkillSlot.ActiveSkill != null && penguin.SkillSlot.ActiveSkill.BlocksMovement;
            if (actors[i].player.isActiveAndEnabled && !casting) RockFeatures(actors[i].features, actors[i].player.transform, -1f, actors[i].gait * 3f, actors[i].waddle);
        }
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i].root == null) { enemies.RemoveAt(i); continue; }
            Glare(enemies[i], now);
            if (enemies[i].root.gameObject.activeInHierarchy) RockFeatures(enemies[i].features, enemies[i].root, -1f, 0f, Vector3.zero);
        }
        if (StopMotion.Enabled) Eyes(true);
        held.Clear();
        heldFrame = frame;
        if (!StopMotion.Enabled) return;
        // Cache the outputs once per art tick. Rounding alone would let the ear spring and look
        // drift every render frame; reapplying absolute display poses holds their changing bases too.
        for (int i = 0; i < saved.Count; i++)
        {
            Saved s = saved[i];
            if (s.t == null) continue;
            held.Add(new Saved { t = s.t, pos = s.t.localPosition,
                rot = s.t.localRotation, scale = s.t.localScale });
        }
    }

    // --- finding the cast ---------------------------------------------------------------------------

    void Scan()
    {
        nextScan = Time.unscaledTime + 0.5f;
        foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
        {
            if (!known.Add(p.GetInstanceID())) continue;
            var a = new Actor { player = p, poseAt = Time.time, lastAt = p.transform.position, nextBlink = Time.time + ArtLibrary.Range(1f, 3f), nextFlick = Time.time + ArtLibrary.Range(4f, 8f) };
            foreach (Transform t in p.GetComponentsInChildren<Transform>(true))
            {
                if (t == p.transform || t.GetComponentInChildren<Collider2D>(true) != null
                    || t.GetComponentInChildren<Rigidbody2D>(true) != null) continue;
                if (IsEye(t, p.transform)) eyePictures.Add(new Eye { t = t, renderer = t.GetComponent<SpriteRenderer>() });
                string n = t.name;
                float side = Mathf.Sign(p.transform.InverseTransformPoint(t.position).x + 0.0001f);
                if (n.Contains("Ear") || n == "Scarf End" || n.StartsWith("Leg ")) a.ears.Add(new Part { t = t, side = side });   // the flappy bits: ears, the penguin's scarf end, the frog's legs
                else if (n.StartsWith("Eye") && !n.Contains("Catchlight")) a.eyes.Add(new Part { t = t, side = side });
                else if (n == "ShieldBubble") a.bubble = t;
                // top-level cut-outs, or containers of them (Artwork, Frog Visuals, a squirrel's Ear L), never a
                // piece that carries or holds a collider (hitboxes, tongue tip)
                if (t.parent == p.transform && n != "ShieldBubble" && t.GetComponentInChildren<SpriteRenderer>(true) != null
                    && t.GetComponentInChildren<Collider2D>(true) == null
                    && t.GetComponentInChildren<Rigidbody2D>(true) == null) a.features.Add(t);
            }
            actors.Add(a);
        }
        foreach (Transform root in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!root.name.Contains("Meowmeow") || !known.Add(root.GetInstanceID())) continue;
            var e = new Enemy { root = root, nextGlare = Time.time + ArtLibrary.Range(1f, 3f) };
            var reds = new List<Transform>();
            foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.color.r > 0.5f && r.color.g < 0.2f && r.transform != root && r.GetComponentInChildren<Collider2D>(true) == null
                    && r.GetComponentInChildren<Rigidbody2D>(true) == null) reds.Add(r.transform);
            if (reds.Count == 0) continue;
            float biggest = 0f;
            foreach (Transform t in reds) biggest = Mathf.Max(biggest, t.localScale.x);
            foreach (Transform t in reds) (t.localScale.x > biggest * 0.6f ? e.eyes : e.teeth).Add(t);
            foreach (Transform t in root)
                if (t.GetComponentInChildren<Collider2D>(true) == null && t.GetComponentInChildren<Rigidbody2D>(true) == null
                    && t.GetComponentInChildren<SpriteRenderer>() != null) e.features.Add(t);
            enemies.Add(e);
        }
        foreach (Hook h in FindObjectsByType<Hook>(FindObjectsSortMode.None))
            if (!hookBusy.ContainsKey(h)) hookBusy[h] = h.IsBusy;
        // Reserve during discovery, so posing/reapplying never grows a list on a render frame.
        eyePictures.RemoveAll(e => e.t == null);
        int capacity = eyePictures.Count;
        foreach (Actor a in actors) capacity += a.ears.Count + a.eyes.Count + a.features.Count + (a.bubble != null ? 1 : 0);
        foreach (Enemy e in enemies) capacity += e.eyes.Count + e.teeth.Count + e.features.Count;
        if (saved.Capacity < capacity) saved.Capacity = capacity;
        if (held.Capacity < capacity) held.Capacity = capacity;
    }

    // --- characters ---------------------------------------------------------------------------------

    void WatchLives()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (lastLives >= 0 && gm.Lives < lastLives)
            foreach (Actor a in actors)
            {
                a.hitAt = Time.time;
                PaperFx.HeartPop(a.player.transform.position, 0.6f);
            }
        lastLives = gm.Lives;
    }

    void Perform(Actor a, float now)
    {
        PlayerCharacter p = a.player;
        if (!p.isActiveAndEnabled)
        {
            a.lastAt = p.transform.position;
            a.poseAt = Time.time;
            return;
        }
        float dt = Mathf.Max(Time.time - a.poseAt, 0f);
        a.poseAt = Time.time;
        Vector3 at = p.transform.position;
        Vector2 v = dt > 0f ? (Vector2)((at - a.lastAt) / dt) : Vector2.zero;
        a.lastAt = at;
        a.velocity = Vector2.Lerp(a.velocity, v, 1f - Mathf.Exp(-12f * dt));

        // trailing ears: a spring toward the lean the motion asks for, overshooting when it stops
        float target = Mathf.Clamp(a.velocity.x * 3.2f, -16f, 16f);
        // Integrate only on pose ticks, using small steps so the 12 Hz spring stays stable.
        float remaining = Mathf.Min(dt, 0.25f);
        while (remaining > 0f)
        {
            float step = Mathf.Min(remaining, 1f / 120f);
            a.earSpeed += ((target - a.earAngle) * 140f - a.earSpeed * 11f) * step;
            a.earAngle += a.earSpeed * step;
            remaining -= step;
        }

        bool walking = StopMotion.Enabled && v.sqrMagnitude > 0.01f;
        if (walking)
        {
            if (!a.wasWalking) a.gaitTick = 0;
            a.earGait = a.gait; // Overlap: ears show the preceding drawing's weight shift.
            // Four drawings over six ticks: left plant, pass, right plant, pass (2 cycles/s).
            int gaitPose = a.gaitTick % 6;
            a.gait = gaitPose < 2 ? -1f : gaitPose == 2 ? 0f : gaitPose < 5 ? 1f : 0f;
            a.gaitTick = (a.gaitTick + 1) % 6;
            a.settlePoses = 2;
        }
        else if (StopMotion.Enabled && a.settlePoses > 0)
        {
            // Halfway home, then registered: exactly two held settle drawings, including ears.
            float settle = a.settlePoses == 2 ? 0.5f : 0f;
            a.gait *= settle;
            a.earGait *= settle;
            a.settlePoses--;
        }
        else a.gait = a.earGait = 0f;
        a.wasWalking = walking;
        float bodyWidth = p.body != null ? p.body.bounds.size.x : p.transform.lossyScale.x;
        Vector3 gaitShift = Vector3.right * (Mathf.Abs(bodyWidth) * 0.025f);
        // the walk sway moves the whole puppet together with the rock (RockFeatures), never the ears or eyes on
        // their own: parts on separate rhythms made a walking cat look loose (playtest)
        a.waddle = gaitShift * a.gait;

        bool blocking = p.IsBlocking;
        if (blocking && !a.wasBlocking)
        {
            a.braceAt = Time.time;
            PaperFx.Puff(at, 0.9f);
        }
        a.wasBlocking = blocking;
        bool digesting = p.IsDigesting;
        bool charging = p is RhinoController rhino && rhino.IsCharging;

        if (Time.time >= a.nextBlink) { a.blinkAt = Time.time; a.nextBlink = Time.time + ArtLibrary.Range(2.2f, 5f); }
        if (Time.time >= a.nextFlick) { a.flickAt = Time.time; a.nextFlick = Time.time + ArtLibrary.Range(5f, 9f); }

        float sinceBrace = now - EventTime(a.braceAt);
        float sinceHit = now - EventTime(a.hitAt);
        float sinceGulp = now - EventTime(a.gulpAt);
        float sinceBlink = now - EventTime(a.blinkAt);
        float sinceFlick = now - EventTime(a.flickAt);
        float onTwos = 1f / StopMotion.Pose;

        // ---- ears
        float flat = 0f;                                                   // degrees outward/back
        if (blocking) flat = sinceBrace < onTwos ? -10f : 38f;             // wind-up, then pinned
        if (charging) flat = 32f;
        if (sinceHit < 6f * onTwos) flat = 44f;
        float wiggle = digesting ? ((StopMotion.BoilFrame & 1) == 0 ? 7f : -7f) : 0f;
        float flick = sinceFlick < 2f * onTwos ? 16f : 0f;
        float lean = a.earAngle;
        foreach (Part e in a.ears)
        {
            if (e.t == null) continue;
            Remember(e.t);
            float angle = lean - e.side * flat + e.side * wiggle + (e.side > 0f ? flick : 0f);
            e.t.localRotation = e.t.localRotation * Quaternion.Euler(0f, 0f, angle);
        }

        // ---- eyes
        float squeeze = 1f;
        if (sinceBlink < 3f * onTwos) squeeze = sinceBlink < onTwos ? 0.45f : sinceBlink < 2f * onTwos ? 0.1f : 0.6f;
        if (digesting) squeeze = Mathf.Min(squeeze, 0.5f);   // a held, contented half-squint (a flutter read as non-stop blinking)
        float pop = 1f;
        if (sinceGulp < onTwos) pop = 1.3f;
        else if (sinceGulp < 3f * onTwos) squeeze = Mathf.Min(squeeze, 0.35f);
        if (blocking && sinceBrace >= onTwos) squeeze = Mathf.Min(squeeze, 0.25f);
        if (sinceHit < 6f * onTwos) squeeze = Mathf.Min(squeeze, 0.15f);
        Vector2 look = a.velocity.sqrMagnitude > 0.2f ? a.velocity.normalized * 0.05f : Vector2.zero;
        foreach (Part e in a.eyes)
        {
            if (e.t == null || !e.t.gameObject.activeInHierarchy) continue;
            Remember(e.t);
            Vector3 s = e.t.localScale;
            e.t.localScale = new Vector3(s.x * pop, s.y * pop * squeeze, s.z);
            Transform parent = e.t.parent != null ? e.t.parent : p.transform;
            Vector3 local = parent.InverseTransformVector(p.transform.TransformVector(look));
            e.t.localPosition += local;
        }

        // ---- shield bubble breathes on threes
        if (a.bubble != null && a.bubble.gameObject.activeInHierarchy)
        {
            Remember(a.bubble);
            float breath = (StopMotion.BoilFrame % 3) == 0 ? 1.05f : (StopMotion.BoilFrame % 3) == 1 ? 1f : 0.97f;
            a.bubble.localScale *= breath;
        }

        // ---- charge steam: keep its cadence here; PaperSkillFx owns roll dust (Swift has no effect),
        // so emitting dust here as well would duplicate the skill performance.
        if (charging && Time.time >= a.nextPuff && a.velocity.sqrMagnitude > 0.5f)
        {
            a.nextPuff = Time.time + 2f * onTwos;
            Vector3 behind = at - (Vector3)(a.velocity.normalized * 0.35f);
            PaperFx.Puff(behind, 0.7f);                       // Hulk/Rhino: felt steam
        }
    }

    // Stop-motion rock: in held poses the whole face (every top-level cut-out) tips about the body's centre,
    // leaning against the treasure (StopMotion.Rock, opposite sign), as if the animator nudged the puppet
    // between frames. The round body sprite is the gameplay root and stays put: it is a circle, so the tip
    // reads as the whole puppet rocking. Characters' mode only (StopMotion.Enabled).
    const float RockDegrees = 6f;

    void RockFeatures(List<Transform> parts, Transform pivot, float sign, float tilt, Vector3 sway)
    {
        if (!StopMotion.Enabled) return;
        int rock = StopMotion.Rock;
        // Remember the rest drawing too: controller-driven face/roll pivots can move between ticks.
        Quaternion turn = Quaternion.Euler(0f, 0f, sign * rock * RockDegrees + tilt);
        Vector3 centre = pivot.position;
        foreach (Transform t in parts)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Remember(t);
            t.SetPositionAndRotation(centre + turn * (t.position - centre) + sway, turn * t.rotation);
        }
    }

    // --- enemies and hooks --------------------------------------------------------------------------

    static float EventTime(float time) => StopMotion.Enabled ? StopMotion.Snap(time, StopMotion.Pose) : time;

    void Glare(Enemy e, float now)
    {
        if (!e.root.gameObject.activeInHierarchy) return;
        if (Time.time >= e.nextGlare) { e.glareAt = Time.time; e.nextGlare = Time.time + ArtLibrary.Range(1.8f, 3.6f); }
        float since = now - EventTime(e.glareAt);
        float narrow = since < 4f / StopMotion.Pose ? (since < 1f / StopMotion.Pose ? 0.8f : 0.55f) : 1f;
        foreach (Transform t in e.eyes)
        {
            if (t == null) continue;
            Remember(t);
            t.localScale = new Vector3(t.localScale.x * (2f - narrow) * 0.5f + t.localScale.x * 0.5f, t.localScale.y * narrow, t.localScale.z);
        }
        float chatter = (StopMotion.Frame & 1) == 0 ? 0.03f : -0.03f;   // on twos
        foreach (Transform t in e.teeth)
        {
            if (t == null) continue;
            Remember(t);
            t.localPosition += new Vector3(0f, chatter, 0f);
        }
    }

    void WatchHooks()
    {
        hooks.Clear();
        hooks.AddRange(hookBusy.Keys);
        foreach (Hook h in hooks)
        {
            if (h == null) { hookBusy.Remove(h); continue; }
            bool busy = h.IsBusy;
            if (busy && !hookBusy[h]) PaperFx.Puff(h.transform.position, 0.55f);
            hookBusy[h] = busy;
        }
    }
}
