using UnityEngine;
using UnityEngine.SceneManagement;

// Put this on an empty object in the Select ("WHO ARE U") scene. LevelLoader opens that scene when
// the tutorial is over. Each champion Button calls Choose with its number in On Click ():
// 0 = Mr. Hyde, 1 = JK. Rolling (cat), 2 = Scrat (squirrel), 3 = Rero Rero (frog)
// Picking is when the real game starts: the champion gets a brand-new cross-level record (wallet,
// lives, skills), and nothing from the tutorial or an earlier run carries over.
public class ChampionSelect : MonoBehaviour
{
    [Header("Scene to load after picking (must be in Build Profiles > Scene List)")]
    public string gameplayScene = "Game";

    public void Choose(int champion)
    {
        ChampionSpawner.Chosen = champion;
        CrossLevelStatus.StartFresh(ChampionSpawner.RecordType(champion));
        SceneManager.LoadScene(gameplayScene);
    }
}
