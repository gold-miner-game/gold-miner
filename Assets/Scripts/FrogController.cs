using UnityEngine;

// The frog (Rero Rero). Eats, digests, blinks and gets hit like every character (PlayerCharacter), at the
// level's speed. Moves like Penny's FrogMovement: the whole round artwork turns so the mouth (local +Y
// of visualRoot) faces where it goes. When it starts moving or changes direction it stops and turns
// for turnOnlyDuration first, then moves. It never flips.
// Its skill is the tongue (FrogTongue): Space shoots it out of the mouth to grab a treasure. The frog
// keeps moving and turning meanwhile, and the tongue swings with it.
public class FrogController : PlayerCharacter
{
    [Header("Turning")]
    [Tooltip("The complete artwork (and its leg mask), turned to face the movement. Local +Y is the mouth")]
    public Transform visualRoot;
    [Tooltip("Seconds it stands still and turns when starting or changing direction, before moving")]
    [Min(0f)] public float turnOnlyDuration = .05f;

    public bool IsTurning => heading.sqrMagnitude > 0f && Time.time < moveAllowedAt;
    public override string SkillHint => tongue != null ? "[Space] tongue" : "";
    public bool TongueOut => tongue != null && tongue.IsUsingSkill;

    protected override bool FlipsWhenTurning => false;

    private Vector2 heading;       // the direction asked for (normalized WASD)
    private float turnStartedAt;
    private float moveAllowedAt;
    private Quaternion turnStartRotation;
    private FrogTongue tongue;

    protected override void Awake()
    {
        base.Awake();
        tongue = GetComponent<FrogTongue>();
    }

    protected override void UseSkill()
    {
        if (tongue != null) tongue.Fire();
    }

    protected override void Update()
    {
        base.Update();
        Vector2 requested = MoveInput.normalized;
        if (requested != heading)
        {
            // Stop at once: a new heading must not slide on in the old direction
            Body.linearVelocity = Vector2.zero;
            turnStartedAt = Time.time;
            moveAllowedAt = requested.sqrMagnitude > 0f ? turnStartedAt + Mathf.Max(0f, turnOnlyDuration) : turnStartedAt;
            if (visualRoot != null) turnStartRotation = visualRoot.localRotation;
        }
        heading = requested;
    }

    // No moving during a turn; otherwise the normal speed (also while the tongue is out)
    protected override Vector2 Velocity(Vector2 input)
    {
        return Time.fixedTime < moveAllowedAt ? Vector2.zero : base.Velocity(input);
    }

    void LateUpdate()
    {
        if (visualRoot == null || !IsPlaying) return;
        Vector2 velocity = Body.linearVelocity;
        if (velocity.sqrMagnitude < .000001f)
        {
            if (heading.sqrMagnitude <= 0f) return;   // keep facing the same way when idle
            float length = Mathf.Max(.00001f, moveAllowedAt - turnStartedAt);
            float progress = Mathf.Clamp01((Time.time - turnStartedAt) / length);
            visualRoot.localRotation = Quaternion.Slerp(turnStartRotation, FacingRotation(heading), progress);
            return;
        }
        // Moving: face exactly where it goes, diagonals and sliding along obstacles included
        visualRoot.localRotation = FacingRotation(velocity);
    }

    Quaternion FacingRotation(Vector2 direction)
    {
        Vector3 local = visualRoot.parent != null
            ? visualRoot.parent.InverseTransformVector(new Vector3(direction.x, direction.y, 0f))
            : new Vector3(direction.x, direction.y, 0f);
        float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg - 90f;
        return Quaternion.Euler(0f, 0f, angle);   // the mouth is local +Y
    }

    void OnDisable()
    {
        heading = Vector2.zero;
        moveAllowedAt = 0f;
        if (Body != null) Body.linearVelocity = Vector2.zero;
    }
}
