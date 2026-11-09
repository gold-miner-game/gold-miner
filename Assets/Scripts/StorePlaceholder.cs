using UnityEngine;

// The map's Store scene. Buying spends CrossLevelStatus wallet and arms that skill,
// so the next level actually changes (speed, digest, shield, or block).
// Leave clears the node (the next node on the map unlocks) and goes back to the map; a cleared
// node stays open, so the shop can be visited again.
public class StorePlaceholder : MonoBehaviour
{
    void Start()
    {
        CrossLevelStatus.Ensure();
        StoreView.Show(Leave);
    }

    static void Leave()
    {
        StoreView.Hide();
        if (GameFlow.Instance != null) GameFlow.Instance.CompleteCurrentNode();
    }
}
