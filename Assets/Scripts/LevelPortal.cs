using System.Collections;
using UnityEngine;

// World-map level marker on the maze.
// Level 1 (X): purple portal from the start; needs $900 maze gold this visit.
// Level 2: IS the first wall-rock on Level 1's row to the left. Stays a normal grey rock
// until Level 1 is cleared, then morphs into a purple portal (never pops in already purple).
public class LevelPortal : MonoBehaviour
{
    public string nodeId = "battle_01";
    public int unlockCost;
    public int entryCredit;
    public string requiresCompleted;
    public string lockedHint = "Need more money";

    MazeRuntime maze;
    SpriteRenderer hex;
    SpriteRenderer star;
    SpriteRenderer stone;
    Collider2D solidWall;   // blocks like a rock while still a stone
    Collider2D enterTrigger;
    bool entered;
    bool morphDone;
    bool morphing;
    Vector3 baseScale;

    public void Init(string id, int cost, int mazeCredit, string prereq, float tileSize, MazeRuntime runtime)
    {
        nodeId = id;
        unlockCost = cost;
        entryCredit = mazeCredit;
        requiresCompleted = prereq ?? "";
        maze = runtime;
        // Match maze wall rock size so L2 stone blends into the wall line.
        baseScale = Vector3.one * tileSize * 0.88f;
        transform.localScale = baseScale;

        stone = gameObject.AddComponent<SpriteRenderer>();
        stone.sprite = HexSprite();
        stone.color = new Color(0.5f, 0.5f, 0.5f, 1f); // same grey family as Rock prefab
        stone.sortingOrder = 3;

        hex = CreateChildSprite("Hex", HexSprite(), new Color(0.62f, 0.28f, 0.85f, 1f), 3);
        star = CreateChildSprite("Star", StarSprite(), new Color(1f, 0.95f, 0.75f, 1f), 4);
        star.transform.localScale = Vector3.one * 0.55f;

        // Solid while stone (walk into wall). Swap to trigger after morph.
        var box = gameObject.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;
        solidWall = box;
        enterTrigger = null;

        var body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;

        if (IsLevel1)
        {
            morphDone = true;
            SetAsPortalCollider();
            ApplyPortalLook(CanEnter);
            transform.localScale = Vector3.one * tileSize * 0.95f;
            baseScale = transform.localScale;
        }
        else
        {
            // Always a wall rock until Level 1 is won — never show purple early.
            morphDone = false;
            ApplyStoneLook();
            if (IsRevealed) StartCoroutine(MorphStoneToPortal());
        }
    }

    SpriteRenderer CreateChildSprite(string name, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    public bool IsRevealed
    {
        get
        {
            if (string.IsNullOrEmpty(requiresCompleted)) return true;
            return ProgressStore.IsCompleted(requiresCompleted);
        }
    }

    bool IsLevel1 => string.IsNullOrEmpty(requiresCompleted) || nodeId == "battle_01";

    int RoundMoney => GameManager.Instance != null ? GameManager.Instance.PlayerMoney : 0;

    public bool CanEnter
    {
        get
        {
            if (!IsLevel1 && !morphDone) return false;
            if (!IsRevealed) return false;
            if (IsLevel1)
            {
                int need = entryCredit > 0 ? entryCredit : 900;
                return RoundMoney >= need;
            }
            // Same total as the MONEY sidebar: maze bank + best margins.
            return RoundMoney >= unlockCost;
        }
    }

    void Update()
    {
        if (!IsLevel1 && !morphDone && !morphing && IsRevealed)
            StartCoroutine(MorphStoneToPortal());

        if (morphDone && !morphing)
            ApplyPortalLook(CanEnter);
    }

    void ApplyStoneLook()
    {
        if (stone != null) { stone.enabled = true; stone.color = new Color(0.5f, 0.5f, 0.5f, 1f); }
        if (hex != null) hex.enabled = false;
        if (star != null) star.enabled = false;
        transform.localScale = baseScale;
    }

    void ApplyPortalLook(bool open)
    {
        if (stone != null) stone.enabled = false;
        if (hex != null)
        {
            hex.enabled = true;
            hex.color = open
                ? new Color(0.62f, 0.28f, 0.85f, 1f)
                : new Color(0.35f, 0.22f, 0.45f, 0.85f);
        }
        if (star != null) star.enabled = true;
    }

    void SetAsPortalCollider()
    {
        if (solidWall != null)
        {
            solidWall.isTrigger = true;
            enterTrigger = solidWall;
            solidWall = null;
        }
    }

    IEnumerator MorphStoneToPortal()
    {
        morphing = true;
        if (maze != null) maze.ShowHint("A stone becomes Level 2!");

        float dur = 1.4f;
        float t = 0f;
        Color stoneA = new Color(0.5f, 0.5f, 0.5f, 1f);
        Color portalA = new Color(0.62f, 0.28f, 0.85f, 1f);
        Vector3 from = baseScale;
        Vector3 to = baseScale * (0.95f / 0.88f);

        if (stone != null) stone.enabled = true;
        if (hex != null) { hex.enabled = true; hex.color = new Color(portalA.r, portalA.g, portalA.b, 0f); }
        if (star != null) { star.enabled = true; star.color = new Color(1f, 0.95f, 0.75f, 0f); }

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float ease = u * u * (3f - 2f * u);
            float punch = 1f + 0.28f * Mathf.Sin(u * Mathf.PI);
            transform.localScale = Vector3.Lerp(from, to, ease) * punch;

            if (stone != null)
                stone.color = Color.Lerp(stoneA, new Color(portalA.r, portalA.g, portalA.b, 0f), ease);
            if (hex != null)
            {
                Color c = portalA;
                c.a = ease;
                hex.color = c;
            }
            if (star != null)
            {
                star.color = new Color(1f, 0.95f, 0.75f, ease);
                star.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.55f, ease);
            }
            yield return null;
        }

        baseScale = to;
        transform.localScale = to;
        SetAsPortalCollider();
        morphDone = true;
        morphing = false;
        ApplyPortalLook(CanEnter);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (entered || morphing || !morphDone) return;
        if (other.GetComponent<PlayerCharacter>() == null) return;
        // Returning from a battle: ignore portals briefly so standing on the gate doesn't re-enter.
        if (Time.unscaledTime < MazeHubState.PortalIgnoreUntil) return;

        if (IsLevel1)
        {
            int need = entryCredit > 0 ? entryCredit : 900;
            if (RoundMoney < need)
            {
                if (maze != null)
                    maze.ShowHint("Level 1 needs $" + need + " (have $" + RoundMoney + ")");
                return;
            }
        }
        else if (RoundMoney < unlockCost)
        {
            if (maze != null)
                maze.ShowHint(lockedHint + " (need $" + unlockCost + ", have $" + RoundMoney + ")");
            return;
        }

        entered = true;
        if (GameFlow.Instance != null)
            GameFlow.Instance.EnterBattleFromMaze(nodeId);
    }

    // Still a wall stone: bumping it reminds the player what it will become.
    void OnCollisionEnter2D(Collision2D other)
    {
        if (morphDone || morphing) return;
        if (other.collider.GetComponent<PlayerCharacter>() == null) return;
        if (maze != null) maze.ShowHint("Clear Level 1 — this rock becomes Level 2");
    }

    static Sprite hexSprite;
    static Sprite starSprite;

    static Sprite HexSprite()
    {
        if (hexSprite != null) return hexSprite;
        foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (s != null && s.name.IndexOf("Hexagon", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hexSprite = s;
                return hexSprite;
            }
        }
        hexSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return hexSprite;
    }

    static Sprite StarSprite()
    {
        if (starSprite != null) return starSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float outer = size * 0.48f;
        float inner = outer * 0.38f;
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float r = (i % 2 == 0) ? outer : inner;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = false;
                for (int i = 0; i < 5 && !inside; i++)
                    inside = PointInTri(p, c, pts[i * 2], pts[(i * 2 + 2) % 10]);
                if (!inside)
                    for (int i = 0; i < 5 && !inside; i++)
                        inside = PointInTri(p, pts[i * 2], pts[i * 2 + 1], pts[(i * 2 + 2) % 10]);
                if (inside) tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();
        starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return starSprite;
    }

    static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
        float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
        if ((s < 0) != (t < 0) && s != 0 && t != 0) return false;
        float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
        return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
    }
}
