using System.Collections.Generic;
using UnityEngine;

// Art: soft cut-paper drop shadows under rocks, walls, treasure, Lives and characters, so flat
// shapes read as layered paper on the mine floor. Also notices pieces vanishing mid-scene (Life
// picked up, treasure reeled in, rock smashed) for PaperFx, and runs the treasure boil: registered
// surface frames (ArtLibrary.BoilFrames) swapped on threes, never moving the cut-out; frozen holds.
// Read-only toward gameplay: shadows live under their own root (never children of gameplay objects,
// so GetComponentsInChildren, bounds and frost tints never see them).
[DefaultExecutionOrder(2000)]
public class PaperShadows : MonoBehaviour
{
    static readonly Vector3 Offset = new Vector3(0.05f, -0.075f, 0f);   // light from the top left
    static readonly Color Shade = new Color(0.03f, 0.015f, 0.005f, 0.55f);
    static readonly Color Blob = new Color(0.03f, 0.015f, 0.005f, 0.4f);
    const float ScanEvery = 0.4f;

    class Piece
    {
        public SpriteRenderer caster, look;
        public bool blob;
        public Kind kind;
        public int id;             // the caster's GameObject (treasure id for the eaten check)
        public Vector3 lastAt;     // where it was last seen, for the effect when it disappears
        public float lastSize;
        public Treasure treasure;   // boil holds still while it is frozen
        public Sprite[] boil;       // registered boil frames; null = none
        public int seed;
        public Sprite[] hang;       // treasure hang poses (NAME_h1, NAME_h2) while a claw holds it
        public bool hangLooked;
    }

    enum Kind { Rock, Treasure, Life, Character }
    static int sceneChangeFrame = -10;   // objects vanishing around a scene change are not events

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
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += s => sceneChangeFrame = Time.frameCount;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => sceneChangeFrame = Time.frameCount;
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + ScanEvery;
        foreach (Treasure t in Treasure.Active) Cut(t.GetComponent<SpriteRenderer>(), t, Kind.Treasure);
        foreach (Rock r in FindObjectsByType<Rock>(FindObjectsSortMode.None)) Cut(r.GetComponent<SpriteRenderer>(), null, Kind.Rock);
        // map portals: hidden ones are disguised as rocks (PaperPortalStones), so they cast and boil like rocks
        foreach (HubPortal h in FindObjectsByType<HubPortal>(FindObjectsSortMode.None)) Cut(h.GetComponent<SpriteRenderer>(), null, Kind.Rock);
        foreach (LifePickup l in FindObjectsByType<LifePickup>(FindObjectsSortMode.None)) Cut(l.GetComponent<SpriteRenderer>(), null, Kind.Life);
        foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None)) Pool(p);
    }

    // A same-shape shadow, offset down-right: rocks, walls, gold, gems, hearts.
    void Cut(SpriteRenderer caster, Treasure treasure, Kind kind)
    {
        if (caster == null || !known.Add(caster.GetInstanceID())) return;
        pieces.Add(new Piece
        {
            caster = caster,
            kind = kind,
            id = treasure != null ? treasure.GetInstanceID() : 0,
            lastAt = caster.transform.position,
            lastSize = caster.transform.lossyScale.x,
            look = Make(caster, caster.sprite, caster.sortingOrder - 1),
            treasure = treasure,
            boil = ArtLibrary.BoilFrames(caster.sprite),
            seed = caster.GetInstanceID() & 0xff,
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
        pieces.Add(new Piece { caster = body, blob = true, kind = Kind.Character, look = Make(body, disc, lowest - 1) });
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
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            Piece s = pieces[i];
            if (s.caster == null)
            {
                if (s.look != null) Destroy(s.look.gameObject);
                pieces.RemoveAt(i);
                Vanished(s);
                continue;
            }
            bool visible = s.caster.enabled && s.caster.gameObject.activeInHierarchy;
            if (s.look.enabled != visible) s.look.enabled = visible;
            if (!visible) continue;
            if (s.blob) { PlaceBlob(s); continue; }
            Boil(s);
            s.lastAt = s.caster.transform.position;
            s.lastSize = s.caster.transform.lossyScale.x;
            Transform t = s.look.transform, c = s.caster.transform;
            t.SetPositionAndRotation(c.position + Offset, c.rotation);
            t.localScale = c.lossyScale;
            if (s.look.sprite != s.caster.sprite) s.look.sprite = s.caster.sprite;
            s.look.flipX = s.caster.flipX;
            s.look.flipY = s.caster.flipY;
            s.look.color = new Color(Shade.r, Shade.g, Shade.b, Shade.a * s.caster.color.a);
        }
    }

    // On threes; a 0-2-1 cycle per piece, staggered, so the pile never pulses in unison. Only the
    // sprite is swapped (same outline): transforms, colliders and colours stay as gameplay set them.
    // A treasure in a claw hangs and swings from the grip in held poses on twos (+4, +4, +4, rest, -3, -3,
    // -3, rest: about 1.5 swings a second), the handled-prop wobble of stop-motion. The poses are pre-drawn
    // on the same full-rect canvas, so the sprite bounds Hook.HoldInClaw reads never change.
    static readonly int[] HangCycle = { 1, 1, 1, 0, 2, 2, 2, 0 };

    static bool Hang(Piece s, ref Sprite current)
    {
        if (s.treasure == null || s.boil == null) return false;
        if (!s.hangLooked)
        {
            s.hangLooked = true;
            Sprite h1 = ArtLibrary.Get("Objects/" + s.boil[0].name + "_h1"), h2 = ArtLibrary.Get("Objects/" + s.boil[0].name + "_h2");
            if (h1 != null && h2 != null) s.hang = new[] { h1, h2 };
        }
        if (s.hang == null) return false;
        bool held = Treasure.OnHooks.Contains(s.treasure) && !s.treasure.IsFrozen;
        if (held)
        {
            int pose = HangCycle[(StopMotion.Frame + s.seed) % HangCycle.Length];
            Sprite next = pose == 0 ? s.boil[0] : s.hang[pose - 1];
            if (current != next) s.caster.sprite = next;
            return true;
        }
        // let go (eaten, dropped, reeled home): back to the resting cut-out before the boil continues
        if (current == s.hang[0] || current == s.hang[1]) { s.caster.sprite = s.boil[0]; current = s.boil[0]; }
        return false;
    }

    static void Boil(Piece s)
    {
        Sprite current = s.caster.sprite;
        if (Hang(s, ref current)) return;
        if (s.boil == null || (current != s.boil[0] && current != s.boil[1] && current != s.boil[2]))
        {
            // A new sprite: boil it only if it is painted art with registered frames (e.g. PaperRocks'
            // stones replacing the placeholder hexagon). Anything else code set is left alone.
            Sprite[] frames = ArtLibrary.BoilFrames(current);
            if (frames == null) return;
            s.boil = frames;
        }
        if (s.treasure != null && s.treasure.IsFrozen) return;
        if (FrostField2D.IsFrozen(s.caster)) return;   // frost covers this exact sprite: hold it still
        Sprite next = s.boil[(StopMotion.BoilFrame * 2 + s.seed) % 3];
        if (current != next) s.caster.sprite = next;
    }

    // An object was destroyed mid-scene: a Life was picked up, treasure reeled into a hole (eaten
    // treasure already burst in PaperFx), or a rock smashed.
    static void Vanished(Piece s)
    {
        if (Time.frameCount - sceneChangeFrame <= 2) return;
        float size = Mathf.Clamp(s.lastSize, 0.3f, 1.5f);
        switch (s.kind)
        {
            case Kind.Life: PaperFx.HeartPop(s.lastAt, size); break;
            case Kind.Treasure: if (!PaperFx.WasEaten(s.id)) PaperFx.Puff(s.lastAt, size); break;
            case Kind.Rock: PaperFx.Crumbs(s.lastAt, size); break;
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
