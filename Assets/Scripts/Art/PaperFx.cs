using System.Collections.Generic;
using UnityEngine;

// Art: small papercraft effects that only listen to gameplay events (never change them).
//   - Treasure eaten: gold-leaf (or blue gem) flakes burst out, a paper ring pops, a warm flash
//     the bloom picks up.
//   - Life picked up: felt-heart scraps; treasure reeled in: dust puff; rock smashed: paper crumbs
//     (PaperShadows notices those objects disappearing mid-scene).
// Flakes are plain sprites moved here; nothing is spawned under gameplay objects.
public class PaperFx : MonoBehaviour
{
    static readonly Color[] GoldLeaf = { new Color(1f, 0.86f, 0.3f), new Color(0.95f, 0.7f, 0.15f), new Color(1f, 0.95f, 0.65f) };
    static readonly Color[] GemLeaf = { new Color(0.6f, 0.88f, 1f), new Color(0.85f, 0.97f, 1f), new Color(0.45f, 0.7f, 0.95f) };

    class Bit
    {
        public SpriteRenderer look;
        public Vector3 velocity;
        public float spin, age, life, grow;
        public Color color;
        public bool ring;
    }

    static PaperFx instance;
    readonly List<Bit> live = new List<Bit>();
    readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    Sprite flake, disc, ring;

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
    }

    static readonly Color[] HeartFelt = { new Color(0.95f, 0.25f, 0.3f), new Color(1f, 0.55f, 0.65f), new Color(1f, 0.92f, 0.9f) };
    static readonly Color[] Dust = { new Color(0.55f, 0.42f, 0.32f), new Color(0.42f, 0.32f, 0.24f), new Color(0.68f, 0.58f, 0.48f) };
    static readonly Color[] StoneCrumbs = { new Color(0.62f, 0.6f, 0.58f), new Color(0.45f, 0.43f, 0.42f), new Color(0.78f, 0.76f, 0.72f) };
    public readonly HashSet<int> eaten = new HashSet<int>();   // treasure the player ate (not reeled in)

    void OnEaten(Treasure treasure)
    {
        if (treasure == null) return;
        eaten.Add(treasure.GetInstanceID());
        float size = Mathf.Clamp(treasure.transform.lossyScale.x, 0.3f, 1.5f);
        Burst(treasure.transform.position, size, treasure.type == TreasureType.Diamond ? GemLeaf : GoldLeaf,
            new Color(1f, 0.93f, 0.7f, 0.9f), new Color(1f, 0.85f, 0.4f, 0.55f));
    }

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
            float angle = Random.value * Mathf.PI * 2f;
            float speed = Random.Range(1.6f, 3.4f) * (0.6f + size * 0.5f);
            Spawn(flake, at, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * speed, Random.Range(-720f, 720f),
                Random.Range(0.35f, 0.6f), Random.Range(0.09f, 0.16f) * (0.7f + size * 0.4f), 0f,
                palette[Random.Range(0, palette.Length)], false);
        }
        if (ring != null) Spawn(ring, at, Vector3.zero, 0f, 0.3f, size * 0.8f, size * 3.2f, ringColor, true);
        if (disc != null && glow.HasValue) Spawn(disc, at, Vector3.zero, 0f, 0.22f, size * 1.6f, size * 1.5f, glow.Value, true);
    }

    public static bool WasEaten(int treasureId) => instance != null && instance.eaten.Remove(treasureId);

    void Spawn(Sprite sprite, Vector3 at, Vector3 velocity, float spin, float life, float scale, float grow, Color color, bool isRing)
    {
        SpriteRenderer look = pool.Count > 0 ? pool.Pop() : NewLook();
        look.gameObject.SetActive(true);
        look.sprite = sprite;
        look.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, Random.value * 360f));
        look.transform.localScale = Vector3.one * scale;
        look.color = color;
        look.sortingOrder = 400;
        live.Add(new Bit { look = look, velocity = velocity, spin = spin, life = life, grow = grow, color = color, ring = isRing });
    }

    SpriteRenderer NewLook()
    {
        var go = new GameObject("Fx");
        go.transform.SetParent(transform, false);
        var look = go.AddComponent<SpriteRenderer>();
        look.sortingOrder = 400;
        return look;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            Bit b = live[i];
            b.age += dt;
            float t = b.age / b.life;
            if (t >= 1f || b.look == null)
            {
                if (b.look != null) { b.look.gameObject.SetActive(false); pool.Push(b.look); }
                live.RemoveAt(i);
                continue;
            }
            Transform tr = b.look.transform;
            if (b.ring)
            {
                tr.localScale += Vector3.one * b.grow * dt;
                b.look.color = new Color(b.color.r, b.color.g, b.color.b, b.color.a * (1f - t) * (1f - t));
            }
            else
            {
                b.velocity *= Mathf.Exp(-5f * dt);
                tr.position += b.velocity * dt;
                tr.Rotate(0f, 0f, b.spin * dt);
                // paper flakes flutter: they flip edge-on and back as they spin
                float flutter = Mathf.Abs(Mathf.Cos(b.age * 14f + b.spin));
                tr.localScale = new Vector3(tr.localScale.y * Mathf.Max(0.25f, flutter), tr.localScale.y, 1f);
                b.look.color = new Color(b.color.r, b.color.g, b.color.b, 1f - t * t);
            }
        }
    }
}
