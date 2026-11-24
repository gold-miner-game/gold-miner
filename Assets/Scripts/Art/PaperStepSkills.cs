using UnityEngine;

// Only the collider-free roll pivot is posed. FrostField2D (1000) must finish reading
// TRUE sprite matrices before this runs; PaperPuppet (32000) then poses its child parts.
[DefaultExecutionOrder(31000)]
public sealed class PaperStepSkills : MonoBehaviour
{
    CatController cat;
    Transform pivot;
    Quaternion trueRotation, heldRotation;
    bool safe, hasPose, applied;
    bool wasStepping;
    int heldFrame = int.MinValue;

    internal void Initialize(CatController owner, Transform visual)
    {
        cat = owner;
        pivot = visual;
        // Validate once, never query components or allocate arrays on render frames.
        // A future prefab with physics under the pivot falls back to the original smooth look.
        safe = pivot != null && pivot != cat.transform && pivot.IsChildOf(cat.transform)
            && pivot.GetComponentsInChildren<Collider2D>(true).Length == 0
            && pivot.GetComponentsInChildren<Rigidbody2D>(true).Length == 0;
        if (safe) gameObject.AddComponent<PaperStepSkillsRestore>().skills = this;
    }

    internal void SetPose(Quaternion rotation)
    {
        bool stepping = Application.isPlaying && StopMotion.Enabled;
        int frame = StopMotion.Frame;
        if (!stepping || !wasStepping || !hasPose || heldFrame != frame)
        {
            heldRotation = rotation;
            heldFrame = frame;
        }
        wasStepping = stepping;
        hasPose = true;
    }

    void LateUpdate()
    {
        if (!safe || cat == null || !cat.isActiveAndEnabled || !hasPose
            || !Application.isPlaying || !StopMotion.Enabled) return;
        trueRotation = pivot.localRotation;
        pivot.localRotation = heldRotation;
        applied = true;
        cat.ApplyHeldSpinCatchlights();
    }

    internal void Restore()
    {
        if (!applied) return;
        if (pivot != null) pivot.localRotation = trueRotation;
        if (cat != null) cat.RestoreSpinCatchlights();
        applied = false;
    }

    internal void CancelPose() { Restore(); hasPose = false; heldFrame = int.MinValue; }
    void OnDisable() { CancelPose(); }
    void OnDestroy() { CancelPose(); }
}

// Like PaperPuppetRestore: restore before physics callbacks and gameplay Update.
// The two restores write different local transforms (pivot versus ears/eyes/bubble),
// so their equal execution order cannot fight. Catchlight locals are also restored.
[DefaultExecutionOrder(-32000)]
public sealed class PaperStepSkillsRestore : MonoBehaviour
{
    internal PaperStepSkills skills;
    void FixedUpdate() { if (skills != null) skills.Restore(); }
    void Update() { if (skills != null) skills.Restore(); }
}
