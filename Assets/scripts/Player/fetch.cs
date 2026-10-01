using UnityEngine;
using UnityEngine.InputSystem;

// Put this on the Player. Hold Space while touching a fetchable to drag it along;
// release Space and it stops where it is.
[RequireComponent(typeof(player), typeof(Rigidbody2D), typeof(Collider2D))]
public class fetch : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Pushing strength. Speed while fetching = inputForce / object mass, capped at the player's moveSpeed.")]
    public float inputForce = 30f;
    [Tooltip("How far beyond the player's collider a fetchable can be and still be grabbed.")]
    public float grabRange = 0.1f;

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

        // Keep the object at the same spot relative to the player
        Vector2 next = body.position + body.linearVelocity * Time.fixedDeltaTime;
        fetchedBody.MovePosition(next + fetchOffset);
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
