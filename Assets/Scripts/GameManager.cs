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
    [Tooltip("Survival mode: no clock and no running out of treasure; only the last Life ends the round")]
    public bool endless = false;
    [Tooltip("Survival levels: still alive when the clock runs out is a win (whatever the money), and eating all the treasure doesn't end the round")]
    public bool timeUpWins = false;

    public int PlayerMoney { get; private set; }
    public float TimeSurvived { get; private set; }   // endless rounds: seconds since the start
    public int MinerMoney { get; private set; }
    public int Lives { get; private set; }
    public float TimeLeft { get; private set; }
    public float TimeElapsed => endless ? TimeSurvived : roundTime - TimeLeft;
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
        PlayAnalytics.BeginLevel();
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

        if (endless)
        {
            TimeSurvived += Time.deltaTime;
            return;
        }

        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f)
        {
            TimeLeft = 0f;
            if (timeUpWins) WinRound("You survived!");
            else EndRound(false, "Time's up!");
        }
        else if (!timeUpWins && hadTreasure && Treasure.Active.Count == 0 && Treasure.OnHooks.Count == 0)
        {
            EndRound(false, "All treasure collected!");
        }
    }

    public void AddPlayerMoney(int amount) { if (IsPlaying) PlayerMoney += amount; }
    public void AddMinerMoney(int amount)  { if (IsPlaying) MinerMoney += amount; }
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
        TutorialEvents.Fire("player_hit");
        if (Lives > 1)
        {
            Lives--;
            invincibleUntil = Time.time + invincibleTime;
            TutorialEvents.Fire("life_lost");   // still alive (the last Life ends the round instead)
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
        PlayerWon = !killed && PlayerMoney > MinerMoney;   // a tie is a loss        RoundMarginLog.Record(this);
        AnnounceEnd();
    }

    // Killed outright, whatever the Lives left (the squeeze's presses closing on the player)
    public void Crush(string reason)
    {
        if (!IsPlaying) return;
        Lives = 0;
        EndRound(true, reason);
    }

    // Win now, whatever the money: for levels with another goal (the block tutorial)
    public void WinRound(string reason)
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        EndReason = reason;
        PlayerWon = true;
        RoundMarginLog.Record(this);
        AnnounceEnd();
    }

    // For tutorial steps and stories that start when the round ends (TutorialEvents)
    void AnnounceEnd()
    {
        TutorialEvents.Fire(PlayerWon ? "round_won" : "round_lost");
        TutorialEvents.Fire("round_end");
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
