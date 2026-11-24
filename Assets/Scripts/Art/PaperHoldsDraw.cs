using UnityEngine;

[DefaultExecutionOrder(32002)]
public class PaperHoldsDraw : MonoBehaviour
{
    void LateUpdate() => PaperHolds.DrawNow();
}
