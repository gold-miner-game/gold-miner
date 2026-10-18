using UnityEngine;

// Put this on the squirrel root. TailPivot's local +Y points along the resting tail.
// Only this visual child changes pose; physics, the head and face are unaffected.
[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(Rigidbody2D))]
[DefaultExecutionOrder(200)]
public class SquirrelTailHeading : MonoBehaviour
{
    public Transform tailPivot;
    [Tooltip("Matches the approved left/right pose. Set 90 for a tail exactly opposite horizontal movement.")]
    [Range(0f, 90f)] public float horizontalLean = 35f;
    [Tooltip("Larger values turn the tail faster. It follows momentum even after keys are released.")]
    [Min(0f)] public float followSpeed = 10f;
    [Min(0f)] public float stoppedSpeed = .05f;
    public bool returnUprightWhenStopped = true;
    [Tooltip("0 keeps the original tail pose. Increasing toward 90 turns in the reversed idle direction. Editable outside Play mode; movement is unaffected.")]
    [InspectorName("Whole Tail Idle Angle")]
    [Range(0f, 90f)] public float idleAngle = 60f;
    [Tooltip("Extra bend for ONLY the outer/large sprite, relative to Whole Tail Idle Angle. 0 keeps both sprites aligned. Idle only.")]
    [Range(-90f, 90f)] public float outerIdleOffset = 0f;
    [Tooltip("The outer/large Tail Crown sprite. Auto-found by name if empty.")]
    public Transform outerTail;
    [SerializeField, HideInInspector] private Transform tailBase;

    private Rigidbody2D body;
    // Keep the original moving pose separate from the saved, tilted idle pose.
    [SerializeField, HideInInspector] private Quaternion restRotation = Quaternion.identity;
    [SerializeField, HideInInspector] private bool hasRestRotation;
    [SerializeField, HideInInspector] private Vector3 restScale = Vector3.one;
    [SerializeField, HideInInspector] private bool hasRestScale;
    [SerializeField, HideInInspector] private Vector3 baseRestPosition;
    [SerializeField, HideInInspector] private Quaternion baseRestRotation = Quaternion.identity;
    [SerializeField, HideInInspector] private bool hasBaseRestPose;
    [SerializeField, HideInInspector] private Vector3 outerRestPosition;
    [SerializeField, HideInInspector] private Quaternion outerRestRotation = Quaternion.identity;
    [SerializeField, HideInInspector] private bool hasOuterRestPose;
    private float angle;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        EnsureRestRotation();
    }

    private void EnsureRestRotation()
    {
        if (tailPivot == null) return;
        if (!hasRestRotation)
        {
            restRotation = tailPivot.localRotation;
            hasRestRotation = true;
        }
        if (!hasRestScale)
        {
            restScale = tailPivot.localScale;
            hasRestScale = true;
        }
        if (tailBase == null) tailBase = tailPivot.Find("Tail Base");
        if (tailBase != null && !hasBaseRestPose)
        {
            baseRestPosition = tailBase.localPosition;
            baseRestRotation = tailBase.localRotation;
            hasBaseRestPose = true;
        }
        if (outerTail == null) outerTail = tailPivot.Find("Tail Crown");
        if (outerTail != null && !hasOuterRestPose)
        {
            outerRestPosition = outerTail.localPosition;
            outerRestRotation = outerTail.localRotation;
            hasOuterRestPose = true;
        }
    }

    private void RestoreBasePose()
    {
        if (tailBase == null || !hasBaseRestPose) return;
        tailBase.localPosition = baseRestPosition;
        tailBase.localRotation = baseRestRotation;
    }

    private void RestoreOuterPose()
    {
        if (outerTail == null || !hasOuterRestPose) return;
        outerTail.localPosition = outerRestPosition;
        outerTail.localRotation = outerRestRotation;
    }

    private void ApplyIdleTilt()
    {
        // Use the actual viewing side instead of assuming negative world Z.
        // The Scene view may look from the opposite side to the Game camera.
        Camera viewer = Camera.main;
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject) && UnityEditor.SceneView.lastActiveSceneView != null)
            viewer = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        float direction = -1f;
        if (viewer != null)
        {
            Vector3 towardViewer = viewer.orthographic
                ? -viewer.transform.forward
                : viewer.transform.position - tailPivot.position;
            if (tailPivot.parent != null)
                towardViewer = tailPivot.parent.InverseTransformDirection(towardViewer);
            towardViewer = Quaternion.Inverse(restRotation) * towardViewer;
            direction = towardViewer.z >= 0f ? 1f : -1f;
        }
        // Reverse the previous idle turn while keeping 0 exactly unchanged.
        // This affects only idle rotation; never invert the sprite scale.
        tailPivot.localScale = restScale;
        tailPivot.localRotation *= Quaternion.Euler(-direction * Mathf.Clamp(idleAngle, 0f, 90f), 0f, 0f);
        RestoreBasePose();
        RestoreOuterPose();
        if (outerTail != null && hasOuterRestPose)
        {
            // Both sprites inherit the whole-tail angle. Bend only the outer
            // piece about its lower end, leaving the base completely unchanged.
            Vector3 hinge = outerRestPosition;
            var renderer = outerTail.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Vector3 lowerEnd = new Vector3(renderer.sprite.bounds.center.x, renderer.sprite.bounds.min.y, 0f);
                hinge += outerRestRotation * Vector3.Scale(lowerEnd, outerTail.localScale);
            }
            Quaternion bend = Quaternion.Euler(-direction * Mathf.Clamp(outerIdleOffset, -90f, 90f), 0f, 0f);
            outerTail.localPosition = hinge + bend * (outerRestPosition - hinge);
            outerTail.localRotation = bend * outerRestRotation;
        }
    }

    // Heading measured from local +Y. Positive Z leans left on screen.
    public static float CalculateAngle(Vector2 velocity, float sideLean)
    {
        if (velocity.sqrMagnitude < .000001f) return 0f;
        float raw = Mathf.Atan2(velocity.x, -velocity.y) * Mathf.Rad2Deg;
        float magnitude = Mathf.Abs(raw);
        sideLean = Mathf.Clamp(sideLean, 0f, 90f);
        float mapped = magnitude <= 90f
            ? magnitude / 90f * sideLean
            : sideLean + (magnitude - 90f) / 90f * (180f - sideLean);
        return Mathf.Sign(raw) * mapped;
    }

    private void LateUpdate()
    {
        if (tailPivot == null) return;
        if (!Application.IsPlaying(gameObject))
        {
            EnsureRestRotation();
            tailPivot.localRotation = restRotation;
            ApplyIdleTilt();
            return;
        }
        if (Time.timeScale == 0f) return;
        Vector2 velocity = body.linearVelocity;
        bool idle = velocity.sqrMagnitude <= stoppedSpeed * stoppedSpeed;
        float target = angle;
        if (velocity.sqrMagnitude > stoppedSpeed * stoppedSpeed)
        {
            Vector3 local = tailPivot.parent != null
                ? tailPivot.parent.InverseTransformVector(new Vector3(velocity.x, velocity.y, 0f))
                : new Vector3(velocity.x, velocity.y, 0f);
            target = CalculateAngle(new Vector2(local.x, local.y), horizontalLean);
        }
        else if (returnUprightWhenStopped)
        {
            target = 0f;
        }
        angle = Mathf.LerpAngle(angle, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        tailPivot.localRotation = restRotation * Quaternion.Euler(0f, 0f, angle);
        tailPivot.localScale = restScale;
        RestoreBasePose();
        RestoreOuterPose();
        // Moving retains its original rotation AND full-length scale.
        if (idle) ApplyIdleTilt();
    }

    private void OnDisable()
    {
        angle = 0f;
        if (tailPivot != null && Application.IsPlaying(gameObject))
        {
            tailPivot.localRotation = restRotation;
            tailPivot.localScale = restScale;
            RestoreBasePose();
            RestoreOuterPose();
        }
    }
}
