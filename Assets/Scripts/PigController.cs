using UnityEngine;
using UnityEngine.InputSystem;

// Owner: B (Player & Rocks)
[RequireComponent(typeof(Rigidbody2D))]
public class PigController : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;

    private bool facingRight = true; // Default facing is right
    private Rigidbody2D body;
    private SpriteRenderer sr;
    private Vector2 input;

    void Awake()
    {
        // Move through physics so solid objects (rocks, walls) block the pig
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true; // Don't spin when bumping into things
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        sr = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        ReadInput();
        UpdateBlink();
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
