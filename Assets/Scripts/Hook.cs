using UnityEngine;

// Owner: D (Hook & UI)
public class Hook : MonoBehaviour
{
    [Header("Speed")]
    public float extendSpeed = 12f;
    public float retractSpeed = 16f;

    [Header("Collision")]
    public float hitRadius = 0.2f;

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

    void Start()
    {
        if (rope != null) rope.gameObject.SetActive(false);
    }

    // Called by MinerAI when the telegraph ends
    public void Fire(Vector2 from, Vector2 direction, float length)
    {
        if (IsBusy) return;

        origin = from;
        dir = direction.normalized;
        maxLength = length;
        traveled = 0f;
        carried = null;

        transform.position = origin;
        // The Triangle sprite points up by default; turn it along the trajectory
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

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

        // Everything the hook would touch during this step, nearest first
        RaycastHit2D[] hits = Physics2D.CircleCastAll(transform.position, hitRadius, dir, step);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit2D hit in hits)
        {
            if (!HandleHit(hit.collider)) continue;

            transform.position += (Vector3)(dir * hit.distance);
            state = State.Retracting;
            return;
        }

        transform.position += (Vector3)(dir * step);
        traveled += step;
        if (traveled >= maxLength - 0.001f) state = State.Retracting;
    }

    // Returns true if this object stops the hook
    bool HandleHit(Collider2D col)
    {
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
            // No color check: any hook that touches the pig is fatal
            // (a Life, if the build has any, absorbs one hit)
            if (GameManager.Instance == null || GameManager.Instance.IsInvincible) return false;

            GameManager.Instance.PlayerHit();
            return true;
        }

        return false; // walls, Life items: the hook passes through
    }

    void Retract()
    {
        Vector2 next = Vector2.MoveTowards(transform.position, origin, retractSpeed * Time.deltaTime);
        transform.position = next;
        if ((next - origin).sqrMagnitude > 0.0001f) return;

        // Back at the miner
        if (carried != null) Destroy(carried.gameObject);
        carried = null;
        state = State.Idle;
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
