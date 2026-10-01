using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(Rigidbody2D))]
public class GrabbableObject : MonoBehaviour
{
    [Header("Object Properties")]
    public ResourceType resourceType; // Drag your Gold, Rock, or Diamond asset here

    [Header("Calculated Values (Do not edit)")]
    private float volume; 

    // Calculated properties (Read-only)
    // Added null checks to prevent errors if ResourceType is missing
    public float TotalValue => resourceType != null ? resourceType.valuePerVolume * volume : 0f;
    public float TotalWeight => resourceType != null ? resourceType.densityPerVolume * volume : 0f;

    private SpriteRenderer spriteRenderer;
    private Rigidbody2D body;

    // Set while a hook is holding this object
    private Transform caughtBy;
    private Vector3 catchOffset;
    public bool IsCaught => caughtBy != null;

    void Start()
    {
        UpdateObjectProperties();

        // Solid and immovable: the player bumps into it and stops instead of pushing it.
        // The hook is a trigger, so it still detects (and catches) solid objects.
        body.bodyType = RigidbodyType2D.Kinematic;
        GetComponent<Collider2D>().isTrigger = false;
    }

    // OnValidate runs automatically in the Unity Editor whenever you change a value, 
    // move the object, or scale it. This makes the color and volume update instantly without hitting Play.
    void OnValidate()
    {
        UpdateObjectProperties();
    }

    private void UpdateObjectProperties()
    {
        // 1. Automatically calculate volume from Scale X * Scale Y
        volume = transform.localScale.x * transform.localScale.y;

        // 2. Use the weight as the physics mass
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (body != null)
        {
            body.useAutoMass = false;
            body.mass = Mathf.Max(TotalWeight, 0.0001f); // Rigidbody2D mass must be positive
        }

        // 3. Override the color immediately based on the ResourceType
        if (resourceType != null)
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = resourceType.baseColor;
            }
        }
    }

    // Called by the hook when it touches this object
    public void Catch(Transform hook)
    {
        caughtBy = hook;
        catchOffset = transform.position - hook.position;

        // Let the hook carry it: no more physics pushing it around or catching it twice
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.bodyType = RigidbodyType2D.Kinematic;
        GetComponent<Collider2D>().enabled = false;
    }

    // Called by the hook once it has pulled this object back to the miner
    public void Collect()
    {
        Debug.Log($"Collected {name}: value {TotalValue}, weight {TotalWeight}");
        Destroy(gameObject);
    }

    void LateUpdate()
    {
        // Follow the hook while caught
        if (caughtBy != null)
        {
            transform.position = caughtBy.position + catchOffset;
        }
    }
}
