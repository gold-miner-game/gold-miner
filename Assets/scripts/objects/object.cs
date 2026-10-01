using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
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

    void Start()
    {
        UpdateObjectProperties();
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

        // 2. Override the color immediately based on the ResourceType
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
}