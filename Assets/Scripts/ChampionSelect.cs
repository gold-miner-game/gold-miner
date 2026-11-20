using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Put this on an empty object in the Select ("WHO ARE U") scene. Each Select button calls Choose
// with its number in On Click ():
// 0 = Mr. Hyde, 1 = JK. Rolling, 2 = Scrat, 3 = Rero Rero, 4 = Elsa, 5 = Hulk.
// The name plates are labels. Picking starts a brand-new cross-level record (wallet, lives, skills).
// From the world map this scene is the RolePick node, so the pick is saved and the map comes back.
// Opened on its own (or by the old level list), it still loads the Game scene.
public class ChampionSelect : MonoBehaviour
{
    [Header("Scene to load after picking (must be in Build Profiles > Scene List)")]
    public string gameplayScene = "Game";

    Coroutine leaving;
    float openedAt;
    string unlockMessage = "";

    void Start()
    {
        openedAt = Time.unscaledTime;
        int shown = -1;
        if (ProgressStore.HasPickedCharacter
            && int.TryParse(ProgressStore.SelectedCharacter, out int picked))
            shown = picked;
        ShowSelected(shown);
        TutorialEvents.Fire(TutorialEvents.RolePickOpen);   // the scene's TutorialSteps listen from OnEnable
    }

    public void Choose(int champion)
    {
        if (leaving != null) return;                          // already picked, leaving
        if (TutorialDialogue.Playing) return;                 // a story is on screen: read it first
        if (DeveloperConsole.CapturesInput || champion < 0 || champion >= CharacterUnlocks.Count) return;
        if (!CharacterUnlocks.IsUnlocked(champion))
        {
            if (CharacterUnlocks.TryPurchase(champion, out unlockMessage))
                foreach (SkillPreview preview in FindObjectsByType<SkillPreview>(FindObjectsSortMode.None))
                    preview.RefreshLock();
            return;
        }
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
        PlayAnalytics.CharacterPicked(champion, Time.unscaledTime - openedAt);

        // A story on the pick plays out before the scene moves on
        TutorialEvents.Fire(TutorialEvents.CharacterPicked);
        TutorialEvents.Fire(TutorialEvents.Picked(champion));
        yield return null;
        while (TutorialDialogue.Playing) yield return null;

        GameFlow flow = FindFirstObjectByType<GameFlow>();
        if (flow != null && flow.CurrentNode != null && flow.CurrentNode.type == NodeType.RolePick)
        {
            flow.CompleteCurrentNode();
            yield break;
        }

        SceneManager.LoadScene(gameplayScene);
    }

    void OnGUI()
    {
        if (DeveloperConsole.CapturesInput) return;
        ScreenFrame.BeginGUI();
        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = false };
        GUI.Label(new Rect(24, 18, 350, 36), "Wallet: $" + MazeHubState.WalletBalance.ToString("N0"), style);
        style.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(24, 677, 1232, 30), unlockMessage, style);
    }

    void ShowSelected(int champion)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!button.name.StartsWith("Select ")) continue;
            if (SkillPreview.IsLocked(button)) continue;   // stays "Locked"
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;
            int index;
            if (!int.TryParse(button.name.Substring("Select ".Length), out index)) continue;
            bool on = index == champion;
            label.text = on ? "Selected" : "Select";
            ApplyButtonStyle(button, index, on);
        }
    }

    public static void ApplyButtonStyle(Button button, int champion, bool selected = false)
    {
        if (button == null) return;
        bool available = CharacterUnlocks.IsUnlocked(champion);
        bool selling = !available && CharacterUnlocks.Price(champion) > 0;
        Color background = available
            ? (selected ? new Color(.16f, .68f, .32f) : new Color(.10f, .48f, .23f))
            : selling ? new Color(.93f, .72f, .22f) : Color.black;
        Image fill = null;
        foreach (Image image in button.GetComponentsInChildren<Image>(true))
            if (image.name == "Fill") { fill = image; break; }
        if (fill == null) fill = button.targetGraphic as Image;
        if (fill != null)
        {
            fill.color = background;
            button.targetGraphic = fill;
        }
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.8f, .8f, .8f, 1f);
        colors.disabledColor = Color.white; // mystery buttons stay black when disabled
        button.colors = colors;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = selling ? new Color(.14f, .10f, .03f) : Color.white;
    }
}
