using UnityEngine;

// Owner: B (Player & Rocks)
// Solid obstacle. Maze walls set MakeWall() so the hook stops without breaking the rock and the
// pig cannot shove it. Battle rocks still shatter after they block a shot.
public class Rock : MonoBehaviour
{
    public Color blockedColor = Color.white;
    public float flashTime = 0.15f;
    public int pieceCount = 5;
    public float pieceSpeed = 3.5f;

    private SpriteRenderer sr;
    private bool shattered;
    private bool indestructible;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // Prefab ships Dynamic; that lets the pig shove rocks into corridors. Keep them planted.
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null) body.bodyType = RigidbodyType2D.Static;
    }

    // Maze '#': permanent wall. Hook still stops; rock never moves or breaks.
    public void MakeWall()
    {
        indestructible = true;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null) body.bodyType = RigidbodyType2D.Static;
    }

    // Called by Hook when this rock blocks it
    public void OnHookBlocked()
    {
        if (indestructible || shattered) return;
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
