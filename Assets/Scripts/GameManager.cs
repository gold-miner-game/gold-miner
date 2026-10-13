using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: A (Captain). Every other script depends on this.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Round")]
    public float roundTime = 90f;
    public int startingLives = 0;
    public float invincibleTime = 1.5f;   // after losing a Life

    public int PlayerMoney { get; private set; }
    public int MinerMoney { get; private set; }
    public int Lives { get; private set; }
    public float TimeLeft { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool PlayerWon { get; private set; }
    public string EndReason { get; private set; }
    public bool IsInvincible => Time.time < invincibleUntil;

    private float invincibleUntil;
    private bool hadTreasure;

    void Awake()
    {
        Instance = this;
        TimeLeft = roundTime;
        Lives = startingLives;
        IsPlaying = true;
    }

    void Start()
    {
        // An empty Dev scene should not end the round on the first frame
        hadTreasure = Treasure.Active.Count > 0;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (!IsPlaying) return;

        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f)
        {
            TimeLeft = 0f;
            EndRound(false, "Time's up!");
        }
        else if (hadTreasure && Treasure.Active.Count == 0 && Treasure.OnHooks.Count == 0)
        {
            EndRound(false, "All treasure collected!");
        }
    }

    public void AddPlayerMoney(int amount) { if (IsPlaying) PlayerMoney += amount; }
    public void AddMinerMoney(int amount)  { if (IsPlaying) MinerMoney += amount; }
    public void AddLife()                  { if (IsPlaying) Lives++; }

    // Called by Hook when it touches the pig
    public void PlayerHit()
    {
        if (!IsPlaying || IsInvincible) return;

        if (Lives > 0)
        {
            Lives--;
            invincibleUntil = Time.time + invincibleTime;
        }
        else
        {
            EndRound(true, "Killed by the hook!");
        }
    }

    void EndRound(bool killed, string reason)
    {
        IsPlaying = false;
        EndReason = reason;
        PlayerWon = !killed && PlayerMoney > MinerMoney;   // a tie is a loss
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
