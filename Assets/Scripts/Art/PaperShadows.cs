using System.Collections.Generic;
using UnityEngine;

// Art: the papercraft "stage" for play pieces.
//  - Soft cut-paper drop shadows under rocks, walls, treasure, Lives and characters, so flat shapes
//    read as layered paper on the mine floor.
//  - Stop-motion boil: treasure and Lives cycle through hand-shifted redraws of their sprite at
//    ~10 fps (ArtLibrary.BoilFrames), like cutouts re-posed between frames. Only the sprite is
//    swapped: transforms, colliders and renderer state stay exactly as gameplay set them, so frost
//    and everything else still see the object. Frozen treasure holds still.
//  - Gold and gems catch an occasional stepped glint (PaperFx.Glint).
// Read-only toward gameplay: shadows live under their own root (never children of gameplay objects,
// so GetComponentsInChildren, bounds and frost tints never see them).
[DefaultExecutionOrder(2000)]
public class PaperShadows : MonoBehaviour
{
    static readonly Vector3 Offset = new Vector3(0.05f, -0.075f, 0f);   // light from the top left
    static readonly Color Shade = new Color(0.03f, 0.015f, 0.005f, 0.55f);
    static readonly Color Blob = new Color(0.03f, 0.015f, 0.005f, 0.4f);
    const float ScanEvery = 0.4f;
    const float BoilFps = 10f;

    class Piece
    {
        public SpriteRenderer caster, look;
        public bool blob;
        public Treasure treasure;      // boil pauses while it is frozen
        public Sprite[] frames;        // stop-motion boil frames; null = no boil
        public int seed;
        public float nextGlint;
    }

    static PaperShadows instance;
    readonly List<Piece> pieces = new List<Piece>();
    readonly HashSet<int> known = new HashSet<int>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Shadows");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperShadows>();
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + ScanEvery;
        foreach (Treasure t in Treasure.Active) Cut(t.GetComponent<SpriteRenderer>(), t);
        foreach (Rock r in FindObjectsByType<Rock>(FindObjectsSortMode.None)) Cut(r.GetComponent<SpriteRenderer>(), null);
        foreach (LifePickup l in FindObjectsByType<LifePickup>(FindObjectsSortMode.None)) Cut(l.GetComponent<SpriteRenderer>(), null);
        foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None)) Pool(p);
    }

    // A same-shape shadow, offset down-right: rocks, walls, gold, gems, hearts.
    void Cut(SpriteRenderer caster, Treasure treasure)
    {
        if (caster == null || !known.Add(caster.GetInstanceID())) return;
        int seed = caster.GetInstanceID() & 0x7fff;
        pieces.Add(new Piece
        {
            caster = caster,
            look = Make(caster, caster.sprite, caster.sortingOrder - 1),
            treasure = treasure,
            frames = ArtLibrary.BoilFrames(caster.sprite),
            seed = seed,
            nextGlint = Time.time + 1f + (seed % 400) / 100f,
        });
    }

    // A soft ellipse under a character built from many parts (one shadow, not one per part).
    void Pool(PlayerCharacter player)
    {
        SpriteRenderer body = player.body != null ? player.body : player.GetComponentInChildren<SpriteRenderer>();
        if (body == null || !known.Add(body.GetInstanceID())) return;
        int lowest = body.sortingOrder;
        foreach (SpriteRenderer part in player.GetComponentsInChildren<SpriteRenderer>(true))
            lowest = Mathf.Min(lowest, part.sortingOrder);
        Sprite disc = ArtLibrary.Get("Fx/SoftShadow");
        if (disc == null) return;
        pieces.Add(new Piece { caster = body, blob = true, look = Make(body, disc, lowest - 1) });
    }

    SpriteRenderer Make(SpriteRenderer caster, Sprite sprite, int order)
    {
        var go = new GameObject("Shadow");
        go.transform.SetParent(transform, false);
        var look = go.AddComponent<SpriteRenderer>();
        look.sprite = sprite;
        look.sharedMaterial = caster.sharedMaterial;
        look.sortingLayerID = caster.sortingLayerID;
        look.sortingOrder = order;
        return look;
    }

    void LateUpdate()
    {
        int step = Mathf.FloorToInt(Time.time * BoilFps);
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            Piece s = pieces[i];
            if (s.caster == null)
            {
                if (s.look != null) Destroy(s.look.gameObject);
                pieces.RemoveAt(i);
                continue;
            }
            bool visible = s.caster.enabled && s.caster.gameObject.activeInHierarchy;
            if (s.look.enabled != visible) s.look.enabled = visible;
            if (!visible) continue;
            if (s.blob) { PlaceBlob(s); continue; }
            Boil(s, step);
            Transform t = s.look.transform, c = s.caster.transform;
            t.SetPositionAndRotation(c.position + Offset, c.rotation);
            t.localScale = c.lossyScale;
            if (s.look.sprite != s.caster.sprite) s.look.sprite = s.caster.sprite;
            s.look.flipX = s.caster.flipX;
            s.look.flipY = s.caster.flipY;
            s.look.color = new Color(Shade.r, Shade.g, Shade.b, Shade.a * s.caster.color.a);
        }
    }

    void Boil(Piece s, int step)
    {
        if (s.frames == null) return;
        Sprite current = s.caster.sprite;
        if (current != s.frames[0] && current != s.frames[1] && current != s.frames[2]) return;   // code changed it: leave it
        if (s.treasure != null && s.treasure.IsFrozen) return;                                     // frozen: holds still
        // Staggered per object, and not a plain 1-2-3 cycle: hand-animated boil skips around
        int k = step + s.seed;
        Sprite next = s.frames[(k + (k / 3)) % 3];
        if (current != next) s.caster.sprite = next;

        if (s.treasure != null && Time.time >= s.nextGlint)
        {
            s.nextGlint = Time.time + 2.2f + ((s.seed * 7919 + step) % 300) / 100f;
            Bounds b = s.caster.bounds;
            Vector3 at = b.center + new Vector3(b.extents.x * Random.Range(-0.5f, 0.3f), b.extents.y * Random.Range(-0.1f, 0.5f), 0f);
            PaperFx.Glint(at, b.size.y * 0.55f, s.treasure.type == TreasureType.Diamond, s.caster.sortingOrder + 1);
        }
    }

    void PlaceBlob(Piece s)
    {
        Bounds b = s.caster.bounds;
        Transform t = s.look.transform;
        t.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + b.size.y * 0.18f, b.center.z) + Offset, Quaternion.identity);
        t.localScale = new Vector3(b.size.x * 1.05f, b.size.y * 0.42f, 1f);
        s.look.color = new Color(Blob.r, Blob.g, Blob.b, Blob.a * s.caster.color.a);
    }
}
