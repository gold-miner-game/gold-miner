using UnityEngine;

// Owner: Butian (compete mode)
// Compete mode's twist: time moves when the player moves. While the player moves (or is still
// sliding, like a rolling cat) the world runs at full speed; standing still slows everything (the
// clock, the miners' hooks and telegraphs, digesting) to Still Scale. Done with Time.timeScale, so
// every timer slows together. The player is only ever slowed while standing still, so it never
// feels slow: a step brings time back up in a moment (Speed Up).
// It leaves time alone while something else stops it (the pause popup, a frozen tutorial step,
// at time scale 0), and puts it back to normal when the round ends or this goes away.
// LevelLoader adds it to compete levels that also set "slow_when_still": 1 (none do for now).
public class TimeMovesWithPlayer : MonoBehaviour
{
    public PlayerCharacter player;
    [Tooltip("Time's speed while the player stands still (1 = normal)")]
    [Range(0.01f, 1f)] public float stillScale = 0.06f;
    [Tooltip("How fast time comes back up when the player moves, in time scale per real second")]
    public float speedUp = 8f;
    [Tooltip("How fast time winds down when the player stops")]
    public float slowDown = 3f;
    [Tooltip("The player counts as moving above this speed (world units per second)")]
    public float movingSpeed = 0.2f;
    [Tooltip("The camera's background darkens toward this while time is slow, so it reads at a glance")]
    public Color slowSky = new Color(0.08f, 0.12f, 0.1f);

    // 0 at full speed, 1 at Still Scale (for anything that wants to show it)
    public static float Slowness { get; private set; }

    Rigidbody2D body;
    Camera cam;
    Color normalSky;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Slowness = 0f; }

    void Start()
    {
        if (player != null) body = player.GetComponent<Rigidbody2D>();
        cam = Camera.main;
        if (cam != null) normalSky = cam.backgroundColor;
    }

    bool PlayerMoving()
    {
        if (player == null) return true;
        if (player.MoveInput.sqrMagnitude > 0f) return true;
        return body != null && body.linearVelocity.sqrMagnitude > movingSpeed * movingSpeed;
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) { Restore(); enabled = false; return; }
        if (Time.timeScale == 0f) return;   // paused, or a tutorial step froze the game

        float target = PlayerMoving() ? 1f : stillScale;
        float rate = target > Time.timeScale ? speedUp : slowDown;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, target, rate * Time.unscaledDeltaTime);
        Slowness = Mathf.InverseLerp(1f, stillScale, Time.timeScale);
        if (cam != null) cam.backgroundColor = Color.Lerp(normalSky, slowSky, Slowness);
    }

    void Restore()
    {
        if (Time.timeScale > 0f) Time.timeScale = 1f;
        Slowness = 0f;
        if (cam != null) cam.backgroundColor = normalSky;
    }

    void OnDisable() { Restore(); }
    void OnDestroy() { if (Time.timeScale > 0f && Time.timeScale < 1f) Time.timeScale = 1f; }
}
