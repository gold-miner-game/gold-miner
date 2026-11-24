using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: hidden hub portals must share the painted stones used by the surrounding rocks; leaving
// their placeholder hexagon visible would give away the secret. The Star renderer is HubPortal's
// public visual signal: disabled (or missing) means stone, enabled means revealing or revealed.
//   - Only the portal renderer's sprite and flipX change. HubPortal keeps control of colour, scale,
//     transforms, labels, colliders and unlocking; no gameplay fields are read or changed.
//   - Remember the original hexagon and restore it once as soon as the star appears, so the existing
//     reveal animation still draws a portal. Missing stone art disables this layer before any edits.
[DefaultExecutionOrder(31000)]
public class PaperPortalStones : MonoBehaviour
{
    static readonly string[] StoneNames = { "Stone1", "Stone2", "Stone3", "Stone5", "Stone6", "Stone7", "Stone9" };
    static PaperPortalStones instance;

    sealed class PortalLook
    {
        public HubPortal portal;
        public SpriteRenderer hex;
        public SpriteRenderer star;
        public Sprite original;
        public bool replaced;
        public Sprite stone;   // fitted like a wall rock's (ArtLibrary.ScaledBoil), picked once
    }

    readonly List<PortalLook> portals = new List<PortalLook>();
    Sprite[] stones;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Portal Stones");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperPortalStones>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (instance != null) instance.nextScan = 0f;
    }

    void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    void Start()
    {
        stones = new Sprite[StoneNames.Length];
        for (int i = 0; i < stones.Length; i++)
        {
            stones[i] = ArtLibrary.Get("Objects/" + StoneNames[i]);
            if (stones[i] == null)
            {
                enabled = false;
                return;
            }
        }
    }

    void Scan()
    {
        // The query and new tracking records allocate only on the half-second scan, never per frame.
        foreach (HubPortal portal in FindObjectsByType<HubPortal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            PortalLook look = null;
            for (int i = 0; i < portals.Count; i++)
            {
                if (portals[i].portal != portal) continue;
                look = portals[i];
                break;
            }

            if (look == null)
            {
                SpriteRenderer hex = portal.GetComponent<SpriteRenderer>();
                if (hex == null) continue;
                look = new PortalLook { portal = portal, hex = hex, original = hex.sprite };
                portals.Add(look);
            }

            Transform star = portal.transform.Find("Star");
            look.star = star != null ? star.GetComponent<SpriteRenderer>() : null;
        }
    }

    // Same fit as PaperRocks: the drawing shrinks to 0.96 x the distance to the nearest rock, so the
    // disguised portal matches the wall stones around it. Read-only physics query, once per portal.
    static float FitFactor(Transform portal)
    {
        float scale = Mathf.Abs(portal.lossyScale.x);
        Vector2 at = portal.position;
        float nearest = float.PositiveInfinity;
        foreach (Collider2D c in Physics2D.OverlapCircleAll(at, Mathf.Max(1.05f, 1.2f * scale)))
        {
            Rock other = c.GetComponentInParent<Rock>();
            if (other == null) continue;
            nearest = Mathf.Min(nearest, Vector2.Distance(at, other.transform.position));
        }
        return scale > 0f && nearest <= 1.2f * scale ? Mathf.Min(1f, 0.96f * nearest / scale) : 1f;
    }

    // The same drawn size as the wall rocks beside it. Read-only physics query, once per portal.
    static float MatchRocks(Transform portal)
    {
        // rock positions, not a physics query: the hub's walls may not have synced their colliders yet
        float scale = Mathf.Abs(portal.lossyScale.x);
        Vector2 at = portal.position;
        float best = float.PositiveInfinity;
        foreach (Rock other in FindObjectsByType<Rock>(FindObjectsSortMode.None))
            best = Mathf.Min(best, Vector2.Distance(at, other.transform.position));
        // fit to the spacing like the rocks around it (0.96 x the nearest rock's distance), so a disguised portal
        // is exactly the size of its neighbours
        return scale > 0f && best <= 1.05f ? 0.96f * best / scale : FitFactor(portal);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            Scan();
        }

        for (int i = portals.Count - 1; i >= 0; i--)
        {
            PortalLook look = portals[i];
            if (look.portal == null || look.hex == null)
            {
                portals.RemoveAt(i);
                continue;
            }

            bool stone = look.star == null || !look.star.enabled;
            if (stone)
            {
                Vector2 p = look.portal.transform.position;
                int signedHash = unchecked(Mathf.RoundToInt(p.x * 7.13f) * 73856093
                    ^ Mathf.RoundToInt(p.y * 5.71f) * 19349663);
                // Absolute magnitude also handles int.MinValue without a negative array index.
                uint hash = (uint)(signedHash < 0 ? -(long)signedHash : signedHash);
                if (look.stone == null)
                    look.stone = ArtLibrary.ScaledBoil(stones[0], MatchRocks(look.portal.transform));   // Stone1, like the green rocks
                // PaperShadows boils this renderer like a rock (on threes): leave any of the stone's boil
                // frames in place, only put the stone back when something else is showing.
                Sprite[] frames = ArtLibrary.BoilFrames(look.stone);
                Sprite now = look.hex.sprite;
                bool showing = now == look.stone || (frames != null && (now == frames[0] || now == frames[1] || now == frames[2]));
                if (!showing) look.hex.sprite = look.stone;
                look.hex.flipX = false;   // same unflipped shape as the green rocks
                look.replaced = true;
            }
            else if (look.replaced)
            {
                look.hex.sprite = look.original;
                look.hex.flipX = false;
                look.replaced = false;
            }
        }
    }
}
