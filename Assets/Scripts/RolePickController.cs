using UnityEngine;

// Owner: Penny (map flow) · characters by Yukun / Zhan Li (#12)
// The map's RolePick node. Lists the champions from #12's character-select system and, on pick,
// starts a fresh cross-level record for that champion and tells ChampionSpawner which one the Game
// scene should swap in. Then it clears this node so the map unlocks the first battle.
// Champion order must match ChampionSpawner.champions[]: 0 Mr. Hyde, 1 JK. Rolling, 2 Scrat, 3 Rero Rero.
public class RolePickController : MonoBehaviour
{
    struct Champion { public int index; public string name; public string desc; }

    static readonly Champion[] Champions =
    {
        new Champion { index = 0, name = "Mr. Hyde",    desc = "The pig. Space blocks a hook and knocks gold off a passing one." },
        new Champion { index = 1, name = "JK. Rolling", desc = "The cat. Rolls to dash through hooks unharmed." },
        new Champion { index = 2, name = "Scrat",       desc = "The squirrel. Sweeps up treasure with its tail." },
        new Champion { index = 3, name = "Rero Rero",   desc = "The frog. A long tongue grabs gold from afar." },
    };

    GUIStyle title;
    GUIStyle nameStyle;
    GUIStyle descStyle;

    void OnGUI()
    {
        EnsureStyles();
        GUI.Label(new Rect(0, 24, Screen.width, 40), "PICK YOUR CHARACTER", title);

        float cardW = 300f;
        float cardH = 150f;
        float gap = 18f;
        float totalW = Champions.Length * cardW + (Champions.Length - 1) * gap;
        float startX = (Screen.width - totalW) * 0.5f;
        float y = Screen.height * 0.5f - cardH * 0.5f;

        string selected = ProgressStore.SelectedCharacter;
        for (int i = 0; i < Champions.Length; i++)
        {
            Champion c = Champions[i];
            Rect card = new Rect(startX + i * (cardW + gap), y, cardW, cardH);

            GUILayout.BeginArea(card, GUI.skin.box);
            GUILayout.Label(c.name, nameStyle);
            GUILayout.Label(c.desc, descStyle);
            GUILayout.FlexibleSpace();
            bool chosen = selected == c.index.ToString();
            if (GUILayout.Button(chosen ? "Selected \u2713" : "Choose", GUILayout.Height(32f)))
                Pick(c.index);
            GUILayout.EndArea();
        }
    }

    void Pick(int index)
    {
        // #12's machinery: a brand-new record for this champion, and the Game scene swaps it in.
        ChampionSpawner.Chosen = index;
        CrossLevelStatus.StartFresh(ChampionSpawner.RecordType(index));
        ProgressStore.SelectedCharacter = index.ToString();
        GameFlow.Instance.CompleteCurrentNode();
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
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            nameStyle.normal.textColor = Color.white;
        }
        if (descStyle == null)
        {
            descStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            descStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        }
    }
}
