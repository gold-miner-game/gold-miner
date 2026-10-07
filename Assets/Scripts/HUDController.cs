using TMPro;
using UnityEngine;

// Owner: D (Hook & UI)
public class HUDController : MonoBehaviour
{
    public TMP_Text playerMoneyText;
    public TMP_Text minerMoneyText;
    public TMP_Text timerText;
    public TMP_Text livesText;      // optional

    public GameObject endPanel;
    public TMP_Text resultText;
    public TMP_Text detailText;

    void Start()
    {
        if (endPanel != null) endPanel.SetActive(false);
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        playerMoneyText.text = "Player: $" + gm.PlayerMoney.ToString("N0");
        minerMoneyText.text = "Miner: $" + gm.MinerMoney.ToString("N0");
        timerText.text = Mathf.CeilToInt(gm.TimeLeft).ToString();
        if (livesText != null) livesText.text = "Life: " + gm.Lives;

        if (!gm.IsPlaying && endPanel != null && !endPanel.activeSelf)
        {
            endPanel.SetActive(true);
            resultText.text = gm.PlayerWon ? "YOU WIN" : "YOU LOSE";
            detailText.text = gm.EndReason + "\nPress R to restart";
        }
    }
}
