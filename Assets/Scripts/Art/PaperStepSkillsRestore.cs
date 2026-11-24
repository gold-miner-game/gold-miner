using UnityEngine;

// Like PaperPuppetRestore: restore before physics callbacks and gameplay Update.
// Undo Puppet first: its absolute eye snapshots include the posed catchlights and roll pivot.
// Then restore the true pivot and glints saved by this layer.
[DefaultExecutionOrder(-31999)]
public sealed class PaperStepSkillsRestore : MonoBehaviour
{
    internal PaperStepSkills skills;
    void FixedUpdate() { if (skills != null) skills.Restore(); }
    void Update() { if (skills != null) skills.Restore(); }
}
