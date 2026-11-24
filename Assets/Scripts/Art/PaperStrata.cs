using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the keyframe's torn-paper sandstone strata (Resources/Art/Tiles/Strata, painted with Meshy from the keyframe: rust, ochre, sand and umber layers with
// white torn-fibre lips, tiling sideways) frame the bottom and top edges of the view in battle levels
// (not the hub, which keeps its dark dungeon floor, nor Survival),
// so every level reads as a cut through the mine.
//   - Floor layer only: drawn just above the felt floor and below the set props, lamplight and every game
//     piece, so it can never hide a hook, rock, treasure or character.
//   - Follows the camera (Survival's camera moves), sized to the view; nothing else is touched.
// Floor layer order: floor MinValue, strata +1, props +2 (PaperSet), lamplight +3 (PaperLamps).
public class PaperStrata : MonoBehaviour
{
    public const int Order = short.MinValue + 1;
    const float Band = 0.16f;   // share of the view height each band takes: deep like the keyframe, clear of the hub's corridors
    static readonly Color Tone = new Color(0.86f, 0.84f, 0.82f);   // a touch dimmer than the pieces
    static PaperStrata instance;
    Sprite tile;
    SpriteRenderer bottom, top;
    LevelLoader level;
    SurvivalLoader survival;
    float nextLook;

    LevelLoader FindLevel()
    {
        if (Time.unscaledTime >= nextLook)
        {
            nextLook = Time.unscaledTime + 0.5f;
            level = FindAnyObjectByType<LevelLoader>();
            survival = FindAnyObjectByType<SurvivalLoader>();
        }
        return level;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Strata");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperStrata>();
    }

    void Start()
    {
        tile = ArtLibrary.Get("Tiles/Strata");
        if (tile == null) { enabled = false; return; }
        bottom = Make("Strata Bottom");
        top = Make("Strata Top");
    }

    SpriteRenderer Make(string bandName)
    {
        var go = new GameObject(bandName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = tile;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.color = Tone;
        sr.sortingOrder = Order;
        return sr;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        // battle levels only: the hub keeps its plain dark dungeon floor, Survival its open felt mat
        bool show = cam != null && cam.orthographic && GameManager.Instance != null && MapHub.Active == null
                    && FindLevel() != null && survival == null;
        bottom.enabled = top.enabled = show;
        if (!show) return;
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector2 art = tile.bounds.size;                 // one tile: 8 x 2 units at 128 px per unit
        float scale = 2f * h * Band / art.y;            // band height = Band of the view
        Vector2 size = new Vector2((2f * w + 2f) / scale, art.y);
        Vector3 c = cam.transform.position;
        Place(bottom, new Vector3(c.x, c.y - h + art.y * scale * 0.5f, 0f), 0f, scale, size);
        Place(top, new Vector3(c.x, c.y + h - art.y * scale * 0.5f, 0f), 180f, scale, size);   // torn edge faces down
    }

    static void Place(SpriteRenderer band, Vector3 at, float angle, float scale, Vector2 size)
    {
        band.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, angle));
        band.transform.localScale = new Vector3(scale, scale, 1f);
        band.size = size;
    }
}
