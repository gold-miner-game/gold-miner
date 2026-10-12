using UnityEngine;
using UnityEngine.InputSystem;

// Owner: B (Player & Rocks)
[RequireComponent(typeof(Rigidbody2D))]
public class PigController : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;

    [Header("Digest (0 = off)")]
    public float digestTime = 0f;       // after eating, the pig can't eat for digestTime + digestPerValue * value
    public float digestPerValue = 0f;
    public Color digestTint = new Color(0.65f, 0.9f, 0.55f);

    [Header("Block (Space)")]
    public bool canBlock = false;       // turned on per level by LevelLoader
    public float blockDuration = 0.8f;  // how long one block lasts
    public float blockCooldown = 1f;    // wait after a block ends before the next one
    public Color blockTint = new Color(0.45f, 0.75f, 1f);

    // Treasure asks this before letting the pig eat it
    public bool CanEat => Time.time >= digestUntil;

    // Hook asks this when it touches the pig: a blocking pig bounces the hook back
    public bool IsBlocking => Time.time < blockUntil;

    private bool facingRight = true; // Default facing is right
    private Rigidbody2D body;
    private SpriteRenderer sr;
    private Color baseColor;
    private Vector2 input;
    private float digestUntil;
    private float blockUntil;
    private float blockReadyAt;

    void Awake()
    {
        // Move through physics so solid objects (rocks, walls) block the pig
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true; // Don't spin when bumping into things
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Never sleep: Treasure re-checks every physics step while touched, so a pig standing on
        // gold eats it as soon as its digest ends
        body.sleepMode = RigidbodySleepMode2D.NeverSleep;

        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    // Called by Treasure when the pig eats it
    public void Digest(int value)
    {
        digestUntil = Time.time + digestTime + digestPerValue * value;
    }

    void Update()
    {
        ReadInput();
        UpdateBlink();
        if (sr != null) sr.color = IsBlocking ? blockTint : CanEat ? baseColor : digestTint;
    }

    void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        input = Vector2.zero;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.wKey.isPressed) input.y += 1f;

        if (keyboard.spaceKey.wasPressedThisFrame) TryBlock();

        // Flip only when the horizontal direction changes; keep facing when moving vertically
        if (input.x < 0f && facingRight)
        {
            Flip();
        }
        else if (input.x > 0f && !facingRight)
        {
            Flip();
        }
    }

    // Start a block if the skill is on and off cooldown
    void TryBlock()
    {
        if (!canBlock || Time.time < blockReadyAt) return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

        blockUntil = Time.time + blockDuration;
        blockReadyAt = blockUntil + blockCooldown;
    }

    // Blink while invincible (right after losing a Life)
    void UpdateBlink()
    {
        if (sr == null)
        {
            return;
        }

        bool invincible = GameManager.Instance != null && GameManager.Instance.IsInvincible;
        if (!invincible)
        {
            sr.enabled = true;
            return;
        }

        // Toggle visible/hidden 10 times per second
        int tick = Mathf.FloorToInt(Time.time * 10f);
        sr.enabled = tick % 2 == 0;
    }

    void FixedUpdate()
    {
        // No GameManager in the scene = free movement (handy in Dev scenes)
        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
        if (!playing)
        {
            // Round is over: stand still
            body.linearVelocity = Vector2.zero;
            return;
        }

        // Normalize so diagonals are not faster; physics stops us at colliders
        body.linearVelocity = moveSpeed * input.normalized;
    }

    // Mirror the whole pig (and its children) horizontally
    void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x = -scale.x;
        transform.localScale = scale;
    }
}
