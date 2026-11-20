using UnityEngine;

// Movement/artwork only. The independent loadout chooses and creates the K skill at runtime.
public class PenguinController : PlayerCharacter
{
    public override bool CanEatFrozenTreasure => true;
    [Header("Facing costs")]
    [Min(0f)] public float flipCost = .5f;
    [Min(0f)] public float rotationCostPer45 = 1f;
    [Header("Facing")]
    public Transform visualRoot;
    [Min(0f), Tooltip("Time to smoothly finish a nearby turn after its first 45 degrees. Quick taps keep the diagonal heading; movement is unchanged.")]
    public float nearbyTurnDelay = .12f;
    Vector2 headingInput;
    bool finishingNearbyTurn;
    float finishHeadingAt;
    Vector2 nearbyHeading;
    RuntimeSkillSlot skillSlot;
    public RuntimeSkillSlot SkillSlot => skillSlot;
    public override bool HasSkill => skillSlot != null && skillSlot.ActiveSkill != null;
    public override string SkillName => HasSkill ? skillSlot.ActiveSkill.SkillName : "";
    public override bool SkillActive => HasSkill && skillSlot.ActiveSkill.IsActive;
    public override float SkillReadyIn => HasSkill ? skillSlot.ActiveSkill.ReadyIn : 0f;
    public override float SkillWait => HasSkill ? skillSlot.ActiveSkill.Wait : 0f;
    protected override bool FlipsWhenTurning => false;

    protected override void Awake()
    {
        if (visualRoot == null) visualRoot = transform.Find("Artwork");
        base.Awake(); InitializeSkills();
    }
    public void InitializeSkills()
    {
        if (skillSlot != null) return;
        skillSlot = GetComponent<RuntimeSkillSlot>();
        if (skillSlot == null) skillSlot = gameObject.AddComponent<RuntimeSkillSlot>();
        skillSlot.Initialize(this, "elsa");
        if (skillSlot.ActiveSkill == null) GetComponent<ElsaMouthPose>()?.SetPose(false,0f);
    }
    protected override void UseSkill() { skillSlot?.TryActivate(); }
    protected override Vector2 Velocity(Vector2 input)
    {
        return HasSkill && skillSlot.ActiveSkill.BlocksMovement ? Vector2.zero : base.Velocity(input);
    }
    protected override void OnStopped()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) skillSlot?.Cancel();
    }
    void OnDisable() { skillSlot?.Cancel(); headingInput = Vector2.zero; finishingNearbyTurn = false; }
    void LateUpdate() { UpdateHeading(); }

    void UpdateHeading()
    {
        if (visualRoot == null || !IsPlaying || HasSkill && skillSlot.ActiveSkill.BlocksMovement) return;
        // Facing follows current key intent, not the previous physics step's velocity.
        Vector2 direction = MoveInput.normalized;
        if (direction.sqrMagnitude < .000001f)
        {
            if (finishingNearbyTurn) ApplyHeading(nearbyHeading);
            headingInput = Vector2.zero; finishingNearbyTurn = false;
            return; // Releasing a quick tap keeps the first 45-degree step.
        }
        bool changed = (direction - headingInput).sqrMagnitude > .000001f;
        bool singleKeyDirection = Mathf.Abs(direction.x) < .0001f || Mathf.Abs(direction.y) < .0001f;
        if (changed)
        {
            headingInput = direction; finishingNearbyTurn = false;
            Vector2 facing = visualRoot.TransformVector(Vector3.left);
            float turn = Vector2.SignedAngle(facing, direction);
            if (singleKeyDirection && nearbyTurnDelay > 0f && Mathf.Abs(turn) > 45.01f && Mathf.Abs(turn) <= 90.01f)
            {
                finishingNearbyTurn = true;
                finishHeadingAt = Time.time + nearbyTurnDelay;
                nearbyHeading = Quaternion.Euler(0f, 0f, Mathf.Sign(turn) * 45f) * facing;
                ApplyHeading(nearbyHeading);
                return;
            }
        }
        if (finishingNearbyTurn && Time.time < finishHeadingAt)
        {
            float progress = Mathf.Clamp01(1f - (finishHeadingAt - Time.time) / Mathf.Max(.0001f, nearbyTurnDelay));
            ApplyHeading(Quaternion.Euler(0f, 0f, Vector2.SignedAngle(nearbyHeading, direction) * progress) * nearbyHeading);
            return;
        }
        finishingNearbyTurn = false;
        ApplyHeading(direction);
    }

    void ApplyHeading(Vector2 direction)
    {
        Vector3 local = visualRoot.parent != null
            ? visualRoot.parent.InverseTransformVector(direction)
            : (Vector3)direction;
        float target = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        Vector3 scale = visualRoot.localScale;
        bool mirrored = scale.x < 0f; // The source beak faces left; a mirror faces right.
        float current = visualRoot.localEulerAngles.z;
        float keepAngle = target - (mirrored ? 0f : 180f);
        float flipAngle = target - (mirrored ? 180f : 0f);
        float turnWeight = Mathf.Max(0f, rotationCostPer45) / 45f;
        float keepCost = Mathf.Abs(Mathf.DeltaAngle(current, keepAngle)) * turnWeight;
        float changeCost = Mathf.Max(0f, flipCost)
            + Mathf.Abs(Mathf.DeltaAngle(current, flipAngle)) * turnWeight;
        // Both poses point the beak correctly. Prefer no flip on a tie to avoid jitter.
        bool flip = changeCost < keepCost - .0001f;
        if (flip) scale.x = -scale.x;
        visualRoot.localScale = scale;
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, flip ? flipAngle : keepAngle);
    }
}
