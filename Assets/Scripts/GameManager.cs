using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: A (Captain). Every other script depends on this.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Round")]
    public float roundTime = 90f;
    public int startingLives = 0;         // the hit that takes the last Life ends the round
    public float invincibleTime = 1.5f;   // after losing a Life

    public int PlayerMoney { get; private set; }
    public int MinerMoney { get; private set; }
    public int Lives { get; private set; }
    public float TimeLeft { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool PlayerWon { get; private set; }
    public string EndReason { get; private set; }
    public bool IsInvincible => Time.time < invincibleUntil;

    // Maze world map: no countdown, and eating all gold must not end the round (gate does).
    public bool UseRoundTimer = true;
    public bool EndWhenTreasureGone = true;

    public void ConfigureTimer(float seconds, bool countdown)
    {
        roundTime = seconds;
        TimeLeft = seconds;
        UseRoundTimer = countdown;
    }

    private float invincibleUntil;
    private bool hadTreasure;

    void Awake()
    {
        Instance = this;
        TimeLeft = roundTime;
        Lives = startingLives;
        IsPlaying = true;
        PlayAnalytics.BeginLevel();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

        if (UseRoundTimer)
        {
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                EndRound(false, "Time's up!");
                return;
            }
        }

        if (EndWhenTreasureGone && hadTreasure && Treasure.Active.Count == 0 && Treasure.OnHooks.Count == 0)
            EndRound(false, "All treasure collected!");
    }

    public void AddPlayerMoney(int amount) { if (IsPlaying) PlayerMoney += amount; }
    public void AddMinerMoney(int amount)  { if (IsPlaying) MinerMoney += amount; }

    // Maze hub restore after returning from a battle (bypasses IsPlaying gate).
    public void RestoreHubTotals(int playerMoney, int lives)
    {
        PlayerMoney = Mathf.Max(0, playerMoney);
        if (lives > 0) Lives = lives;
    }
    public void AddLife()
    {
        if (!IsPlaying) return;
        Lives++;
        if (!LevelLoader.Tutorial && CrossLevelStatus.Active != null) CrossLevelStatus.Active.setLives(Lives);
    }

    // Called by Hook when it touches the pig
    public void PlayerHit()
    {
        if (!IsPlaying || IsInvincible) return;

        // A shield absorbs the hit for its full 5 seconds. The hook still stops.
        if (!LevelLoader.Tutorial && CrossLevelStatus.Active != null && CrossLevelStatus.Active.isShielded()) return;

        PlayAnalytics.Death();
        if (Lives > 1)
        {
            Lives--;
            invincibleUntil = Time.time + invincibleTime;
        }
        else
        {
            Lives = 0;
            EndRound(true, "Killed by the hook!");
        }

        if (!LevelLoader.Tutorial && CrossLevelStatus.Active != null) CrossLevelStatus.Active.setLives(Lives);
    }

    void EndRound(bool killed, string reason)
    {
        IsPlaying = false;
        EndReason = reason;
        PlayerWon = !killed && PlayerMoney > MinerMoney;   // a tie is a loss
        RoundMarginLog.Record(this);
    }

    // Win now, whatever the money: for levels with another goal (the block tutorial)
    public void WinRound(string reason)
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        EndReason = reason;
        PlayerWon = true;
        RoundMarginLog.Record(this);
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
