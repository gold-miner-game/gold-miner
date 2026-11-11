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
    // Bonus lives from Brace this round (orange on the HUD / over-head bar).
    public int BonusLives { get; private set; }
    // Set on settle when this round beats the level's best (player − miner) margin.
    public string NewRecordLine { get; private set; }
    public float TimeLeft { get; private set; }
    public float TimeElapsed => endless ? TimeSurvived : roundTime - TimeLeft;
    public bool IsPlaying { get; private set; }
    public bool PlayerWon { get; private set; }
    public bool PlayerDied { get; private set; }
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
    bool earningsBanked;

    // Both the level and the map transition may settle a round. Pay its collected gold once.
    public int BankLevelEarnings()
    {
        if (IsPlaying || earningsBanked) return 0;
        earningsBanked = true;
        int earned = Mathf.Max(0, PlayerMoney);
        if (earned > 0) CrossLevelStatus.Ensure().addWallet(earned);
        return earned;
    }

    void Awake()
    {
        Instance = this;
        TimeLeft = roundTime;
        Lives = startingLives;
        BonusLives = 0;
        NewRecordLine = "";
        IsPlaying = true;
        PlayAnalytics.BeginLevel();
    }

    public void SetBonusLives(int bonus)
    {
        BonusLives = Mathf.Max(0, bonus);
    }

    // The world map keeps its own HP between visits (MapHub): set it straight
    public void SetLives(int lives)
    {
        Lives = Mathf.Max(1, lives);
    }

    // LevelLoader sets startingLives after Awake; call this so Lives matches the braced total.
    public void SyncLivesFromStart()
    {
        Lives = startingLives;
    }

    public void SetNewRecordLine(string line)
    {
        NewRecordLine = line ?? "";
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

        if (endless)
        {
            TimeSurvived += Time.deltaTime;
            return;
        }

        if (UseRoundTimer)
        {
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                if (timeUpWins) WinRound("You survived!");
                else EndRound(false, "Time's up!");
                return;
            }
        }

        if (EndWhenTreasureGone && !timeUpWins && hadTreasure && Treasure.Active.Count == 0 && Treasure.OnHooks.Count == 0)
            EndRound(false, "All treasure collected!");
    }

    public void AddPlayerMoney(int amount) { if (IsPlaying) PlayerMoney += amount; }
    public void AddMinerMoney(int amount)  { if (IsPlaying) MinerMoney += amount; }

    // A fresh money race inside the round (the tutorial's race stage starts and retries with this)
    public void ResetMoney() { PlayerMoney = 0; MinerMoney = 0; }

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
        if (!LevelLoader.Tutorial && CrossLevelStatus.Active != null && CrossLevelStatus.Active.isShielded())
        {
            PlayAnalytics.ShieldSoaked();
            return;
        }

        // The world map (the evil cat's eye miners): a hook costs 1 HP. The last one doesn't end the
        // run: the player revives at the Pick Character portal with full HP (MapHub)
        if (MapHub.Active != null)
        {
            TutorialEvents.Fire("player_hit");
            invincibleUntil = Time.time + invincibleTime;
            if (Lives > 1)
            {
                Lives--;
                if (BonusLives > 0) BonusLives--;
                TutorialEvents.Fire("life_lost");
                MapHub.Active.SaveHp(this);
            }
            else MapHub.Active.Revive(this);
            return;
        }

        PlayAnalytics.Death();
        TutorialEvents.Fire("player_hit");
        if (Lives > 1)
        {
            Lives--;
            // Brace bonus pips are lost first (orange count drops with the hit).
            if (BonusLives > 0)
            {
                BonusLives--;
                PlayAnalytics.BraceSpent();
            }
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
        PlayerDied = killed;
        EndReason = reason;
        PlayerWon = !killed && PlayerMoney > MinerMoney;   // a tie is a loss
        RoundMarginLog.Record(this);
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
        StoreView.ShuffleStock();
        TutorialEvents.Fire(PlayerWon ? "round_won" : "round_lost");
        TutorialEvents.Fire("round_end");
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
