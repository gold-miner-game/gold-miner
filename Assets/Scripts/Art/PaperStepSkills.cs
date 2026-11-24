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
    bool wasStepping, ending;
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
        ending = false;
        wasStepping = stepping;
        hasPose = true;
    }

    void LateUpdate()
    {
        // FinishSpin restores the true base immediately; the picture finishes on the next pose.
        if (ending && heldFrame != StopMotion.Frame)
        { ending = false; hasPose = false; return; }
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

    internal void CancelPose()
    {
        Restore();
        if (safe && hasPose && Application.isPlaying && StopMotion.Enabled) { ending = true; return; }
        ClearPose();
    }
    void ClearPose() { Restore(); ending = hasPose = false; heldFrame = int.MinValue; }
    void OnDisable() { ClearPose(); }
    void OnDestroy() { ClearPose(); }
}
