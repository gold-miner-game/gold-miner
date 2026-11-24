using System.Collections.Generic;
using UnityEngine;

// Art: small papercraft effects that only listen to gameplay events (never change them).
//   - Treasure eaten: gold-leaf (or blue gem) flakes burst out, a paper ring pops, a warm flash
//     the bloom picks up.
//   - Life picked up: felt-heart scraps; treasure reeled in: dust puff; rock smashed: paper crumbs
//     (PaperShadows notices those objects disappearing mid-scene).
//   - Light catch: every few seconds one visible treasure catches the light: a paper star pops
//     small, full, held, half, gone.
// Stop-motion rules (docs/art-style.md): the bits simulate smoothly but are only re-posed on twos
// (StopMotion.Frame), so they move like cut-outs shot frame by frame. Scaled time: pause = still.
// Flakes are plain sprites under this object; nothing is spawned under gameplay objects.
public class PaperFx : MonoBehaviour
{
    static readonly Color[] GoldLeaf = { new Color(1f, 0.86f, 0.3f), new Color(0.95f, 0.7f, 0.15f), new Color(1f, 0.95f, 0.65f) };
    static readonly Color[] GemLeaf = { new Color(0.6f, 0.88f, 1f), new Color(0.85f, 0.97f, 1f), new Color(0.45f, 0.7f, 0.95f) };
    static readonly Color[] HeartFelt = { new Color(0.95f, 0.25f, 0.3f), new Color(1f, 0.55f, 0.65f), new Color(1f, 0.92f, 0.9f) };
    static readonly Color[] Dust = { new Color(0.55f, 0.42f, 0.32f), new Color(0.42f, 0.32f, 0.24f), new Color(0.68f, 0.58f, 0.48f) };
    static readonly Color[] StoneCrumbs = { new Color(0.62f, 0.6f, 0.58f), new Color(0.45f, 0.43f, 0.42f), new Color(0.78f, 0.76f, 0.72f) };
    static readonly float[] CatchPoses = { 0.35f, 1f, 1f, 0.55f };   // one scale per pose on twos

    enum Kind { Flake, Ring, Catch, Strip }

    // A running character leaves thin cream paper strips along its path (Swift).
    public static void Strip(Vector3 at, Vector2 velocity)
    {
        if (instance == null || instance.flake == null) return;
        Bit b = instance.Spawn(Kind.Strip, instance.flake, at, Vector3.zero, 0f, 0.3f, 2.2f, 0f, new Color(0.97f, 0.9f, 0.74f, 0.8f), 3);
        b.angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        instance.Pose(b);
    }

    class Bit
    {
        public SpriteRenderer look;
        public Kind kind;
        public Vector3 at, velocity;
        public float angle, spin, age, life, size, grow;
        public Color color;
        public int bornFrame;
    }

    static PaperFx instance;
    readonly List<Bit> live = new List<Bit>();
    readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    readonly HashSet<int> eaten = new HashSet<int>();   // treasure the player ate (not reeled in)
    Sprite flake, disc, ring, sparkle;
    int shownFrame = -1;
    float nextCatch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Fx");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperFx>();
    }

    void OnEnable() { Treasure.Eaten += OnEaten; }
    void OnDisable() { Treasure.Eaten -= OnEaten; }

    void Start()
    {
        flake = ArtLibrary.Get("Fx/Flake");
        disc = ArtLibrary.Get("Fx/SoftShadow");
        ring = ArtLibrary.Get("Fx/Ring");
        sparkle = ArtLibrary.Get("Fx/Sparkle");
        nextCatch = Time.time + 2f;
    }

    void OnEaten(Treasure treasure)
    {
        if (treasure == null) return;
        eaten.Add(treasure.GetInstanceID());
        float size = Mathf.Clamp(treasure.transform.lossyScale.x, 0.3f, 1.5f);
        Burst(treasure.transform.position, size, treasure.type == TreasureType.Diamond ? GemLeaf : GoldLeaf,
            new Color(1f, 0.93f, 0.7f, 0.9f), new Color(1f, 0.85f, 0.4f, 0.55f));
    }

    public static bool WasEaten(int treasureId) => instance != null && instance.eaten.Remove(treasureId);

    // A Life was picked up: felt-heart scraps and a pink pop.
    public static void HeartPop(Vector3 at, float size)
    {
        if (instance != null) instance.Burst(at, size, HeartFelt, new Color(1f, 0.6f, 0.7f, 0.9f), new Color(1f, 0.4f, 0.5f, 0.45f));
    }

    // Treasure reeled into a hole by a miner: a small dusty puff, no glow.
    public static void Puff(Vector3 at, float size)
    {
        if (instance != null) instance.Burst(at, size * 0.7f, Dust, new Color(0.6f, 0.48f, 0.36f, 0.6f), null, 0.6f);
    }

    // A rock smashed: grey paper crumbs and dust on top of the rock's own pieces.
    public static void Crumbs(Vector3 at, float size)
    {
        if (instance != null) instance.Burst(at, size, StoneCrumbs, new Color(0.55f, 0.45f, 0.36f, 0.5f), null, 0.8f);
    }

    void Burst(Vector3 at, float size, Color[] palette, Color ringColor, Color? glow, float amount = 1f)
    {
        if (flake == null) return;
        int count = Mathf.RoundToInt(Mathf.Lerp(8, 18, Mathf.InverseLerp(0.3f, 1.2f, size)) * amount);
        for (int i = 0; i < count; i++)
        {
            float a = ArtLibrary.Value * Mathf.PI * 2f;
            float speed = ArtLibrary.Range(1.6f, 3.4f) * (0.6f + size * 0.5f);
            Spawn(Kind.Flake, flake, at, new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * speed, ArtLibrary.Range(-720f, 720f),
                ArtLibrary.Range(0.4f, 0.65f), ArtLibrary.Range(0.09f, 0.16f) * (0.7f + size * 0.4f), 0f,
                palette[ArtLibrary.Range(0, palette.Length)], 400);
        }
        if (ring != null) Spawn(Kind.Ring, ring, at, Vector3.zero, 0f, 0.33f, size * 0.8f, size * 3.2f, ringColor, 400);
        if (disc != null && glow.HasValue) Spawn(Kind.Ring, disc, at, Vector3.zero, 0f, 0.25f, size * 1.6f, size * 1.5f, glow.Value, 400);
    }

    Bit Spawn(Kind kind, Sprite sprite, Vector3 at, Vector3 velocity, float spin, float life, float size, float grow, Color color, int order)
    {
        SpriteRenderer look = pool.Count > 0 ? pool.Pop() : NewLook();
        look.gameObject.SetActive(true);
        look.sprite = sprite;
        look.sortingOrder = order;
        var b = new Bit
        {
            look = look, kind = kind, at = at, velocity = velocity, spin = spin, life = life, size = size,
            grow = grow, color = color, angle = ArtLibrary.Value * 360f, bornFrame = StopMotion.Frame,
        };
        live.Add(b);
        Pose(b);   // the first pose shows at once
        return b;
    }

    SpriteRenderer NewLook()
    {
        var go = new GameObject("Fx");
        go.transform.SetParent(transform, false);
        return go.AddComponent<SpriteRenderer>();
    }

    void Update()
    {
        CatchLight();
        float dt = Time.deltaTime;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            Bit b = live[i];
            b.age += dt;
            if (b.age >= b.life || b.look == null)
            {
                if (b.look != null) { b.look.gameObject.SetActive(false); pool.Push(b.look); }
                live.RemoveAt(i);
                continue;
            }
            if (b.kind == Kind.Flake)
            {
                b.velocity *= Mathf.Exp(-5f * dt);
                b.at += b.velocity * dt;
                b.angle += b.spin * dt;
            }
        }
        if (StopMotion.Frame == shownFrame) return;   // hold the pose until the next twos
        shownFrame = StopMotion.Frame;
        foreach (Bit b in live) Pose(b);
    }

    void Pose(Bit b)
    {
        if (b.look == null) return;
        Transform t = b.look.transform;
        float k = Mathf.Clamp01(b.age / b.life);
        switch (b.kind)
        {
            case Kind.Flake:
                // paper flakes flutter: edge-on and back as they spin
                float flutter = Mathf.Max(0.25f, Mathf.Abs(Mathf.Cos(b.age * 14f + b.spin)));
                t.SetPositionAndRotation(b.at, Quaternion.Euler(0f, 0f, b.angle));
                t.localScale = new Vector3(b.size * flutter, b.size, 1f);
                b.look.color = new Color(b.color.r, b.color.g, b.color.b, 1f - k * k);
                break;
            case Kind.Ring:
                t.SetPositionAndRotation(b.at, Quaternion.identity);
                t.localScale = Vector3.one * (b.size + b.grow * b.age);
                b.look.color = new Color(b.color.r, b.color.g, b.color.b, b.color.a * (1f - k) * (1f - k));
                break;
            case Kind.Strip:
                t.SetPositionAndRotation(b.at, Quaternion.Euler(0f, 0f, b.angle));
                t.localScale = new Vector3(b.size * (1f - 0.5f * k), 0.16f, 1f);   // flake art is 0.25 units: a long thin strip
                b.look.color = new Color(b.color.r, b.color.g, b.color.b, b.color.a * (1f - k));
                break;
            case Kind.Catch:
                int pose = Mathf.Clamp(StopMotion.Frame - b.bornFrame, 0, CatchPoses.Length - 1);
                t.SetPositionAndRotation(b.at, Quaternion.Euler(0f, 0f, b.angle));
                t.localScale = Vector3.one * b.size * CatchPoses[pose];
                b.look.color = b.color;
                break;
        }
    }

    // One treasure at a time catches the light, every 2.5-4.5 s: a deliberate accent, not sparkle noise.
    void CatchLight()
    {
        if (sparkle == null || Time.time < nextCatch || Treasure.Active.Count == 0) return;
        nextCatch = Time.time + ArtLibrary.Range(2.5f, 4.5f);
        Camera cam = Camera.main;
        for (int tries = 0; tries < 6; tries++)
        {
            Treasure t = Treasure.Active[ArtLibrary.Range(0, Treasure.Active.Count)];
            if (t == null || t.IsFrozen) continue;
            SpriteRenderer look = t.GetComponent<SpriteRenderer>();
            if (look == null || !look.isVisible) continue;
            if (cam != null && !cam.pixelRect.Contains(cam.WorldToScreenPoint(look.bounds.center))) continue;
            Bounds b = look.bounds;
            Vector3 at = b.center + new Vector3(b.extents.x * ArtLibrary.Range(-0.45f, 0.2f), b.extents.y * ArtLibrary.Range(0.1f, 0.5f), 0f);
            Color tint = t.type == TreasureType.Diamond ? new Color(0.88f, 0.97f, 1f) : new Color(1f, 0.97f, 0.82f);
            Bit c = Spawn(Kind.Catch, sparkle, at, Vector3.zero, 0f, CatchPoses.Length / StopMotion.Pose, b.size.y * 0.6f, 0f, tint, look.sortingOrder + 1);
            c.angle = ArtLibrary.Range(0, 2) * 45f;
            Pose(c);
            return;
        }
    }
}
