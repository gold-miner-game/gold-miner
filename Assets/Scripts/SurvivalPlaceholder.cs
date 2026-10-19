using UnityEngine;

// Owner: Penny (placeholder) -> Butian replaces the contents
// The map node after Level 8. The rules of survival mode are not written yet; this scene only
// keeps the flow from breaking so a teammate can drop the real mode in. Contract:
//   the picked champion's CrossLevelStatus is already active when this scene loads,
//   and leaving calls GameFlow.Instance.ReturnToMap() (or CompleteCurrentNode once a run can be won).
public class SurvivalPlaceholder : MonoBehaviour
{
    GUIStyle title;
    GUIStyle body;

    void OnGUI()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            title.normal.textColor = Color.white;
            body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true,
            };
            body.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }

        GUI.Label(new Rect(0, 80, Screen.width, 40), "SURVIVE MODE", title);
        GUI.Label(new Rect(Screen.width * 0.5f - 280f, 140, 560f, 64),
            "Placeholder. The survival rules are left for a teammate to write.", body);

        float w = 220f;
        if (GUI.Button(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f, w, 44f), "Back to map"))
            GameFlow.Instance.ReturnToMap();
    }
}
