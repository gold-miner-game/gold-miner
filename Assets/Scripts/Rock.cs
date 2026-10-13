using UnityEngine;

// Owner: B (Player & Rocks)
// The hook still stops on this rock. OnHookBlocked then breaks it, so that shot is blocked
// and the rock cannot be pushed into the path again.
public class Rock : MonoBehaviour
{
    public Color blockedColor = Color.white;
    public float flashTime = 0.15f;
    public int pieceCount = 5;
    public float pieceSpeed = 3.5f;

    private SpriteRenderer sr;
    private bool shattered;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    // Called by Hook when this rock blocks it
    public void OnHookBlocked()
    {
        if (shattered) return;
        shattered = true;

        SpawnPieces();

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        Destroy(gameObject);
    }

    void SpawnPieces()
    {
        Sprite sprite = sr != null ? sr.sprite : null;
        Color color = sr != null ? sr.color : Color.gray;
        int count = Mathf.Max(3, pieceCount);

        for (int i = 0; i < count; i++)
        {
            GameObject piece = new GameObject("RockPiece");
            piece.transform.position = transform.position;
            piece.transform.localScale = transform.localScale * Random.Range(0.22f, 0.38f);

            SpriteRenderer pieceRenderer = piece.AddComponent<SpriteRenderer>();
            pieceRenderer.sprite = sprite;
            pieceRenderer.color = i == 0 ? blockedColor : color;
            pieceRenderer.sortingOrder = 8;

            Rigidbody2D body = piece.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.linearDamping = 6f;
            float angle = (360f / count) * i + Random.Range(-15f, 15f);
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            body.linearVelocity = direction * pieceSpeed;

            Destroy(piece, flashTime + 0.25f);
        }
    }
}
