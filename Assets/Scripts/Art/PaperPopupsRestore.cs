using UnityEngine;

// Art: runs before everything else each frame so HUDController's cue values are its own again before its
// Update (PaperPopups shows them held on the stop-motion beat in LateUpdate)
[DefaultExecutionOrder(-32001)]
public class PaperPopupsRestore : MonoBehaviour
{
    void Update() => PaperPopups.RestoreNow();
}
