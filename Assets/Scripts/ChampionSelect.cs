using UnityEngine;
using UnityEngine.SceneManagement;

// Put this on an empty object in the Select ("WHO ARE U") scene. Each champion Button calls Choose
// with its number in On Click ():
// 0 = Mr. Hyde, 1 = JK. Rolling (cat), 2 = Scrat (squirrel), 3 = Rero Rero (frog)
// Picking starts a brand-new cross-level record (wallet, lives, skills). From the world map this
// scene is the RolePick node, so the pick is saved and the map comes back. Opened on its own
// (or by the old level list), it still loads the Game scene.
public class ChampionSelect : MonoBehaviour
{
    [Header("Scene to load after picking (must be in Build Profiles > Scene List)")]
    public string gameplayScene = "Game";

    public void Choose(int champion)
    {
        ChampionSpawner.Chosen = champion;
        CrossLevelStatus.StartFresh(ChampionSpawner.RecordType(champion));
        ProgressStore.SelectedCharacter = champion.ToString();

        GameFlow flow = FindFirstObjectByType<GameFlow>();
        if (flow != null && flow.CurrentNode != null && flow.CurrentNode.type == NodeType.RolePick)
        {
            flow.CompleteCurrentNode();
            return;
        }

        SceneManager.LoadScene(gameplayScene);
    }
}
