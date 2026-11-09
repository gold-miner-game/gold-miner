using UnityEngine;

// Owner: Penny (Level map & tutorial)
// A wordless intro: when the level starts the game freezes, the arena goes dark except for a round
// hole on the player, and TutorialArrows point at things. After introSeconds the dark and the
// arrows fade out and play starts. Add it to a level scene with Tools > Gold Miner > Tutorial Spotlight.
//   Dark   a big black sprite over the arena; it only draws outside sprite masks
//   Hole   a circle SpriteMask that follows the target. Duplicate it (and leave the copy where you
//          put it) to also light up something that stays still, like a piece of gold
// The freeze is a time scale of 0, like the HUD's pause, so the clock, hooks and character all stop.
// Runs in the editor too, so the Scene view shows the effect around the level's PlayerStart.
[ExecuteAlways]
public class TutorialSpotlight : MonoBehaviour
{
    [Tooltip("What the hole follows. Empty = the player's character (PlayerStart in the editor).")]
    public Transform target;
    [Tooltip("Radius of the lit hole, in world units")]
    public float radius = 1.6f;
    [Range(0f, 1f)] public float darkness = 0.8f;
    [Tooltip("Seconds the intro shows before it fades away. 0 = the dark stays for the whole level")]
    public float introSeconds = 3f;
    [Tooltip("Nothing moves during the intro (the clock, the hooks, the player)")]
    public bool freezeGame = true;
    [Tooltip("Seconds the fade in / fade out takes")]
    public float fadeTime = 0.6f;

    public SpriteRenderer dark;
    public SpriteMask hole;

    // The spotlight in play, for TutorialArrow
    public static TutorialSpotlight Active { get; private set; }

    // 0..1: how much of the intro is showing (1 in the editor)
    public float Visibility { get; private set; } = 1f;
    // True once the intro has faded out; the spotlight is switched off then
    public bool Finished { get; private set; }

    float startedAt = -1f;   // unscaled time the intro began; -1 = not yet (the player isn't there)
    bool frozen;
    PlayerCharacter player;

    void OnEnable()
    {
        if (Application.isPlaying) Active = this;
    }

    void OnDisable()
    {
        Unfreeze();
        if (Active == this) Active = null;
    }

    void LateUpdate()
    {
        Transform follow = Target();
        if (hole != null)
        {
            if (follow != null) hole.transform.position = new Vector3(follow.position.x, follow.position.y, hole.transform.position.z);
            hole.transform.localScale = Vector3.one * (radius * 2f);   // the circle sprite is 1 unit across
        }

        if (Application.isPlaying) UpdateIntro(follow != null);
        if (dark != null)
        {
            Color c = dark.color;
            c.a = darkness * Visibility;
            dark.color = c;
        }
    }

    void UpdateIntro(bool ready)
    {
        // Start once the Game scene has put the player in, so the whole intro is seen
        if (startedAt < 0f)
        {
            if (!ready) { Visibility = 0f; return; }
            startedAt = Time.unscaledTime;
        }

        float t = Time.unscaledTime - startedAt;
        bool holding = introSeconds <= 0f || t < introSeconds;
        if (holding && freezeGame && introSeconds > 0f) Freeze();
        else Unfreeze();

        float fadeIn = fadeTime > 0f ? Mathf.Clamp01(t / fadeTime) : 1f;
        float fadeOut = introSeconds <= 0f ? 1f
                      : fadeTime > 0f ? Mathf.Clamp01((introSeconds + fadeTime - t) / fadeTime)
                      : (holding ? 1f : 0f);
        Visibility = Mathf.Min(fadeIn, fadeOut);

        if (introSeconds > 0f && Visibility <= 0f && t >= introSeconds)
        {
            Finished = true;
            gameObject.SetActive(false);
        }
    }

    // Kept at 0 every frame of the intro, so closing the HUD's pause popup doesn't end it early
    void Freeze()
    {
        frozen = true;
        Time.timeScale = 0f;
    }

    void Unfreeze()
    {
        if (!frozen) return;
        frozen = false;
        HUDController hud = FindFirstObjectByType<HUDController>();
        if (hud == null || !hud.IsPaused) Time.timeScale = 1f;   // a pause opened meanwhile stays paused
    }

    Transform Target()
    {
        if (target != null) return target;
        if (Application.isPlaying)
        {
            if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
            return player != null ? player.transform : null;
        }
        LevelLayout layout = FindFirstObjectByType<LevelLayout>();
        return layout != null ? layout.playerStart : null;
    }
}
