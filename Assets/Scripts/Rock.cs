using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

// Owner: B (Player & Rocks)
// Solid obstacle. Maze walls set MakeWall() so the hook stops without breaking the rock and the
// pig cannot shove it. Battle rocks still shatter after they block a shot.
public class Rock : MonoBehaviour
{
    public bool IsFrozen => FrostField2D.IsFrozen(transform);
    public Color blockedColor = Color.white;
    public float flashTime = 0.15f;
    public int pieceCount = 5;
    public float pieceSpeed = 3.5f;

    private SpriteRenderer sr;
    [Tooltip("A maze wall (the world map): planted, and it never breaks")]
    public bool isWall;
    [Tooltip("A hook flies through this rock (the world map's green rocks: levels not disclosed yet). The player still can't")]
    public bool hookPasses;

    [Header("Rhino break effect")]
    public GameObject brokenBlockPrefab;
    [FormerlySerializedAs("breakDelay")]
    [Tooltip("Base duration of each stage. Impact speed reduces the total, split equally between stages.")]
    [Min(0f)] public float breakStageTime = 0.3f;

    // Green barriers also exist in older baked maps without the hookPasses flag.
    public bool RhinoProof
    {
        get
        {
            if (hookPasses) return true;
            SpriteRenderer look = sr != null ? sr : GetComponent<SpriteRenderer>();
            if (look == null) return false;
            Color tint = look.color;
            Color green = MapHub.GreenRock;
            return Mathf.Abs(tint.r - green.r) < 0.05f && Mathf.Abs(tint.g - green.g) < 0.05f
                && Mathf.Abs(tint.b - green.b) < 0.05f;
        }
    }

    private bool shattered;
    private bool indestructible;
    GameObject chargeDebris;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // Prefab ships Dynamic; that lets the pig shove rocks into corridors. Keep them planted.
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null) body.bodyType = RigidbodyType2D.Static;
        if (isWall) MakeWall();
    }

    // Maze walls (the world map, '#' tiles): planted, and they never break
    public void MakeWall()
    {
        isWall = true;
        indestructible = true;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null) body.bodyType = RigidbodyType2D.Static;
    }

    // Called by Hook when this rock blocks it
    public void OnHookBlocked()
    {
        if (IsFrozen) { FrostField2D.BreakIce(transform); return; }
        if (indestructible || shattered || isWall) return;
        shattered = true;
        SpawnPieces();
        DisableColliders();
        Destroy(gameObject);
    }

    // Hulk can smash ordinary walls, but never the green barriers hiding locked map areas.
    public bool Smash(float impactSpeed = 0f, float speedFactor = 0f)
    {
        if (shattered || RhinoProof || IsFrozen) return false;
        shattered = true;
        GetComponent<MapRockDamage>()?.RememberSmash();
        float total = BreakTime(impactSpeed, speedFactor);
        if (total <= 0f)
        {
            // Destroy is deferred until the frame ends; clear collision and artwork now.
            DisableColliders();
            foreach (SpriteRenderer original in GetComponentsInChildren<SpriteRenderer>()) original.enabled = false;
            CompleteSmash();
        }
        else
        {
            // Stay solid during both equal stages until destruction finishes.
            StartCoroutine(BreakFromCharge(total * 0.5f));
        }
        return true;
    }

    public float BreakTime(float impactSpeed, float speedFactor)
    {
        float total = 2f * Mathf.Max(0f, breakStageTime);
        float reduction = Mathf.Max(0f, speedFactor) * Mathf.Max(0f, impactSpeed);
        // Avoid a one-frame wait from floating-point rounding at the instant-break threshold.
        return reduction >= total || Mathf.Approximately(reduction, total) ? 0f : total - reduction;
    }

    void DisableColliders()
    {
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
    }

    IEnumerator BreakFromCharge(float stageDuration)
    {
        yield return new WaitForSeconds(stageDuration);
        GameObject prefab = brokenBlockPrefab != null ? brokenBlockPrefab
            : Resources.Load<GameObject>("Effects/BrokenBlock");
        if (prefab != null)
        {
            foreach (SpriteRenderer original in GetComponentsInChildren<SpriteRenderer>()) original.enabled = false;
            GameObject broken = Instantiate(prefab, transform, false);
            chargeDebris = broken;
            broken.name = "BrokenBlock";
            SpriteRenderer brokenBody = broken.GetComponent<SpriteRenderer>();
            if (sr != null && brokenBody != null)
            {
                brokenBody.sprite = sr.sprite;
                brokenBody.color = sr.color;
                brokenBody.sharedMaterial = sr.sharedMaterial;
                foreach (SpriteRenderer part in broken.GetComponentsInChildren<SpriteRenderer>())
                {
                    part.sortingLayerID = sr.sortingLayerID;
                    part.sortingOrder += sr.sortingOrder;
                }
            }
        }
        yield return new WaitForSeconds(stageDuration);
        CompleteSmash();
    }

    void CompleteSmash()
    {
        if (GetComponent<MapRockDamage>() != null) gameObject.SetActive(false);
        else Destroy(gameObject);
    }

    public void ResetMapSmash()
    {
        StopAllCoroutines(); shattered = false;
        if (chargeDebris != null) { chargeDebris.SetActive(false); Destroy(chargeDebris); chargeDebris = null; }
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
