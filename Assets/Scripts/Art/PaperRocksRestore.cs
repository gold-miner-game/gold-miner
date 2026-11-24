using UnityEngine;

// Art: gives every hidden rock its real alpha back before any gameplay runs (FixedUpdate and Update), so
// rock fragments and charge-smashed blocks that copy the rock's colour are visible. PaperRocks hides the
// rock again in LateUpdate, for drawing only. Its own file: the Editor reads execution order per script file.
[DefaultExecutionOrder(-32001)]
public class PaperRocksRestore : MonoBehaviour
{
    void FixedUpdate() => PaperRocks.RestoreNow();
    void Update() => PaperRocks.RestoreNow();
}
