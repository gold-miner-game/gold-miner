using System.Collections.Generic;
using UnityEngine;

// Art: small papercraft effects that only listen to gameplay events (never change them).
//   - Treasure eaten: gold-leaf (or blue gem) flakes burst out, a paper ring pops, a warm flash
//     the bloom picks up.
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
        public bool glint;       // stepped 4-frame star (stop-motion), no smooth motion
        public float size;
    }

    static readonly float[] GlintFrames = { 0.45f, 1f, 0.7f, 0.3f };   // scale per 1/12 s frame

    // A paper star glint on gold or a gem, popped over four stop-motion frames.
    public static void Glint(Vector3 at, float size, bool gem, int sortingOrder)
    {
        if (instance == null || instance.sparkle == null) return;
        Color tint = gem ? new Color(0.85f, 0.97f, 1f) : new Color(1f, 0.97f, 0.8f);
        SpriteRenderer look = instance.pool.Count > 0 ? instance.pool.Pop() : instance.NewLook();
        look.gameObject.SetActive(true);
        look.sprite = instance.sparkle;
        look.sortingOrder = sortingOrder;
        look.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, Random.Range(0, 2) * 45f));
        look.transform.localScale = Vector3.one * size * GlintFrames[0];
        look.color = tint;
        instance.live.Add(new Bit { look = look, life = GlintFrames.Length / 12f, color = tint, glint = true, size = size });
    }

    static PaperFx instance;
    readonly List<Bit> live = new List<Bit>();
    readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    Sprite flake, disc, ring, sparkle;

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
    }

    void OnEaten(Treasure treasure)
    {
        if (treasure == null || flake == null) return;
        Vector3 at = treasure.transform.position;
        float size = Mathf.Clamp(treasure.transform.lossyScale.x, 0.3f, 1.5f);
        Color[] palette = treasure.type == TreasureType.Diamond ? GemLeaf : GoldLeaf;
        int count = Mathf.RoundToInt(Mathf.Lerp(8, 18, Mathf.InverseLerp(0.3f, 1.2f, size)));
        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float speed = Random.Range(1.6f, 3.4f) * (0.6f + size * 0.5f);
            Spawn(flake, at, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * speed, Random.Range(-720f, 720f),
                Random.Range(0.35f, 0.6f), Random.Range(0.09f, 0.16f) * (0.7f + size * 0.4f), 0f,
                palette[Random.Range(0, palette.Length)], false);
        }
        if (ring != null) Spawn(ring, at, Vector3.zero, 0f, 0.3f, size * 0.8f, size * 3.2f, new Color(1f, 0.93f, 0.7f, 0.9f), true);
        if (disc != null) Spawn(disc, at, Vector3.zero, 0f, 0.22f, size * 1.6f, size * 1.5f, new Color(1f, 0.85f, 0.4f, 0.55f), true);
    }

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
            if (b.glint)
            {
                int frame = Mathf.Min(GlintFrames.Length - 1, Mathf.FloorToInt(b.age * 12f));
                tr.localScale = Vector3.one * b.size * GlintFrames[frame];
            }
            else if (b.ring)
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
