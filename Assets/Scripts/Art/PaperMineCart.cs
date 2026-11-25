using System.Collections.Generic;
using UnityEngine;

// Art: a WALKING hook hole (Red Round, Gold Round) drawn as a felt Meowmeow riding a mine cart.
// A fixed miner works a winch over a timber shaft (PaperMiners); a shaft cannot walk, and a walker
// has to read as mobile even while it stands, so it rides a wheeled cart with its winch on board:
// a red riveted tub with a stitched red felt rim (Red Round), or an ochre-gold tub heaped with
// nuggets and a sack (Gold Round). Meshy-painted, Resources/Art/Enemies/Cart{Red,Gold}_*, built by
// tools/art/make_cart.py. The tub is painted without wheels: two spoked wheels (Miner/WinchReel)
// are separate cut-outs that turn a notch per pose while it rolls, and it leaves twin wheel ruts.
// Art only: it swaps the hole's own root sprite, flip and sorting order and adds collider-free
// children, so the hole's collider (fitted to the old disc), HP blink, frost, hook and every
// gameplay value stay as they were.
//   rolling ........ Lurk: ducked to the ears, the cart rocking over bumps, wheels turning, ruts left
//   standing ....... Lurk, wheels still
//   telegraph ...... Aim: up on the rim, glaring at its target
//   hook out ....... Reel: standing in the cart, straining on the twine
//   hit ............ Hit: X eyes / coins popping, while HookHealth blinks
//   frozen ......... holds the pose and facing it was caught in, wheels iced, until it thaws
// It faces its target (the player, or the gold for a Gold Round), turning only on a new pose.
[DisallowMultipleComponent]
public class PaperMineCart : MonoBehaviour
{
    public enum Kind { Red, Gold }
    public Kind kind;

    [Tooltip("Degrees a wheel turns per pose while rolling (a held notch, not a smooth spin)")]
    public float wheelNotch = 40f;
    [Tooltip("World units between two pairs of wheel ruts")]
    public float rutEvery = 0.3f;
    [Tooltip("Seconds a rut stays on the floor")]
    public float rutLife = 1.4f;

    // Wheel hubs on the 360 px cart canvas, in the sprite's local units (see make_cart.py)
    static readonly Vector2 WheelHub = new Vector2(0.415f, -0.54f);
    const float WheelSize = 0.36f;   // local units across
    // While its hook rests the cat sits in front of the resting brass claw; while the hook is out the
    // rope and claw pass over it. Same orders as the shaft cats (PaperMiners): rope +7 / rope -2
    const int FrontOfRope = 7, BehindRope = -2;
    const int MaxRuts = 24;
    static readonly Color Ice = new Color(0.72f, 0.88f, 1f);
    static readonly Color RutColor = new Color(0.8f, 0.68f, 0.52f, 0.5f);   // pale dust: dark ruts vanish on the dark mine floor

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    RedEnemy walker;
    SurvivalHookAI ai;
    HookHealth health;
    SpriteRenderer look;
    LineRenderer rope;
    Sprite lurk, rockA, rockB, aim, reel, hit;
    Sprite shown;
    SpriteRenderer wheelL, wheelR;
    float wheelAngle;
    Vector3 lastPosition, rutFrom;
    int lastFrame = int.MinValue, rollStep;
    bool wasFrozen;

    struct Rut { public SpriteRenderer sr; public float born; }
    readonly List<Rut> ruts = new List<Rut>();
    Transform rutRoot;
    Sprite rutArt;
    int rutNext = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache.Clear(); }

    static Sprite Get(string name)
    {
        if (!cache.TryGetValue(name, out Sprite s)) { s = ArtLibrary.Get(name); cache[name] = s; }
        return s;
    }

    void Start()
    {
        walker = GetComponent<RedEnemy>();
        ai = GetComponent<SurvivalHookAI>();
        health = GetComponent<HookHealth>();
        look = GetComponent<SpriteRenderer>();
        string set = "Enemies/Cart" + kind + "_";
        lurk = Get(set + "Lurk");
        if (ai == null || look == null || lurk == null) { enabled = false; return; }   // keeps the old hole disc
        rockA = Get(set + "Lurk_w1") ?? lurk;
        rockB = Get(set + "Lurk_w2") ?? lurk;
        aim = Get(set + "Aim") ?? lurk;
        reel = Get(set + "Reel") ?? aim;
        hit = Get(set + "Hit") ?? aim;
        look.color = Color.white;   // painted felt and card, not a tinted disc
        Transform r = ai.hook != null ? ai.hook.transform.Find("Rope") : null;
        rope = r != null ? r.GetComponent<LineRenderer>() : null;
        Sprite wheel = Get("Miner/WinchReel");
        if (wheel != null) { wheelL = Wheel("Wheel L", wheel, -1f); wheelR = Wheel("Wheel R", wheel, 1f); }
        rutArt = Get("Fx/Stitch");
        lastPosition = rutFrom = transform.position;
        Show(lurk);
        Order();
    }

    SpriteRenderer Wheel(string wheelName, Sprite art, float side)
    {
        var go = new GameObject(wheelName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(WheelHub.x * side, WheelHub.y, 0f);
        float across = Mathf.Max(art.bounds.size.x, art.bounds.size.y);
        go.transform.localScale = Vector3.one * (WheelSize / Mathf.Max(0.001f, across));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        return sr;
    }

    void OnDestroy() { if (rutRoot != null) Destroy(rutRoot.gameObject); }

    void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;   // paused: a still photo
        FadeRuts();
        bool frozen = walker != null && walker.IsFrozen;
        if (frozen != wasFrozen)
        {
            wasFrozen = frozen;
            Color wheelTint = frozen ? Ice : Color.white;   // the body's frost is FrostBody2D's; the wheels ice to match
            if (wheelL != null) wheelL.color = wheelTint;
            if (wheelR != null) wheelR.color = wheelTint;
        }
        if (frozen) return;   // held as it was caught

        int frame = StopMotion.Frame;
        bool newPose = frame != lastFrame;
        Sprite want = Pick(false);
        bool urgent = want == hit || want == aim || want == reel;
        if (!newPose && !(urgent && shown != want)) return;
        lastFrame = frame;

        Vector3 now = transform.position;
        Vector3 moved = now - lastPosition;
        bool moving = moved.sqrMagnitude > 0.0004f;
        lastPosition = now;
        if (newPose && moving)
        {
            rollStep++;
            // wheels turn the way it rolls on screen (clockwise going right), a notch per pose
            float dir = Mathf.Abs(moved.x) > 0.001f ? -Mathf.Sign(moved.x) : (moved.y > 0f ? 1f : -1f);
            wheelAngle += dir * wheelNotch;
            Quaternion turn = Quaternion.Euler(0f, 0f, wheelAngle);
            if (wheelL != null) wheelL.transform.localRotation = turn;
            if (wheelR != null) wheelR.transform.localRotation = turn;
        }
        if (moving) LeaveRuts(now);

        Show(Pick(moving));
        Order();
        Transform target = walker != null ? walker.target : ai.target;
        if (target != null)
        {
            float dx = target.position.x - now.x;
            if (Mathf.Abs(dx) > 0.15f) look.flipX = dx < 0f;   // the painted rope leaves to its right
        }
    }

    Sprite Pick(bool moving)
    {
        if (health != null && health.Blinking) return hit;
        if (ai.Aiming) return aim;
        if (ai.hook != null && ai.hook.IsBusy) return reel;
        if (!moving) return lurk;
        switch (rollStep & 3) { case 0: return rockA; case 2: return rockB; default: return lurk; }
    }

    void Show(Sprite sprite)
    {
        if (sprite == null || shown == sprite) return;
        shown = sprite;
        look.sprite = sprite;
    }

    void Order()
    {
        int ropeOrder = rope != null ? rope.sortingOrder : 2;
        look.sortingOrder = ropeOrder + (ai.hook != null && ai.hook.IsBusy ? BehindRope : FrontOfRope);
        // the wheels sit behind the tub: only their lower halves show under it
        if (wheelL != null) wheelL.sortingOrder = look.sortingOrder - 1;
        if (wheelR != null) wheelR.sortingOrder = look.sortingOrder - 1;
    }

    // Twin dashed ruts pressed into the floor where the wheels rolled; they fade in held steps
    void LeaveRuts(Vector3 now)
    {
        if (rutArt == null || (now - rutFrom).sqrMagnitude < rutEvery * rutEvery) return;
        Vector3 dir = (now - rutFrom).normalized;
        rutFrom = now;
        if (rutRoot == null) rutRoot = new GameObject(name + " ruts").transform;   // not a child: ruts stay where they were pressed
        float gauge = WheelHub.x * Mathf.Abs(transform.lossyScale.x);
        Vector3 across = new Vector3(-dir.y, dir.x, 0f) * gauge;
        Vector3 ground = now + Vector3.up * (WheelHub.y * Mathf.Abs(transform.lossyScale.y)) * 0.6f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Stamp(ground + across, angle);
        Stamp(ground - across, angle);
    }

    void Stamp(Vector3 at, float angle)
    {
        Rut r;
        if (ruts.Count < MaxRuts)
        {
            var go = new GameObject("Rut");
            go.transform.SetParent(rutRoot, false);
            r = new Rut { sr = go.AddComponent<SpriteRenderer>() };
            r.sr.sprite = rutArt;
            r.sr.sortingOrder = -45;   // pressed into the floor, under treasure, characters and the cart
            ruts.Add(r);
            rutNext = ruts.Count - 1;
        }
        else rutNext = (rutNext + 1) % ruts.Count;
        r = ruts[rutNext];
        r.sr.transform.position = at;
        r.sr.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        Vector2 size = rutArt.bounds.size;
        r.sr.transform.localScale = new Vector3(0.26f / Mathf.Max(0.001f, size.x), 0.09f / Mathf.Max(0.001f, size.y), 1f);
        r.sr.color = RutColor;
        r.sr.enabled = true;
        r.born = Time.time;
        ruts[rutNext] = r;
    }

    void FadeRuts()
    {
        for (int i = 0; i < ruts.Count; i++)
        {
            Rut r = ruts[i];
            if (r.sr == null || !r.sr.enabled) continue;
            float age = StopMotion.Snap(Time.time - r.born, StopMotion.Pose) / Mathf.Max(0.01f, rutLife);
            if (age >= 1f) { r.sr.enabled = false; continue; }
            Color c = RutColor;
            c.a *= age < 0.5f ? 1f : 1f - (age - 0.5f) / 0.5f;
            r.sr.color = c;
        }
    }
}
