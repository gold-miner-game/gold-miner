using UnityEngine;

// A 2D approximation of a shallow 3D turn: the face slides and foreshortens
// while the head silhouette, Rigidbody2D and collider remain unchanged.
[DisallowMultipleComponent]
public class SquirrelFaceHeading : MonoBehaviour
{
    public Transform leftEye;
    public Transform rightEye;
    public Transform leftTooth;
    public Transform rightTooth;

    [Header("Face turn (local units before root scaling)")]
    [Range(0f, .65f)] public float eyeShift = .24f;
    [Range(0f, .65f)] public float toothShift = .42f;
    [Range(0f, .6f)] public float eyeSpacingCompression = .16f;
    [Range(0f, .5f)] public float farEyeCompression = .12f;
    [Min(0f)] public float turnSpeed = 12f;
    [Range(-1f, 1f)] public float initialHeading;

    private Transform[] parts;
    private Vector3[] restPositions;
    private Vector3[] restScales;
    private SquirrelController movement;
    private Rigidbody2D body;
    private float target;
    private float turn;

    private void Awake()
    {
        movement = GetComponent<SquirrelController>();
        body = GetComponent<Rigidbody2D>();
        parts = new[] { leftEye, rightEye, leftTooth, rightTooth };
        restPositions = new Vector3[parts.Length];
        restScales = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            restPositions[i] = parts[i].localPosition;
            restScales[i] = parts[i].localScale;
        }
        turn = target = initialHeading;
    }

    public void SetHeading(float horizontal)
    {
        target = Mathf.Abs(horizontal) > .01f ? Mathf.Sign(horizontal) : 0f;
    }

    private void LateUpdate()
    {
        if (Time.timeScale == 0f) return;
        float horizontal = movement != null && movement.enabled
            ? movement.MoveInput.x : body != null ? body.linearVelocity.x : 0f;
        SetHeading(horizontal);
        // No horizontal input restores exact symmetry immediately, including vertical-only movement.
        turn = target == 0f ? 0f : Mathf.Lerp(turn, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            Vector3 position = restPositions[i];
            Vector3 scale = restScales[i];
            bool eye = i < 2;
            position.x *= 1f - Mathf.Abs(turn) * (eye ? eyeSpacingCompression : 0f);
            position.x += turn * (eye ? eyeShift : toothShift);
            if (eye && restPositions[i].x * turn < 0f)
                scale.x *= 1f - Mathf.Abs(turn) * farEyeCompression;
            parts[i].localPosition = position;
            parts[i].localScale = scale;
        }
    }

    private void OnDisable()
    {
        if (parts == null) return;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            parts[i].localPosition = restPositions[i];
            parts[i].localScale = restScales[i];
        }
    }
}
