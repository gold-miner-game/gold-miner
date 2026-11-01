using UnityEngine;

// Owner: Penny (Level map & tutorial)
// A one-way tile in the world-map hub's maze ('<' '>' '^' 'v' in the grid). Solid like a wall unless
// the player is moving the allowed way. Drawn as a chevron pointing that way.
// Ported from Penny's maze hub (feature/penny-level-map).
public class OneWayDoor : MonoBehaviour
{
    public Vector2 allowedDir = Vector2.left;

    Collider2D wall;
    Collider2D playerCol;
    Rigidbody2D playerBody;
    PlayerCharacter player;

    // Also for a door placed in the world map scene: it keeps its Allowed Dir and adds what is missing
    public void Init(Vector2 dir, float tileSize)
    {
        allowedDir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.left;
        transform.localScale = Vector3.one * tileSize;

        var sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = ChevronSprite();
        sr.color = new Color(0.25f, 0.7f, 0.55f, 0.95f);
        sr.sortingOrder = 2;
        // The chevron art points left
        float angle = Mathf.Atan2(allowedDir.y, allowedDir.x) * Mathf.Rad2Deg;
        transform.localRotation = Quaternion.Euler(0f, 0f, angle + 180f);

        var box = GetComponent<BoxCollider2D>();
        if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;
        wall = box;

        var body = GetComponent<Rigidbody2D>();
        if (body == null) body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
    }

    // The chevron is drawn at runtime; in the Scene view an arrow shows the way through
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        Vector3 c = transform.position;
        Vector3 d = (Vector3)(allowedDir.sqrMagnitude > 0.01f ? allowedDir.normalized : Vector2.left) * 0.25f;
        Gizmos.color = new Color(0.25f, 0.85f, 0.6f);
        Gizmos.DrawLine(c - d, c + d);
        Vector3 side = new Vector3(-d.y, d.x, 0f) * 0.6f;
        Gizmos.DrawLine(c + d, c + d * 0.3f + side);
        Gizmos.DrawLine(c + d, c + d * 0.3f - side);
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
        var pixels = new Color[size * size];   // all clear

        // Two bars forming "<"
        void PaintBar(Vector2 a, Vector2 b, float thickness)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float r2 = thickness * thickness * 0.25f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Vector2.Dot(p - a, ab) / len2;
                    if (t < 0f || t > 1f) continue;
                    if ((p - (a + ab * t)).sqrMagnitude <= r2) pixels[y * size + x] = Color.white;
                }
        }
        PaintBar(new Vector2(44, 52), new Vector2(18, 32), 10f);
        PaintBar(new Vector2(18, 32), new Vector2(44, 12), 10f);
        tex.SetPixels(pixels);
        tex.Apply();
        chevron = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return chevron;
    }
}
