using UnityEngine;

// Owner: Penny (shell) · characters by Yukun
// The character picker. Lists everything in CharacterRegistry. Picking one saves the choice, makes
// it the live CrossLevelStatus, and clears this node so the map unlocks the first battle.
public class RolePickController : MonoBehaviour
{
    GUIStyle title;
    GUIStyle name;
    GUIStyle desc;

    void OnGUI()
    {
        EnsureStyles();
        GUI.Label(new Rect(0, 24, Screen.width, 40), "PICK YOUR CHARACTER", title);

        var all = CharacterRegistry.All;
        float cardW = 320f;
        float cardH = 110f;
        float gap = 20f;
        float totalW = all.Count * cardW + (all.Count - 1) * gap;
        float startX = (Screen.width - totalW) * 0.5f;
        float y = Screen.height * 0.5f - cardH * 0.5f;

        for (int i = 0; i < all.Count; i++)
        {
            CharacterInfo info = all[i];
            Rect card = new Rect(startX + i * (cardW + gap), y, cardW, cardH);

            GUILayout.BeginArea(card, GUI.skin.box);
            GUILayout.Label(info.displayName, name);
            GUILayout.Label(info.description, desc);
            GUILayout.FlexibleSpace();
            bool chosen = info.id == ProgressStore.SelectedCharacter;
            if (GUILayout.Button(chosen ? "Selected \u2713" : "Choose"))
            {
                ProgressStore.SelectedCharacter = info.id;
                info.activate?.Invoke();
                GameFlow.Instance.CompleteCurrentNode();
            }
            GUILayout.EndArea();
        }
    }

    void EnsureStyles()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            title.normal.textColor = Color.white;
        }
        if (name == null)
        {
            name = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            name.normal.textColor = Color.white;
        }
        if (desc == null)
        {
            desc = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            desc.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        }
    }
}
