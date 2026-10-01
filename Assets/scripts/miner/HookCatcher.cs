using UnityEngine;

// Lives on the hook head. Detects minerals the hook touches and hands them to MinerHook.
[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class HookCatcher : MonoBehaviour
{
    [HideInInspector] public MinerHook owner;

    void Awake()
    {
        // The hook is moved by MinerHook, not by physics, and should pass through objects
        // instead of shoving them (or being blocked by them).
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.useFullKinematicContacts = true; // Also detect kinematic objects (fetchables are kinematic)
        GetComponent<Collider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        GrabbableObject grabbable = other.GetComponent<GrabbableObject>();
        if (grabbable != null && owner != null)
        {
            owner.TryCatch(grabbable);
        }
    }
}
