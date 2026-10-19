using UnityEngine;
using UnityEngine.SceneManagement;

// Opens the shop as its own scene. Continue returns to the game scene.
// Wallet and items stay in CrossLevelStatus, so a purchase is still there next level.
public class StoreScene : MonoBehaviour
{
    public string gameScene = "Game";

    void Start()
    {
        StoreController.OpenAfterWin();
    }

    void Update()
    {
        if (!StoreController.ConsumeContinue()) return;
        SceneManager.LoadScene(gameScene);
    }
}
