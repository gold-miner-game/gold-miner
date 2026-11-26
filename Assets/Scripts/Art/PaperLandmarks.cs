using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: every world-map portal gets a landmark that says what it is, instead of the anonymous violet
// hexagon and star: the camp shelter for Pick Character, a market stall for the Store, the record
// kiosk for the Music Store, the chalkboard school for the Tutorial, timber mine entrances for the
// levels, the log fort for Survival, the red-eyed lair for Level 4 (the pack), the stone ring for
// Level 5. Painted in the keyframe's cut-paper style (Resources/Art/Landmarks/*, tools/art/make_landmarks.py).
//   - Same contract as PaperPortalStones: HubPortal's Star renderer is the public signal (off = still a
//     disguised rock, on = revealing or revealed). While it is a rock nothing changes, so secrets hold.
//   - Once revealed, the star is switched off with forceRenderingOff (HubPortal never touches it) and
//     the landmark stands on the portal's own hexagon, which stays as a coloured plate in its original
//     size (violet open, blue cleared, dim locked), so the portals still pop off the brown floor. The
//     landmark is the portal's child: it grows with the reveal and breathes with an open portal.
//   - The portal's state still reads: locked (the hexagon's dim colour) = the landmark greyed;
//     cleared = a small blue paper star pinned to it; open = full colour, breathing.
//   - The landmark fits inside the hexagon, so the map keeps its honeycomb rhythm and the name tags stay
//     where HubPortal put them.
// No gameplay field is read or changed: colliders, unlocking and entering stay HubPortal's.
[DefaultExecutionOrder(31100)]   // after PaperPortalStones (31000)
public class PaperLandmarks : MonoBehaviour
{

    const float TileWidth = 0.82f;   // in map tiles: the landmark sits inside its hexagon, whose rim shows the state colour

    static PaperLandmarks instance;

    sealed class Look
    {
        public HubPortal portal;
        public SpriteRenderer hex, star, landmark, ribbon;
        public Transform label;
        public bool shown;
    }

    readonly List<Look> looks = new List<Look>();
    readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>();
    Sprite ribbonArt;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Landmarks");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperLandmarks>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start() { ribbonArt = ArtLibrary.Get("Fx/PaperStar"); }

    // Which landmark a node gets: by id first, then by its kind
    static string LandmarkFor(MapNode node)
    {
        switch (node.id)
        {
            case "role_pick": return "Camp";
            case "store_1": return "Market";
            case "music_store": return "MusicKiosk";
            case "battle_03": return "School";        // the Tutorial
            case "survival": return "Fort";
            case "battle_05": return "Lair";          // Level 4, the roam map: the pack's lair
            case "battle_07": return "StoneRing";     // Level 5, the ring only Hulk can smash
        }
        switch (node.type)
        {
            case NodeType.RolePick: return "Camp";
            case NodeType.Store: return "Market";
            case NodeType.Survival: return "Fort";
            default: return "Mine";                   // every other level: a mine entrance
        }
    }


    Sprite Art(string name)
    {
        if (!art.TryGetValue(name, out Sprite s)) { s = ArtLibrary.Get("Landmarks/" + name); art[name] = s; }
        return s;
    }

    void Scan()
    {
        nextScan = Time.unscaledTime + 0.5f;
        foreach (HubPortal portal in FindObjectsByType<HubPortal>(FindObjectsSortMode.None))
        {
            if (portal.Node == null || looks.Exists(l => l.portal == portal)) continue;
            Sprite sprite = Art(LandmarkFor(portal.Node));
            if (sprite == null) continue;   // no art: the hexagon stays
            Transform star = portal.transform.Find("Star");
            var look = new Look
            {
                portal = portal,
                hex = portal.GetComponent<SpriteRenderer>(),
                star = star != null ? star.GetComponent<SpriteRenderer>() : null,
                label = portal.transform.Find("Label"),
            };
            var go = new GameObject("Landmark");
            go.transform.SetParent(portal.transform, false);
            look.landmark = go.AddComponent<SpriteRenderer>();
            look.landmark.sprite = sprite;
            look.landmark.sortingOrder = 4;   // over the hexagon (3), under the name tag (5)
            look.landmark.enabled = false;
            if (ribbonArt != null)
            {
                var r = new GameObject("Cleared Star");
                r.transform.SetParent(go.transform, false);
                look.ribbon = r.AddComponent<SpriteRenderer>();
                look.ribbon.sprite = ribbonArt;
                look.ribbon.sortingOrder = 5;
                look.ribbon.enabled = false;
            }
            looks.Add(look);
        }
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan) Scan();
        for (int i = looks.Count - 1; i >= 0; i--)
        {
            Look l = looks[i];
            if (l.portal == null || l.landmark == null) { looks.RemoveAt(i); continue; }
            bool revealed = l.star != null && l.star.enabled;
            if (!revealed)
            {
                // still a rock (PaperPortalStones draws it): no landmark, the hexagon renderer stays on
                if (l.shown) { l.landmark.enabled = false; if (l.ribbon != null) l.ribbon.enabled = false; SetPortalArt(l, true); l.shown = false; }
                continue;
            }
            if (!l.shown)
            {
                // the first revealed frame: the landmark is one map tile across, the size the hexagon and the
                // wall rocks read at, so it sits in the honeycomb like the tile it replaces (no overlaps; the
                // name tags stay where they were). HubPortal scales the portal to tile x 0.95.
                Vector2 spriteSize = l.landmark.sprite.bounds.size;
                float local = TileWidth / 0.95f / Mathf.Max(0.001f, Mathf.Max(spriteSize.x, spriteSize.y));
                l.landmark.transform.localScale = Vector3.one * local;
                l.landmark.transform.localPosition = Vector3.zero;
                if (l.ribbon != null)
                {
                    Vector2 rb = l.ribbon.sprite.bounds.size;
                    l.ribbon.transform.localScale = Vector3.one * (spriteSize.x * 0.3f / Mathf.Max(0.001f, rb.x));
                    l.ribbon.transform.localPosition = new Vector3(spriteSize.x * 0.38f, spriteSize.y * 0.36f, 0f);
                }
                SetPortalArt(l, false);
                l.landmark.enabled = true;
                l.shown = true;
            }
            // the hexagon's own colour says the state: dim = locked, blue = cleared, violet = open
            Color hc = l.hex != null ? l.hex.color : Color.white;
            bool cleared = hc.b > 0.85f && hc.g > 0.6f;
            bool locked = !cleared && hc.r < 0.4f && hc.g < 0.22f;
            Color tint = locked ? new Color(0.55f, 0.53f, 0.58f, 1f) : Color.white;
            tint.a = hc.a;
            l.landmark.color = tint;
            if (l.ribbon != null)
            {
                l.ribbon.enabled = cleared;
                l.ribbon.color = new Color(0.55f, 0.8f, 1f, tint.a);
            }
        }
    }

    // The hexagon stays: it is the portal's plate, the original size, and its colour still says the
    // state (violet open, blue cleared, dim locked). Only the star gives way to the landmark.
    static void SetPortalArt(Look l, bool on)
    {
        if (l.star != null) l.star.forceRenderingOff = !on;
    }
}
