using UnityEngine;
using UnityEngine.SceneManagement;

// Art: rocks and walls as flat 2D cut-paper stones: two or three torn paper layers seen from straight
// above, like the set's other cut-outs (Resources/Art/Objects/Stone1..9, painted with Meshy using the
// cover and the character sheet as style references, cut with slice_sheet.py). Each has registered boil
// frames (Stone*_b1/_b2, boil_frames.py), so PaperShadows re-lays its paper surface on threes like the
// gold: a stop-motion stone, never a still image.
// The hexagon (Assets/Sprites/HexagonFlatTop) was only a placeholder for level design; every rock still
// drawn with it gets a stone, picked and mirrored from its position so a layout always looks the same.
//   - Gameplay untouched: only the SpriteRenderer's sprite and flipX change. Colliders (a circle on the
//     prefab), transforms and rigidbodies are separate. Nothing reads a rock's sprite bounds.
//   - Same meaning: the stones are light grey, so the renderer tint (green = rhino-proof, map area
//     colours) still colours them. Shards of a broken rock reuse its sprite, so they are stone too.
//   - Walls keep a small paper gap: only the drawing shrinks to fit the nearest rock's centre distance.
//     Walls only get the chunky stones (the long oval and slab, Stone4 and Stone8, are kept for loose rocks).
public class PaperRocks : MonoBehaviour
{
    const string Placeholder = "HexagonFlatTop";
    static readonly string[] Chunky = { "Stone1", "Stone2", "Stone3", "Stone5", "Stone6", "Stone7", "Stone9" };
    static readonly string[] Any = { "Stone1", "Stone2", "Stone3", "Stone4", "Stone5", "Stone6", "Stone7", "Stone8", "Stone9" };
    static PaperRocks instance;
    Sprite[] chunky, any;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Rocks");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperRocks>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    static Sprite[] Load(string[] names)
    {
        var sprites = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++)
            if ((sprites[i] = ArtLibrary.Get("Objects/" + names[i])) == null) return null;
        return sprites;
    }

    void Start()
    {
        chunky = Load(Chunky);
        any = Load(Any);
        if (chunky == null || any == null) enabled = false;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;   // rocks spawned later (levels build after loading) are picked up too
        foreach (Rock rock in FindObjectsByType<Rock>(FindObjectsSortMode.None))
        {
            SpriteRenderer look = rock.GetComponent<SpriteRenderer>();
            if (look == null || look.sprite == null || look.sprite.name.Replace(" ", "") != Placeholder) continue;   // the sheet calls it "Hexagon Flat Top"
            Vector2 p = rock.transform.position;
            int hash = Mathf.Abs(Mathf.RoundToInt(p.x * 7.13f) * 73856093 ^ Mathf.RoundToInt(p.y * 5.71f) * 19349663);
            float scale = Mathf.Abs(rock.transform.lossyScale.x);
            float nearest = NearestNeighbour(rock, p, scale, out bool hasNeighbour);
            Sprite[] pool = rock.isWall || hasNeighbour ? chunky : any;
            // The canvas is one unit wide; fit the ink, leaving gameplay's original size alone.
            float factor = scale > 0f && nearest <= 1.2f * scale ? Mathf.Min(1f, 0.96f * nearest / scale) : 1f;
            look.sprite = ArtLibrary.ScaledBoil(pool[hash % pool.Length], factor);
            look.flipX = (hash / 7 & 1) == 1;
        }
    }

    // One query supplies both the old chunky-pool choice and the drawing's nearest centre.
    static float NearestNeighbour(Rock rock, Vector2 at, float scale, out bool hasNeighbour)
    {
        // read-only physics query, once per rock (only placeholder rocks are looked at)
        float nearest = float.PositiveInfinity;
        hasNeighbour = false;
        foreach (Collider2D c in Physics2D.OverlapCircleAll(at, Mathf.Max(1.05f, 1.2f * scale)))
        {
            Rock other = c.GetComponentInParent<Rock>();
            if (other == null || other == rock) continue;
            // Keep the old 1.05-unit collider query's pool choice even when the size query is wider.
            if ((c.ClosestPoint(at) - at).sqrMagnitude <= 1.05f * 1.05f) hasNeighbour = true;
            nearest = Mathf.Min(nearest, Vector3.Distance(rock.transform.position, other.transform.position));
        }
        return nearest;
    }
}
