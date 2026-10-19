using UnityEngine;

// Owner: Penny (placeholder) -> Zhan Li replaces the contents
// A stand-in store so the map flow works end to end before the real shop lands. It only shows the
// wallet and a Leave button. The contract is in docs/level-map-tutorial-design.md section 6:
//   read/write CrossLevelStatus.Active (getWallet / addWallet / addSkill), then on leave call
//   GameFlow.Instance.CompleteCurrentNode() (or ReturnToMap to leave without clearing the node).
public class StorePlaceholder : MonoBehaviour
{
    GUIStyle title;

    void OnGUI()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            title.normal.textColor = Color.white;
        }

        GUI.Label(new Rect(0, 40, Screen.width, 36), "STORE (placeholder)", title);

        int wallet = CrossLevelStatus.Active != null ? CrossLevelStatus.Active.getWallet() : 0;
        GUI.Label(new Rect(0, 90, Screen.width, 28),
            "Wallet: $" + wallet.ToString("N0"), new GUIStyle(GUI.skin.label)
            { fontSize = 18, alignment = TextAnchor.MiddleCenter });

        float w = 220f;
        if (GUI.Button(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f, w, 44f), "Leave"))
            GameFlow.Instance.CompleteCurrentNode();
    }
}
