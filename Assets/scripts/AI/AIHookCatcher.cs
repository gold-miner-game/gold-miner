using UnityEngine;

// Lives on the hook head when the Miner uses AIMinerHook. Same job as HookCatcher, but reports to
// AIMinerHook. AIMinerHook adds it automatically if it is missing.
[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class AIHookCatcher : MonoBehaviour
{
    [HideInInspector] public AIMinerHook owner;

    void Awake()
    {
        // Moved by the hook script, not by physics, and passes through objects
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.useFullKinematicContacts = true; // Fetchables are kinematic too
        GetComponent<Collider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (owner == null)
        {
            return;
        }

        if (other.TryGetComponent(out GrabbableObject mineral))
        {
            owner.TouchedMineral(mineral);
        }
        else if (other.TryGetComponent(out die victim))
        {
            owner.TouchedPlayer(victim);
        }
    }
}
