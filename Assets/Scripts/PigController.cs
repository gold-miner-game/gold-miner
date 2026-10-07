using UnityEngine;

// Owner: B (Player & Rocks)
[RequireComponent(typeof(Rigidbody2D))]
public class PigController : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Vector2 input;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // WASD or arrow keys
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

        // Blink while invincible (right after losing a Life)
        if (sr != null)
        {
            bool invincible = GameManager.Instance != null && GameManager.Instance.IsInvincible;
            sr.enabled = !invincible || Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
        }
    }

    void FixedUpdate()
    {
        // No GameManager in the scene = free movement (handy in Dev scenes)
        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
        rb.linearVelocity = playing ? input * moveSpeed : Vector2.zero;
    }
}
