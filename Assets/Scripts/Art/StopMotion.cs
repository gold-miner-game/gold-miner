using UnityEngine;

// Art: the shared stop-motion clock (docs/art-style.md, "Animation: the stop-motion rules").
// Art-owned motion samples these instead of Time, so every cut-out moves on the same beat: on twos
// (12 poses a second) for action, on threes (8) for boil. Gameplay never uses this.
public static class StopMotion
{
    public const float Twos = 12f;
    public const float Threes = 8f;

    // Game time snapped to the last 12 fps pose (scaled time: a paused game is a still photo).
    public static float Time => Snap(UnityEngine.Time.time, Twos);

    // The pose number on twos / threes, for things that swap drawings.
    public static int Frame => Mathf.FloorToInt(UnityEngine.Time.time * Twos);
    public static int BoilFrame => Mathf.FloorToInt(UnityEngine.Time.time * Threes);

    public static float Snap(float t, float fps) => Mathf.Floor(t * fps) / fps;

    // Unscaled versions, for UI that must move while the game is paused (menus, scene reveals).
    public static float UnscaledSnap(float since) => Snap(UnityEngine.Time.unscaledTime - since, Twos);

    // Ease that holds a beat, anticipates slightly backwards, then accelerates away: a hand pulling paper.
    public static float Pull(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.18f) return -0.03f * Mathf.Sin(t / 0.18f * Mathf.PI);   // the little dip before the pull
        float u = (t - 0.18f) / 0.82f;
        return u * u * (1.6f - 0.6f * u);
    }
}
