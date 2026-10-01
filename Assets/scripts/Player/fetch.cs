using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Put this on the Player. Hold Space while touching a fetchable to drag it along;
// release Space and it stops where it is. Other fetchables it bumps into get pushed along.
[RequireComponent(typeof(player), typeof(Rigidbody2D), typeof(Collider2D))]
[DefaultExecutionOrder(100)] // Run after player.FixedUpdate sets the velocity, so we can limit it
public class fetch : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Pushing strength. Speed while fetching = inputForce / total mass of everything being moved, capped at the player's moveSpeed.")]
    public float inputForce = 30f;
    [Tooltip("How far beyond the player's collider a fetchable can be and still be grabbed.")]
    public float grabRange = 0.1f;

    // Gap kept between the fetched object and whatever it is blocked by
    private const float Skin = 0.01f;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D castFilter;

    // Bodies moving this step (fetched object first, then anything it pushes), per axis
    private readonly List<Rigidbody2D> xChain = new List<Rigidbody2D>();
    private readonly List<Rigidbody2D> yChain = new List<Rigidbody2D>();
    private readonly List<Rigidbody2D> diagonalChain = new List<Rigidbody2D>();
    private readonly Dictionary<Rigidbody2D, Vector2> pushMoves = new Dictionary<Rigidbody2D, Vector2>();

    private player playerMovement;
    private Rigidbody2D body;
    private Collider2D playerCollider;

    private GrabbableObject fetched;  // Fetchable we are dragging
    private Rigidbody2D fetchedBody;
    private Collider2D fetchedCollider;
    private Vector2 fetchOffset;

    void Awake()
    {
        playerMovement = GetComponent<player>();
        body = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool holdingSpace = keyboard != null && keyboard.spaceKey.isPressed;

        if (fetched == null)
        {
            GrabbableObject nearby = holdingSpace ? FindNearbyFetchable() : null;
            if (nearby != null)
            {
                StartFetch(nearby);
            }
        }
        else if (!holdingSpace || fetched.IsCaught)
        {
            // Let go on release, or if the hook grabbed it from us
            StopFetch();
        }
    }

    void FixedUpdate()
    {
        if (fetched == null)
        {
            return;
        }

        // The fetched object is kinematic, so physics won't stop it from going into other
        // objects. Check each axis ourselves (so it can still slide along things): fetchables
        // it touches get pushed along, anything else stops it. The player slows down to match.
        float dt = Time.fixedDeltaTime;
        Vector2 velocity = body.linearVelocity;
        velocity.x = LimitAxis(Vector2.right, velocity.x, dt, xChain);
        velocity.y = LimitAxis(Vector2.up, velocity.y, dt, yChain);

        // Corners: each axis may be free on its own but blocked when moving diagonally
        // (or the diagonal would touch something neither axis is pushing)
        float distance = velocity.magnitude * dt;
        if (velocity.x != 0f && velocity.y != 0f)
        {
            bool blocked = PushDistance(velocity.normalized, distance, diagonalChain) < distance;
            bool touchesOthers = diagonalChain.Exists(rb => !xChain.Contains(rb) && !yChain.Contains(rb));
            if (blocked || touchesOthers)
            {
                velocity.y = 0f;
                yChain.Clear();
            }
        }

        // Pushing more objects is slower: speed = inputForce / total mass, never faster than now
        float maxSpeed = inputForce / Mathf.Max(TotalPushedMass(), 0.0001f);
        if (velocity.magnitude > maxSpeed)
        {
            velocity = velocity.normalized * maxSpeed;
        }

        body.linearVelocity = velocity;

        // Keep the object at the same spot relative to the player
        Vector2 next = body.position + velocity * dt;
        fetchedBody.MovePosition(next + fetchOffset);

        // Move everything being pushed by the same amount along its axis
        pushMoves.Clear();
        AddPushMoves(xChain, new Vector2(velocity.x * dt, 0f));
        AddPushMoves(yChain, new Vector2(0f, velocity.y * dt));
        foreach (KeyValuePair<Rigidbody2D, Vector2> move in pushMoves)
        {
            move.Key.MovePosition(move.Key.position + move.Value);
        }
    }

    // Shrink the speed along one axis so the pushed chain stops right at whatever is in the way
    float LimitAxis(Vector2 axis, float speed, float dt, List<Rigidbody2D> chain)
    {
        if (speed == 0f)
        {
            chain.Clear();
            return 0f;
        }

        float sign = Mathf.Sign(speed);
        float allowed = PushDistance(axis * sign, Mathf.Abs(speed) * dt, chain);
        return sign * allowed / dt;
    }

    // How far the fetched object can move in a direction. Fetchables touching the front of it
    // (and touching those, and so on) join the chain and move with it; anything else blocks.
    // Fills chain with every body that would move, starting with the fetched object.
    float PushDistance(Vector2 direction, float distance, List<Rigidbody2D> chain)
    {
        chain.Clear();
        chain.Add(fetchedBody);
        float allowed = distance;

        for (int c = 0; c < chain.Count; c++)
        {
            int count = chain[c].Cast(direction, castFilter, castHits, distance + Skin);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == playerCollider || chain.Contains(hit.rigidbody))
                {
                    continue;
                }
                // Already touching but moving away from it: don't get stuck
                if (Vector2.Dot(hit.normal, direction) >= 0f)
                {
                    continue;
                }

                // Touching a free fetchable: push it too. Ones a little further ahead block
                // for now; we stop right next to them and they join the chain next step.
                bool touching = hit.distance <= Skin * 2f;
                if (touching && hit.rigidbody != null
                    && hit.collider.TryGetComponent(out GrabbableObject other) && !other.IsCaught)
                {
                    chain.Add(hit.rigidbody);
                    continue;
                }

                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - Skin));
            }
        }
        return allowed;
    }

    float TotalPushedMass()
    {
        float mass = fetchedBody.mass;
        foreach (Rigidbody2D rb in xChain)
        {
            if (rb != fetchedBody) mass += rb.mass;
        }
        foreach (Rigidbody2D rb in yChain)
        {
            if (rb != fetchedBody && !xChain.Contains(rb)) mass += rb.mass;
        }
        return mass;
    }

    void AddPushMoves(List<Rigidbody2D> chain, Vector2 delta)
    {
        foreach (Rigidbody2D rb in chain)
        {
            if (rb == fetchedBody)
            {
                continue; // Moved with the player above
            }
            pushMoves.TryGetValue(rb, out Vector2 total);
            pushMoves[rb] = total + delta;
        }
    }

    // Drop whatever we're holding if this component gets turned off (e.g. on game over)
    void OnDisable()
    {
        if (fetched != null)
        {
            StopFetch();
        }
    }

    void StartFetch(GrabbableObject target)
    {
        fetched = target;
        fetchedBody = target.GetComponent<Rigidbody2D>();
        fetchedCollider = target.GetComponent<Collider2D>();
        fetchOffset = fetchedBody.position - body.position;

        // Only solid colliders on layers the object would normally collide with can block it
        castFilter = new ContactFilter2D { useTriggers = false };
        castFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(target.gameObject.layer));

        // We move together, so don't let our own colliders block each other
        Physics2D.IgnoreCollision(playerCollider, fetchedCollider, true);

        // Heavier objects are slower to drag
        float mass = Mathf.Max(target.TotalWeight, 0.0001f);
        playerMovement.speedLimit = inputForce / mass;
    }

    void StopFetch()
    {
        // Object stops where it is; player goes back to normal speed
        if (fetchedCollider != null)
        {
            Physics2D.IgnoreCollision(playerCollider, fetchedCollider, false);
        }

        playerMovement.speedLimit = float.PositiveInfinity;
        fetched = null;
        fetchedBody = null;
        fetchedCollider = null;
    }

    // Find a fetchable right next to (or touching) the player
    GrabbableObject FindNearbyFetchable()
    {
        Bounds bounds = playerCollider.bounds;
        Vector2 size = (Vector2)bounds.size + Vector2.one * (grabRange * 2f);

        foreach (Collider2D hit in Physics2D.OverlapBoxAll(bounds.center, size, 0f))
        {
            if (hit.TryGetComponent(out GrabbableObject grabbable) && !grabbable.IsCaught)
            {
                return grabbable;
            }
        }
        return null;
    }
}
