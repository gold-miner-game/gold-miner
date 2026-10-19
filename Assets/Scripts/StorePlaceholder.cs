using UnityEngine;

// The map's Store scene. Buying spends CrossLevelStatus wallet and arms that skill,
// so the next level actually changes (speed, digest, shield, or block).
// Leave goes back to the map without clearing the node, so the shop stays open.
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
        if (GameFlow.Instance != null) GameFlow.Instance.ReturnToMap();
    }
}
