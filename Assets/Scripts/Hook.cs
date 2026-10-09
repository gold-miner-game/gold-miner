using System.Collections.Generic;
using UnityEngine;

// Owner: D (Hook & UI)
public class Hook : MonoBehaviour
{
    [Header("Speed")]
    public float extendSpeed = 12f;
    public float retractSpeed = 16f;

    [Header("Weight (retract speed multiplier)")]
    public float smallGoldSpeed = 1.0f;
    public float mediumGoldSpeed = 0.7f;
    public float largeGoldSpeed = 0.45f;
    public float diamondSpeed = 1.1f;

    [Header("Collision")]
    public float hitRadius = 0.2f;

    [Header("Color")]
    public SpriteRenderer hookSprite;  // defaults to the SpriteRenderer on this object
    public Color defaultColor = Color.yellow;

    [Header("Rope (optional)")]
    public Transform rope;           // a Square sprite at the scene root
    public float ropeWidth = 0.05f;

    public bool IsBusy => state != State.Idle;

    private enum State { Idle, Extending, Retracting }
    private State state = State.Idle;

    private Vector2 origin;
    private Vector2 dir;
    private float maxLength;
    private float traveled;
    private Treasure carried;
    private SpriteRenderer ropeSprite;

    // Colliders this shot has already reacted to, so nothing is handled twice
    private readonly HashSet<Collider2D> touched = new HashSet<Collider2D>();

    void Awake()
    {
        if (hookSprite == null) hookSprite = GetComponent<SpriteRenderer>();
        if (rope != null) ropeSprite = rope.GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        if (rope != null) rope.gameObject.SetActive(false);
    }

    public void Fire(Vector2 from, Vector2 direction, float length)
    {
        Fire(from, direction, length, defaultColor);
    }

    // Called by MinerAI when the telegraph ends. Yellow = treasure shot, red = player shot.
    public void Fire(Vector2 from, Vector2 direction, float length, Color color)
    {
        if (IsBusy) return;

        origin = from;
        dir = direction.normalized;
        maxLength = length;
        traveled = 0f;
        carried = null;
        touched.Clear();

        transform.position = origin;
        // The Triangle sprite points up by default; turn it along the trajectory
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (hookSprite != null) hookSprite.color = color;
        if (ropeSprite != null) ropeSprite.color = color;

        state = State.Extending;
    }

    void Update()
    {
        if (state == State.Idle) return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

        if (state == State.Extending) Extend();
        else Retract();

        UpdateRope();
    }

    void Extend()
    {
        float step = Mathf.Min(extendSpeed * Time.deltaTime, maxLength - traveled);

        foreach (RaycastHit2D hit in CastAhead(dir, step))
        {
            if (!HandleHit(hit.collider)) continue;

            // First valid object on the path: stop here and pull back
            transform.position += (Vector3)(dir * hit.distance);
            state = State.Retracting;
            return;
        }

        transform.position += (Vector3)(dir * step);
        traveled += step;
        if (traveled >= maxLength - 0.001f) state = State.Retracting;
    }

    void Retract()
    {
        Vector2 pos = transform.position;
        float remaining = Vector2.Distance(pos, origin);
        float step = Mathf.Min(retractSpeed * RetractMultiplier() * Time.deltaTime, remaining);

        // An empty hook still reacts to what it touches on the way back;
        // once it carries something it ignores everything else
        if (carried == null && remaining > 0.0001f)
        {
            Vector2 back = (origin - pos) / remaining;
            foreach (RaycastHit2D hit in CastAhead(back, step))
            {
                HandleHit(hit.collider);
                if (carried != null) break;
            }
        }

        Vector2 next = Vector2.MoveTowards(pos, origin, step);
        transform.position = next;
        if ((next - origin).sqrMagnitude > 0.0001f) return;

        // Back at the miner (the miner was already paid in Treasure.GrabByHook)
        if (carried != null) Destroy(carried.gameObject);
        carried = null;
        state = State.Idle;
    }

    // Everything the hook would touch moving this far, nearest first, excluding itself
    List<RaycastHit2D> CastAhead(Vector2 direction, float distance)
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(transform.position, hitRadius, direction, distance);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        List<RaycastHit2D> result = new List<RaycastHit2D>(hits.Length);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            result.Add(hit);
        }
        return result;
    }

    // Returns true if this object is a valid stop (treasure, rock or pig)
    bool HandleHit(Collider2D col)
    {
        if (!touched.Add(col)) return false;

        Treasure treasure = col.GetComponent<Treasure>();
        if (treasure != null)
        {
            if (treasure.IsTaken) return false;
            treasure.GrabByHook(transform);
            carried = treasure;
            return true;
        }

        Rock rock = col.GetComponent<Rock>();
        if (rock != null)
        {
            rock.OnHookBlocked();
            return true;
        }

        if (col.GetComponent<PigController>() != null)
        {
            // No color check: any hook that touches the pig hurts it.
            // GameManager decides the outcome (lose a Life, or end the round).
            if (GameManager.Instance != null) GameManager.Instance.PlayerHit();
            return true;
        }

        return false; // walls, Life items: the hook passes through
    }

    // Heavier treasure is pulled back more slowly
    float RetractMultiplier()
    {
        if (carried == null) return 1f;

        switch (carried.type)
        {
            case TreasureType.SmallGold:  return smallGoldSpeed;
            case TreasureType.MediumGold: return mediumGoldSpeed;
            case TreasureType.LargeGold:  return largeGoldSpeed;
            case TreasureType.Diamond:    return diamondSpeed;
            default:                      return 1f;
        }
    }

    void UpdateRope()
    {
        if (rope == null) return;

        Vector2 tip = transform.position;
        float length = Vector2.Distance(origin, tip);

        rope.gameObject.SetActive(state != State.Idle && length > 0.01f);
        rope.position = (origin + tip) * 0.5f;
        rope.rotation = transform.rotation;
        rope.localScale = new Vector3(ropeWidth, length, 1f);
    }
}
