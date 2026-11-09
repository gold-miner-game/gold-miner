using UnityEngine;

// Owner: Butian (attack mode)
// "Red Square": a red block with no hook. Faster than a Red Round; it runs at the player and rams
// them. A ram costs a Life (GameManager.PlayerHit) unless the player is blocking (Mr. Hyde holding
// his Space block): then it bounces off with the block's flash and no harm done. Either way it is
// knocked back and needs a moment before it charges again. A rolling cat is untouchable and passes
// through. With HP (square_hp > 0, attack mode) it dies like any HookHealth enemy: Mr. Hyde's Space up
// close, or the cat rolling into it. With square_hp 0 (survival) nothing can kill it.
public class RedSquare : RedEnemy
{
    [Tooltip("How far a ram knocks it back, in world units")]
    public float knockback = 2.5f;
    [Tooltip("Seconds the knock-back takes")]
    public float knockTime = 0.3f;
    [Tooltip("Seconds it waits after a ram before it charges again")]
    public float recover = 0.8f;
    [Tooltip("It rams when its box is this close to the player's body")]
    public float contactGap = 0.1f;

    public static readonly Color Red = new Color(0.86f, 0.16f, 0.16f);

    Collider2D box;
    Collider2D playerBody;
    PlayerCharacter player;
    Vector2 knockDir;
    float knockUntil = -1f, waitUntil = -1f;

    protected override void Start()
    {
        base.Start();
        box = GetComponent<Collider2D>();
    }

    void FixedUpdate()
    {
        if (Pushed()) return;
        if (!Ready(out float distance)) return;
        if (player == null)
        {
            player = target.GetComponent<PlayerCharacter>();
            playerBody = target.GetComponent<Collider2D>();
        }

        // Knocked back: slide away, stopped by walls
        if (Time.time < knockUntil)
        {
            float speed = knockback / Mathf.Max(0.01f, knockTime);
            if (!Blocked(knockDir, speed * Time.fixedDeltaTime + 0.05f))
                body.MovePosition(body.position + knockDir * speed * Time.fixedDeltaTime);
            return;
        }
        if (!Hunting || Time.time < waitUntil) return;

        if (Touching()) { Ram(); return; }
        Chase(moveSpeed * Time.fixedDeltaTime);
    }

    bool Touching()
    {
        if (box == null || playerBody == null) return false;
        ColliderDistance2D gap = box.Distance(playerBody);
        return gap.isValid && gap.distance <= contactGap;
    }

    void Ram()
    {
        Vector2 toMe = (body.position - (Vector2)target.position);
        if (toMe.sqrMagnitude < 0.0001f) toMe = Vector2.up;
        toMe.Normalize();
        knockDir = toMe;
        knockUntil = Time.time + knockTime;
        waitUntil = knockUntil + recover;

        if (player != null && player.IsInvulnerable) return;   // a rolling cat: passes through
        if (player != null && player.IsBlocking)
        {
            // Bounced off the block: the block's flash on the player, facing the square
            Vector2 surface = playerBody != null ? playerBody.ClosestPoint(body.position) : (Vector2)target.position;
            float width = playerBody != null ? playerBody.bounds.size.x : 1f;
            BlockFlash.Show(surface, toMe, width);
            player.OnHookBlocked();
            return;
        }
        if (GameManager.Instance != null) GameManager.Instance.PlayerHit();
    }

    // A solid red square with a dark rim, one unit across
    public static Sprite CreateSprite()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        Color rim = new Color(0.35f, 0.05f, 0.05f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x < 3 || y < 3 || x >= size - 3 || y >= size - 3;
                pixels[y * size + x] = edge ? rim : Color.white;   // the fill is tinted by the renderer
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
