using System.Collections;
using TMPro;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// One map node (a level, the Store, Pick Character, Survive Mode) as a portal tile in the world-map
// hub. Walk into it to enter the node. A locked one only says what to clear first, or how much is
// still to earn when its earnings gate (MapNode.unlockGold) is what keeps it shut. Look after Penny's maze portals: a violet hexagon with a star.
// A hidden portal is a rock in the wall (same colour as the walls around it) until its node unlocks.
// The first time the player sees it unlocked, the rock turns into the portal (Penny's stone morph).
public class HubPortal : MonoBehaviour
{
    static readonly Color Locked    = new Color(0.34f, 0.17f, 0.46f, 1f);    // Available, dimmed
    static readonly Color Available = new Color(0.62f, 0.28f, 0.85f, 1f);
    static readonly Color Cleared   = new Color(0.40f, 0.72f, 0.95f, 1f);   // not gold: the survival rocks are yellow
    static readonly Color StarOn    = new Color(1f, 0.95f, 0.75f, 1f);
    static readonly Color StarOff   = new Color(0.55f, 0.52f, 0.60f, 1f);

    [Tooltip("The WorldMap node this portal enters (store_1, battle_01, survival, ...)")]
    public string nodeId;
    [Tooltip("A rock in the wall until the node unlocks")]
    public bool hidden;
    [Tooltip("The rock's colour while hidden: the walls around it")]
    public Color stoneColor = new Color(0.5f, 0.5f, 0.5f);
    [Tooltip("Seconds the rock takes to turn into the portal")]
    public float revealTime = 1.4f;

    public MapNode Node { get; private set; }

    MapHub hub;
    SpriteRenderer hex;
    SpriteRenderer star;
    TextMeshPro label;
    string labelName;
    CircleCollider2D area;
    Vector3 baseScale;
    Vector3 stoneScale;
    bool isStone;     // still a rock in the wall: solid, no star, no name
    bool revealing;
    bool armed;       // false while the player is standing on it (just came back from it)

    public void Init(MapNode node, float tileSize, Sprite hexSprite, MapHub owner, Vector2 playerAt)
    {
        Node = node;
        hub = owner;
        baseScale = Vector3.one * tileSize * 0.95f;
        stoneScale = Vector3.one * tileSize;   // the walls' size, so it reads as one of them

        hex = Visuals(gameObject, hexSprite, node.displayName, out label);
        labelName = label.text;

        var starGo = new GameObject("Star");
        starGo.transform.SetParent(transform, false);
        starGo.transform.localScale = Vector3.one * 0.55f;
        star = starGo.AddComponent<SpriteRenderer>();
        star.sprite = StarSprite();
        star.sortingOrder = 4;

        // Reaches a little past the tile, so a portal in a one-tile pocket is entered from the corridor
        area = GetComponent<CircleCollider2D>();
        if (area == null) area = gameObject.AddComponent<CircleCollider2D>();
        area.radius = 0.6f;

        armed = Vector2.Distance(playerAt, transform.position) > tileSize;

        isStone = hidden && (node.comingSoon || !ProgressStore.WasRevealed(node.id));   // coming soon: a rock, even if seen before
        if (isStone && IsUnlocked) StartCoroutine(Reveal());   // just unlocked: show it happening
        else ApplyLook();
    }

    // The hexagon and the name under it. Also used by the world map baker (HubSceneBaker), so a
    // portal placed in the scene shows in the Scene view; one that already has them keeps them
    public static SpriteRenderer Visuals(GameObject go, Sprite hexSprite, string text, out TextMeshPro label)
    {
        SpriteRenderer hex = go.GetComponent<SpriteRenderer>();
        if (hex == null) hex = go.AddComponent<SpriteRenderer>();
        if (hex.sprite == null) hex.sprite = hexSprite;
        hex.sortingOrder = 3;

        Transform existing = go.transform.Find("Label");
        label = existing != null ? existing.GetComponent<TextMeshPro>() : null;
        if (label == null)
        {
            // In world units under the tile, so it scales with the maze
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, -0.95f, 0f);
            label = labelGo.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 6f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(6f, 1f);
            label.sortingOrder = 5;
        }
        if (string.IsNullOrEmpty(label.text)) label.text = text;   // a name typed in the scene stays
        return hex;
    }

    bool IsUnlocked => GameFlow.Instance.Map != null && GameFlow.Instance.Map.IsUnlocked(Node);
    bool IsCleared => ProgressStore.IsCompleted(Node.id);

    void Update()
    {
        if (Node == null || revealing) return;   // not set up: the scene's level isn't the hub this play
        if (isStone)
        {
            if (IsUnlocked) StartCoroutine(Reveal());
            return;
        }
        ApplyLook();
        // An open, uncleared portal breathes so the next step is easy to spot
        float pulse = IsUnlocked && !IsCleared ? 1f + 0.08f * Mathf.Sin(Time.time * 4f) : 1f;
        transform.localScale = baseScale * pulse;
    }

    void ApplyLook()
    {
        area.isTrigger = !isStone;
        area.radius = isStone ? 0.5f : 0.6f;
        star.enabled = !isStone;
        label.enabled = !isStone;
        if (isStone)
        {
            hex.color = stoneColor;
            transform.localScale = stoneScale;
            return;
        }
        bool open = IsUnlocked;
        hex.color = !open ? Locked : IsCleared ? Cleared : Available;
        star.color = open ? StarOn : StarOff;
        label.color = open ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        label.text = WaitingOnGold
            ? labelName + "\n$" + Map.GoldTowardUnlock(Node) + " / $" + Node.unlockGold
            : labelName;
        transform.localScale = baseScale;
    }

    WorldMap Map => GameFlow.Instance.Map;

    // Its level before is cleared; only the earnings gate keeps it shut
    bool WaitingOnGold => !IsUnlocked && Map != null && Map.PrerequisiteCleared(Node) && !Map.GoldGateMet(Node);

    // The rock swells, takes the portal's colour and grows its star, then opens
    IEnumerator Reveal()
    {
        revealing = true;
        hub.ShowHint(Node.displayName + " appears!");
        star.enabled = true;
        label.enabled = true;
        for (float t = 0f; t < revealTime; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / revealTime);
            float ease = u * u * (3f - 2f * u);
            transform.localScale = Vector3.Lerp(stoneScale, baseScale, ease) * (1f + 0.28f * Mathf.Sin(u * Mathf.PI));
            hex.color = Color.Lerp(stoneColor, Available, ease);
            star.color = new Color(StarOn.r, StarOn.g, StarOn.b, ease);
            star.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.55f, ease);
            label.color = new Color(1f, 1f, 1f, ease);
            yield return null;
        }
        star.transform.localScale = Vector3.one * 0.55f;
        isStone = false;
        revealing = false;
        ProgressStore.MarkRevealed(Node.id);
        TutorialEvents.Fire(TutorialEvents.PortalRevealed(Node.id));
        ApplyLook();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (Node == null || isStone || revealing || !armed || other.GetComponentInParent<PlayerCharacter>() == null) return;
        if (!IsUnlocked)
        {
            PlayAnalytics.LockedPortal(Node.id, Map != null ? Map.GoldTowardUnlock(Node) : 0);
            if (WaitingOnGold)
                hub.ShowHint("Win by $" + Node.unlockGold + " in total across " + Map.GoldGateNames(Node)
                             + " (best round each, now $" + Map.GoldTowardUnlock(Node) + ")");
            else
                hub.ShowHint((PrerequisiteIsBattle() ? "Beat " : "Visit ") + PrerequisiteNames()
                             + " first to unlock " + Node.displayName);
            return;
        }
        armed = false;
        hub.Enter(this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (Node != null && other.GetComponentInParent<PlayerCharacter>() != null) armed = true;
    }

    string PrerequisiteNames()
    {
        WorldMap map = GameFlow.Instance.Map;
        string[] ids = Node.prerequisites ?? new string[0];
        var names = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            MapNode n = map != null ? map.Find(ids[i]) : null;
            names[i] = n != null ? n.displayName : ids[i];
        }
        return names.Length == 0 ? "the levels before" : string.Join(" or ", names);
    }

    bool PrerequisiteIsBattle()
    {
        WorldMap map = GameFlow.Instance.Map;
        if (map == null || Node.prerequisites == null || Node.prerequisites.Length == 0) return true;
        MapNode n = map.Find(Node.prerequisites[0]);
        return n == null || n.type == NodeType.Battle;
    }

    static Sprite starSprite;
    static Sprite StarSprite()
    {
        if (starSprite != null) return starSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];

        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float outer = size * 0.48f;
        float inner = outer * 0.38f;
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float r = i % 2 == 0 ? outer : inner;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = false;
                for (int i = 0; i < 5 && !inside; i++)
                    inside = InTri(p, c, pts[i * 2], pts[(i * 2 + 2) % 10]) || InTri(p, pts[i * 2], pts[i * 2 + 1], pts[(i * 2 + 2) % 10]);
                if (inside) pixels[y * size + x] = Color.white;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return starSprite;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Cross(Vector2 p, Vector2 a, Vector2 b) => (a.x - p.x) * (b.y - p.y) - (b.x - p.x) * (a.y - p.y);
}
