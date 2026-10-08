using UnityEngine;

// Owner: B (Player & Rocks)
[RequireComponent(typeof(Rigidbody2D))]
public class PigController : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private SpriteRenderer[] sprites;
    private Vector2 input;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sprites = GetComponentsInChildren<SpriteRenderer>();
    }

    void Update()
    {
        // WASD or arrow keys
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

        // Blink the whole pig while invincible (right after losing a Life)
        bool invincible = GameManager.Instance != null && GameManager.Instance.IsInvincible;
        bool show = !invincible || Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i].enabled = show;
        }
    }

    void FixedUpdate()
    {
        // No GameManager in the scene = free movement (handy in Dev scenes)
        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
        rb.linearVelocity = playing ? input * moveSpeed : Vector2.zero;
    }
}
