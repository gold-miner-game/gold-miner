using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class player : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;

    // Lowered by other scripts (e.g. fetch while dragging something heavy)
    [HideInInspector] public float speedLimit = float.PositiveInfinity;

    private bool facingRight = true; // Default facing is right
    private Rigidbody2D body;
    private Vector2 input;

    void Awake()
    {
        // Move through physics so solid objects (like fetchables) block the player
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true; // Don't spin when bumping into things
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Update()
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

    void FixedUpdate()
    {
        // Normalize so diagonals are not faster; physics stops us at colliders
        body.linearVelocity = Mathf.Min(moveSpeed, speedLimit) * input.normalized;
    }

    // Mirror the whole player (and its children) horizontally
    void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x = -scale.x;
        transform.localScale = scale;
    }
}
