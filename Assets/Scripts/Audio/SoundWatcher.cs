using UnityEngine;

// Owner: Claude (sound director)
// Turns what already happens in the game into sound, without touching gameplay: it listens to the
// game's named events (TutorialEvents), Treasure.Eaten and HookHealth.Destroyed, and reads (never
// writes) a few public states each frame: the round's clock and money goal, and the player's skill.
// Moments with no event (a hook leaving its hole, a claw closing on gold, a rock breaking) call
// Sfx.Play from one line in their own script instead.
// The story/dialogue scenes stay silent on purpose: their animation is someone else's pass.
public class SoundWatcher : MonoBehaviour
{
    void OnEnable()
    {
        TutorialEvents.Fired += OnEvent;
        Treasure.Eaten += OnEaten;
        HookHealth.Destroyed += OnEnemyDown;
    }

    void OnDisable()
    {
        TutorialEvents.Fired -= OnEvent;
        Treasure.Eaten -= OnEaten;
        HookHealth.Destroyed -= OnEnemyDown;
    }

    PlayerCharacter Player
    {
        get
        {
            if (player != null && player.isActiveAndEnabled) return player;
            if (Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 0.5f;
                player = FindFirstObjectByType<PlayerCharacter>();   // active, enabled ones only
            }
            return player != null && player.isActiveAndEnabled ? player : null;
        }
    }
    PlayerCharacter player;
    float nextPlayerSearch;

    Vector3 PlayerAt { get { PlayerCharacter p = Player; return p != null ? p.transform.position : Vector3.zero; } }

    // --- named events --------------------------------------------------------------------------------
    // Never let a sound problem stop the event reaching the game's other listeners
    void OnEvent(string name)
    {
        try { Play(name); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    void Play(string name)
    {
        switch (name)
        {
            case "hook_aim": Sfx.Play("hook_aim_warn"); return;
            case "hook_blocked": Sfx.Play("hook_blocked", PlayerAt); return;
            case "treasure_stolen": Sfx.Play("gold_dropped", PlayerAt); return;
            case "digest_blocked": Sfx.Play("digest_full", PlayerAt); return;
            case "player_hit": Hurt(); return;
            case HookHealth.DamagedEvent: Sfx.Play("enemy_hit"); return;
            case "round_won":
                Sfx.Play("round_won");
                SoundDirector.Instance?.SetRoundEndDip(0.55f);
                return;
            case "round_lost":
                Sfx.Play("round_lost");
                SoundDirector.Instance?.SetRoundEndDip(0.35f);
                return;
            case "block_unlocked":
            case "roll_unlocked":
            case LevelLoader.SectorClearedEvent:
                Sfx.Play("unlock"); return;
            case SqueezePhase.StartedEvent: Sfx.Play("squeeze_rumble"); return;
            case SqueezePhase.ClosedEvent: Sfx.Play("squeeze_slam"); return;
            case SqueezePhase.CrushedEvent: Sfx.Play("squeeze_slam"); Sfx.Play("hurt_" + SoundCues.Voice(Player), PlayerAt); return;
            case SqueezePhase.RescueEvent: Sfx.Play("revive"); return;
            case SqueezePhase.ExitOpenEvent: Sfx.Play("exit_open"); return;
            case LevelExit.ReachedEvent: Sfx.Play("portal_enter"); return;
        }

        if (name.StartsWith("portal_revealed_")) { Sfx.Play("portal_reveal"); return; }
        if (name.StartsWith("picked_"))
        {
            Sfx.Ui("pick_confirm");
            Sfx.Ui("happy_" + SoundCues.VoiceForPick(name.Substring("picked_".Length)));
        }
    }

    void Hurt()
    {
        Vector3 at = PlayerAt;
        Sfx.Play("hit_impact", at);
        Sfx.Play("hurt_" + SoundCues.Voice(Player), at);
    }

    // Gold and diamonds: a bite, then the reward's ring (bigger nuggets ring longer and lower)
    void OnEaten(Treasure treasure)
    {
        try { Eaten(treasure); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    void Eaten(Treasure treasure)
    {
        if (treasure == null) return;
        Vector3 at = treasure.transform.position;
        Sfx.Play("eat_munch", at);
        switch (treasure.type)
        {
            case TreasureType.SmallGold: Sfx.Play("coin_small", at, 0.85f, 1.08f); break;
            case TreasureType.MediumGold: Sfx.Play("coin_small", at, 1f, 0.94f); break;
            case TreasureType.LargeGold: Sfx.Play("coin_large", at); break;
            case TreasureType.Diamond: Sfx.Play("diamond", at, 1f, 1.15f); break;   // pitched up: a sparkle above the coins
        }
    }

    void OnEnemyDown(HookHealth enemy)
    {
        if (enemy != null) Sfx.Play("enemy_down", enemy.transform.position);
    }

    // --- states read every frame ---------------------------------------------------------------------
    GameManager roundSeen;
    float roundSeenAt;
    bool startRung, goalWasReached;
    int lastTick = -1;
    bool skillWasActive;
    bool coolingDown;
    PlayerCharacter skillOwner;

    void Update()
    {
        WatchRound();
        WatchSkill();
    }

    void WatchRound()
    {
        GameManager gm = GameManager.Instance;
        if (gm != roundSeen)
        {
            roundSeen = gm;
            roundSeenAt = Time.unscaledTime;
            startRung = false;
            goalWasReached = gm != null && gm.MoneyTargetReached;
            lastTick = -1;
        }
        if (gm == null || !gm.IsPlaying || Time.timeScale == 0f) return;

        // The bell: a timed money round has begun (not on the world map, not in endless survival)
        if (!startRung && gm.UsesMoneyTarget && gm.UseRoundTimer && MapHub.Active == null
            && Time.unscaledTime - roundSeenAt > 0.5f)
        {
            startRung = true;
            Sfx.Play("round_start");
        }

        bool reached = gm.MoneyTargetReached;
        if (reached && !goalWasReached) Sfx.Play("target_reached");
        goalWasReached = reached;

        // The last ten seconds tick; the last five tick higher and louder
        if (gm.UseRoundTimer && !gm.endless && MapHub.Active == null)
        {
            int left = Mathf.CeilToInt(gm.TimeLeft);
            if (left != lastTick && left <= 10 && left >= 1 && lastTick != -1)
            {
                bool urgent = left <= 5;
                Sfx.Play("clock_tick", Vector3.zero, urgent ? 1.25f : 0.9f, urgent ? 1.18f : 1f);
            }
            lastTick = left;
        }
    }

    void WatchSkill()
    {
        PlayerCharacter p = Player;
        if (p != skillOwner) { skillOwner = p; skillWasActive = p != null && p.SkillActive; coolingDown = false; }
        if (p == null) return;
        bool active = p.SkillActive;
        if (active && !skillWasActive && PlayerCharacter.IsPlaying)
        {
            Sfx.Play(SoundCues.Skill(p), p.transform.position);
            if (p is CatController) Sfx.Play("roll_cry", p.transform.position);   // J.K. Rolling's battle cry as she rolls
        }
        skillWasActive = active;

        // The skill is ready again: a soft chime when a real cooldown (not a hold skill) runs out
        float readyIn = p.HasSkill ? p.SkillReadyIn : 0f;
        if (readyIn > 0.25f && p.SkillWait > 0.75f) coolingDown = true;   // a real cooldown is running
        if (readyIn <= 0f && coolingDown)
        {
            coolingDown = false;
            if (PlayerCharacter.IsPlaying) Sfx.Play("skill_ready", p.transform.position);
        }
    }
}
