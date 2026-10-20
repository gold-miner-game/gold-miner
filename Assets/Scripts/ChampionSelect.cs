using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Put this on an empty object in the Select ("WHO ARE U") scene. Each Select button calls Choose
// with its number in On Click ():
// 0 = Mr. Hyde, 1 = JK. Rolling (cat), 2 = Scrat (squirrel), 3 = Rero Rero (frog)
// The name plates are labels. Picking starts a brand-new cross-level record (wallet, lives, skills).
// From the world map this scene is the RolePick node, so the pick is saved and the map comes back.
// Opened on its own (or by the old level list), it still loads the Game scene.
public class ChampionSelect : MonoBehaviour
{
    [Header("Scene to load after picking (must be in Build Profiles > Scene List)")]
    public string gameplayScene = "Game";

    Coroutine leaving;

    void Start()
    {
        int shown = -1;
        if (ProgressStore.HasPickedCharacter
            && int.TryParse(ProgressStore.SelectedCharacter, out int picked))
            shown = picked;
        ShowSelected(shown);
    }

    public void Choose(int champion)
    {
        ShowSelected(champion);
        if (leaving != null) StopCoroutine(leaving);
        leaving = StartCoroutine(Leave(champion));
    }

    // The word changes to Selected first, so the choice is obvious, then the scene moves on.
    IEnumerator Leave(int champion)
    {
        yield return new WaitForSecondsRealtime(0.45f);

        ChampionSpawner.Chosen = champion;
        CrossLevelStatus.StartFresh(ChampionSpawner.RecordType(champion));
        ProgressStore.SelectedCharacter = champion.ToString();
        PlayAnalytics.CharacterPicked(champion);

        GameFlow flow = FindFirstObjectByType<GameFlow>();
        if (flow != null && flow.CurrentNode != null && flow.CurrentNode.type == NodeType.RolePick)
        {
            flow.CompleteCurrentNode();
            yield break;
        }

        SceneManager.LoadScene(gameplayScene);
    }

    void ShowSelected(int champion)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!button.name.StartsWith("Select ")) continue;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;
            int index;
            if (!int.TryParse(button.name.Substring("Select ".Length), out index)) continue;
            bool on = index == champion;
            label.text = on ? "Selected" : "Select";
            label.color = on ? ChosenText : IdleText;
            Image fill = null;
            foreach (Image image in button.GetComponentsInChildren<Image>(true))
            {
                if (image.gameObject.name == "Fill") fill = image;
            }
            if (fill != null) fill.color = on ? ChosenFill : IdleFill;
        }
    }

    // Hollow blue with a white ring, so the button does not look like the solid name plate.
    // Chosen buttons fill in gold; the plate itself stays a plain white label.
    static readonly Color IdleFill = new Color(0.192f, 0.302f, 0.475f, 1f);
    static readonly Color ChosenFill = new Color(0.93f, 0.78f, 0.42f, 1f);
    static readonly Color IdleText = Color.white;
    static readonly Color ChosenText = new Color(0.16f, 0.12f, 0.06f, 1f);
}
