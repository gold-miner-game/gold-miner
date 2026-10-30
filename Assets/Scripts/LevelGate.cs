using UnityEngine;

// Maze gate "X" (world-map level node). Hexagon + star, not a gold-looking square.
// Touching it with enough entry credit wins the round; otherwise a hint is shown.
public class LevelGate : MonoBehaviour
{
    public int entryCredit;
    public string lockedHint = "Not enough money yet";

    MazeRuntime maze;
    static Sprite hexSprite;
    static Sprite starSprite;

    public void Init(int credit, float tileSize, MazeRuntime runtime)
    {
        entryCredit = credit;
        maze = runtime;
        transform.localScale = Vector3.one * tileSize * 0.95f;

        // Violet — distinct from yellow gold and cyan diamond.
        var hex = gameObject.AddComponent<SpriteRenderer>();
        hex.sprite = HexSprite();
        hex.color = new Color(0.62f, 0.28f, 0.85f, 1f);
        hex.sortingOrder = 3;

        var starGo = new GameObject("Star");
        starGo.transform.SetParent(transform, false);
        starGo.transform.localScale = Vector3.one * 0.55f;
        var star = starGo.AddComponent<SpriteRenderer>();
        star.sprite = StarSprite();
        star.color = new Color(1f, 0.95f, 0.75f, 1f);
        star.sortingOrder = 4;

        var box = gameObject.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerCharacter>() == null) return;
        int money = GameManager.Instance != null ? GameManager.Instance.PlayerMoney : 0;
        if (money >= entryCredit)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlaying)
                GameManager.Instance.WinRound("Gate cleared!");
            return;
        }
        if (maze != null) maze.ShowHint(lockedHint + " (need $" + entryCredit + ")");
    }

    static Sprite HexSprite()
    {
        if (hexSprite != null) return hexSprite;
        // Prefer the project hex art when present.
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
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        Color fill = Color.white;
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
        // Fill by winding test against the 5 star triangles from center.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = false;
                for (int i = 0; i < 5 && !inside; i++)
                    inside = PointInTri(p, c, pts[i * 2], pts[(i * 2 + 2) % 10]);
                // Also fill the pentagon core via outer→inner edge pairs.
                if (!inside)
                {
                    for (int i = 0; i < 5 && !inside; i++)
                        inside = PointInTri(p, pts[i * 2], pts[i * 2 + 1], pts[(i * 2 + 2) % 10]);
                }
                if (inside) tex.SetPixel(x, y, fill);
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
