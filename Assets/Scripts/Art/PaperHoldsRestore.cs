using UnityEngine;

[DefaultExecutionOrder(-32001)]
public class PaperHoldsRestore : MonoBehaviour
{
    void FixedUpdate() => PaperHolds.RestoreNow();
    void Update() => PaperHolds.RestoreNow();
}
