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
// Roll dust and swift afterimages live in PaperSkillFx so skill effects have a single owner.
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
        public bool wasWalking;
    }

    class Enemy
    {
        public Transform root;
        public readonly List<Transform> eyes = new List<Transform>(), teeth = new List<Transform>();
        public float nextGlare, glareAt = -10f;
    }

    static PaperPuppet instance;
    readonly List<Actor> actors = new List<Actor>();
    readonly List<Enemy> enemies = new List<Enemy>();
    readonly Dictionary<Hook, bool> hookBusy = new Dictionary<Hook, bool>();
    readonly List<Hook> hooks = new List<Hook>();
    readonly HashSet<int> known = new HashSet<int>();
    readonly List<Saved> saved = new List<Saved>();
    // These are additive deltas, not snapshots of gameplay's transforms: skills still own the base.
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
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.OnScene(); };
    }

    void OnEnable() { Treasure.Eaten += OnEaten; }
    void OnDisable() { Treasure.Eaten -= OnEaten; Restore(); held.Clear(); heldFrame = int.MinValue; }

    internal static void ResetHeldPose()
    {
        if (instance == null) return;
        instance.Restore();
        instance.held.Clear();
        instance.heldFrame = int.MinValue;
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
        saved.Clear();
        held.Clear();
        heldFrame = int.MinValue;
        actors.Clear();
        enemies.Clear();
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
    }

    void Remember(Transform t)
    {
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
                h.t.localPosition += h.pos;
                h.t.localRotation *= h.rot;
                h.t.localScale = Vector3.Scale(h.t.localScale, h.scale);
            }
            return;
        }
        float now = StopMotion.Enabled ? StopMotion.Time : Time.time;
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (actors[i].player == null) { actors.RemoveAt(i); continue; }
            Perform(actors[i], now);
        }
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i].root == null) { enemies.RemoveAt(i); continue; }
            Glare(enemies[i], now);
        }
        held.Clear();
        heldFrame = frame;
        if (!StopMotion.Enabled) return;
        // Cache the outputs once per art tick. Rounding alone would let the ear spring and look
        // drift every render frame; reapplying these deltas gives actual temporal holds.
        for (int i = 0; i < saved.Count; i++)
        {
            Saved s = saved[i];
            if (s.t == null) continue;
            Vector3 scale = s.t.localScale;
            held.Add(new Saved { t = s.t, pos = s.t.localPosition - s.pos,
                rot = Quaternion.Inverse(s.rot) * s.t.localRotation,
                scale = new Vector3(s.scale.x == 0f ? 1f : scale.x / s.scale.x,
                    s.scale.y == 0f ? 1f : scale.y / s.scale.y,
                    s.scale.z == 0f ? 1f : scale.z / s.scale.z) });
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
                if (t == p.transform || t.GetComponent<Collider2D>() != null) continue;
                string n = t.name;
                float side = Mathf.Sign(p.transform.InverseTransformPoint(t.position).x + 0.0001f);
                if (n.Contains("Ear")) a.ears.Add(new Part { t = t, side = side });
                else if (n.StartsWith("Eye") && !n.Contains("Catchlight")) a.eyes.Add(new Part { t = t, side = side });
                else if (n == "ShieldBubble") a.bubble = t;
            }
            actors.Add(a);
        }
        foreach (Transform root in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!root.name.Contains("Meowmeow") || !known.Add(root.GetInstanceID())) continue;
            var e = new Enemy { root = root, nextGlare = Time.time + ArtLibrary.Range(1f, 3f) };
            var reds = new List<Transform>();
            foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.color.r > 0.5f && r.color.g < 0.2f && r.transform != root && r.GetComponent<Collider2D>() == null) reds.Add(r.transform);
            if (reds.Count == 0) continue;
            float biggest = 0f;
            foreach (Transform t in reds) biggest = Mathf.Max(biggest, t.localScale.x);
            foreach (Transform t in reds) (t.localScale.x > biggest * 0.6f ? e.eyes : e.teeth).Add(t);
            enemies.Add(e);
        }
        foreach (Hook h in FindObjectsByType<Hook>(FindObjectsSortMode.None))
            if (!hookBusy.ContainsKey(h)) hookBusy[h] = h.IsBusy;
        // Reserve during discovery, so posing/reapplying never grows a list on a render frame.
        int capacity = 0;
        foreach (Actor a in actors) capacity += a.ears.Count + a.eyes.Count + (a.bubble != null ? 1 : 0);
        foreach (Enemy e in enemies) capacity += e.eyes.Count + e.teeth.Count;
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
        float onTwos = 1f / StopMotion.Twos;

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
            float angle = lean + a.earGait * 3f - e.side * flat + e.side * wiggle + (e.side > 0f ? flick : 0f);
            e.t.localRotation = e.t.localRotation * Quaternion.Euler(0f, 0f, angle);
            e.t.localPosition += e.t.parent.InverseTransformVector(gaitShift * a.earGait);
        }

        // ---- eyes
        float squeeze = 1f;
        if (sinceBlink < 3f * onTwos) squeeze = sinceBlink < onTwos ? 0.45f : sinceBlink < 2f * onTwos ? 0.1f : 0.6f;
        if (digesting) squeeze = Mathf.Min(squeeze, (StopMotion.BoilFrame & 1) == 0 ? 0.55f : 0.45f);
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
            e.t.localPosition += parent.InverseTransformVector(gaitShift * a.gait);
            e.t.localRotation *= Quaternion.Euler(0f, 0f, a.gait * 3f);
        }

        // ---- shield bubble breathes on threes
        if (a.bubble != null && a.bubble.gameObject.activeInHierarchy)
        {
            Remember(a.bubble);
            float breath = (StopMotion.BoilFrame % 3) == 0 ? 1.05f : (StopMotion.BoilFrame % 3) == 1 ? 1f : 0.97f;
            a.bubble.localScale *= breath;
        }

        // ---- charge steam: keep its cadence here; PaperSkillFx owns roll dust and swift
        // afterimages, so emitting either here as well would duplicate the skill performance.
        if (charging && Time.time >= a.nextPuff && a.velocity.sqrMagnitude > 0.5f)
        {
            a.nextPuff = Time.time + 2f * onTwos;
            Vector3 behind = at - (Vector3)(a.velocity.normalized * 0.35f);
            PaperFx.Puff(behind, 0.7f);                       // Hulk/Rhino: felt steam
        }
    }

    // --- enemies and hooks --------------------------------------------------------------------------

    static float EventTime(float time) => StopMotion.Enabled ? StopMotion.Snap(time, StopMotion.Twos) : time;

    void Glare(Enemy e, float now)
    {
        if (!e.root.gameObject.activeInHierarchy) return;
        if (Time.time >= e.nextGlare) { e.glareAt = Time.time; e.nextGlare = Time.time + ArtLibrary.Range(1.8f, 3.6f); }
        float since = now - EventTime(e.glareAt);
        float narrow = since < 4f / StopMotion.Twos ? (since < 1f / StopMotion.Twos ? 0.8f : 0.55f) : 1f;
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

// Runs before every other script each frame: puts back what PaperPuppet posed for the last picture.
[DefaultExecutionOrder(-32000)]
public class PaperPuppetRestore : MonoBehaviour
{
    internal PaperPuppet puppet;
    void FixedUpdate() { if (puppet != null) puppet.Restore(); }   // physics steps come first in a frame
    void Update() { if (puppet != null) puppet.Restore(); }
}
