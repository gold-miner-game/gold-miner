using UnityEngine;

// Art: the shared stop-motion clock (docs/art-style.md, "Animation: the stop-motion rules").
// Art-owned motion samples these instead of Time, so every cut-out moves on the same beat: 8 poses a
// second for action (on threes, the hand-made, visibly choppy rate; 12 on twos read almost smooth) and 6
// for boil (on fours). Gameplay never uses this.
public static class StopMotion
{
    public const float Pose = 8f;   // action poses a second
    public const float Boil = 6f;   // line-boil / held-wobble poses a second

    static bool enabled = true;
    public static bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            // A console toggle can happen while paused, before another art tick is possible.
            PaperHolds.RestoreNow();
            PaperPuppet.ResetHeldPose();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { enabled = true; beatClock = lastUnscaled = gameTimeAtBeat = 0f; beat = boil = 0; tickedFrame = -1; }

    // ONE beat for every stop-motion picture, so movement, eyes, rock, roll and pips change on the same frames
    // (two clocks drifted out of phase after any pause or slow-down: eyes changed between the body's steps).
    // It runs on real time while the game runs, slowed tutorial prompts and the time machine included (a
    // slowed world still gets 8 pictures a second), and stops while the game is paused (timeScale 0), so a
    // paused game is still a still photo.
    static float beatClock, lastUnscaled;
    static int beat, boil, tickedFrame = -1;
    static float gameTimeAtBeat;

    static void Tick()
    {
        int f = UnityEngine.Time.frameCount;
        if (f == tickedFrame) return;
        tickedFrame = f;
        float now = UnityEngine.Time.unscaledTime, dt = now - lastUnscaled;
        lastUnscaled = now;
        // a locked capture clock (video recording) advances the beat by its fixed step, not by wall time
        if (UnityEngine.Time.captureDeltaTime > 0f) dt = UnityEngine.Time.captureDeltaTime;
        if (UnityEngine.Time.timeScale > 0f && dt > 0f && dt < 0.5f) beatClock += dt;   // a long hitch is not a held pose
        int b = Mathf.FloorToInt(beatClock * Pose);
        if (b != beat) { beat = b; gameTimeAtBeat = UnityEngine.Time.time; }
        boil = Mathf.FloorToInt(beatClock * Boil);
    }

    // Game time as it was when the current picture was taken: constant between beats.
    public static float Time { get { Tick(); return gameTimeAtBeat; } }

    // The picture number: changes Pose times a (running) second. BoilFrame: Boil times a second, same clock.
    public static int Frame { get { Tick(); return beat; } }
    public static int BoilFrame { get { Tick(); return boil; } }

    // The shared rock: -1, -1, 0, +1, +1, 0 on the pose clock (0.75 s a rock). Treasure leans with it,
    // characters and cats against it, so the set rocks in counterpoint like a handled stage.
    // How far a rock tips: 3 degrees each way, the gentle nudge of a hand-set cut-out between frames (idle
    // sway in hand-made motion sits around 1.5-4 degrees; 6 read as a wobble). Treasure's pre-drawn
    // Objects/NAME_r1/_r2 poses are drawn at exactly this angle (+ and -).
    public const float RockDegrees = 3f;
    static readonly int[] RockCycle = { -1, -1, 0, 1, 1, 0 };
    public static int Rock => RockCycle[((Frame % RockCycle.Length) + RockCycle.Length) % RockCycle.Length];

    public static float Snap(float t, float fps) => Mathf.Floor(t * fps) / fps;

    // Unscaled versions, for UI that must move while the game is paused (menus, scene reveals).
    public static float UnscaledSnap(float since) => Snap(UnityEngine.Time.unscaledTime - since, Pose);

    // Ease that holds a beat, anticipates slightly backwards, then accelerates away: a hand pulling paper.
    public static float Pull(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.18f) return -0.03f * Mathf.Sin(t / 0.18f * Mathf.PI);   // the little dip before the pull
        float u = (t - 0.18f) / 0.82f;
        return u * u * (1.6f - 0.6f * u);
    }
}
