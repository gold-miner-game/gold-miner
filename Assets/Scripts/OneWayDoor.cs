using UnityEngine;

// Maze one-way tile ("<" entity). Solid like a rock unless the pig is moving in `allowedDir`
// (Left / Right / Up / Down). Drawn as a chevron, not a level node.
public class OneWayDoor : MonoBehaviour
{
    public Vector2 allowedDir = Vector2.left;

    Collider2D wall;
    Collider2D playerCol;
    Rigidbody2D playerBody;
    PlayerCharacter player;

    public void Init(string dir, float tileSize)
    {
        allowedDir = ParseDir(dir);
        transform.localScale = Vector3.one * tileSize;

        var sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = ChevronSprite();
        sr.color = new Color(0.25f, 0.7f, 0.55f, 0.95f);
        sr.sortingOrder = 2;
        // Point the chevron along the allowed travel direction (default art faces left).
        float angle = Mathf.Atan2(allowedDir.y, allowedDir.x) * Mathf.Rad2Deg;
        transform.localRotation = Quaternion.Euler(0f, 0f, angle + 180f);

        wall = gameObject.AddComponent<BoxCollider2D>();
        ((BoxCollider2D)wall).size = Vector2.one;

        var body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
    }

    static Vector2 ParseDir(string dir)
    {
        switch ((dir ?? "").ToLowerInvariant())
        {
            case "right": return Vector2.right;
            case "up": return Vector2.up;
            case "down": return Vector2.down;
            default: return Vector2.left;
        }
    }

    void FixedUpdate()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerCharacter>();
            if (player == null) return;
            playerBody = player.GetComponent<Rigidbody2D>();
            playerCol = player.GetComponent<Collider2D>();
        }
        if (wall == null || playerCol == null) return;

        Vector2 move = player.MoveInput;
        if (move.sqrMagnitude < 0.01f && playerBody != null) move = playerBody.linearVelocity;
        bool open = move.sqrMagnitude > 0.01f && Vector2.Dot(move.normalized, allowedDir) > 0.35f;
        Physics2D.IgnoreCollision(wall, playerCol, open);
    }

    static Sprite chevron;
    static Sprite ChevronSprite()
    {
        if (chevron != null) return chevron;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);

        // Two bars forming "<".
        void PaintBar(Vector2 a, Vector2 b, float thickness)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.001f) return;
            Vector2 n = new Vector2(-ab.y, ab.x).normalized * (thickness * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Vector2.Dot(p - a, ab) / (len * len);
                    if (t < 0f || t > 1f) continue;
                    Vector2 closest = a + ab * t;
                    if ((p - closest).sqrMagnitude <= n.sqrMagnitude)
                        tex.SetPixel(x, y, Color.white);
                }
            }
        }
        PaintBar(new Vector2(44, 52), new Vector2(18, 32), 10f);
        PaintBar(new Vector2(18, 32), new Vector2(44, 12), 10f);
        tex.Apply();
        chevron = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return chevron;
    }
}
