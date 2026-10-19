using UnityEngine;

// Owner: Butian (Survival mode)
// Guesses that the player keeps moving the way it is moving now: straight on, at its current speed.
// The hook arrives after the telegraph plus its flight, and the flight depends on how far the guess
// is, so the guess is refined a few times until the two agree (the intercept point).
// A player standing still gets a straight shot; a running one gets led. It always shoots.
public class LinearHookAimer : HookAimer
{
    [Tooltip("How much of the player's movement to lead by (0 = aim at where it is, 1 = the full guess)")]
    [Range(0f, 1f)] public float leadAmount = 1f;
    [Tooltip("Never guess further ahead than this many seconds")]
    public float maxLeadTime = 3f;
    [Tooltip("Refinement steps for the intercept point")]
    [Range(1, 8)] public int iterations = 4;

    public override bool TryAim(Vector2 origin, Transform target, float delay, float hookSpeed, out Vector2 aimAt)
    {
        Vector2 now = target.position;
        Vector2 velocity = TargetVelocity(target) * leadAmount;
        aimAt = now;
        if (velocity.sqrMagnitude < 0.0001f || hookSpeed <= 0f) return true;

        for (int i = 0; i < iterations; i++)
        {
            float arrival = delay + Vector2.Distance(origin, aimAt) / hookSpeed;
            aimAt = now + velocity * Mathf.Min(arrival, maxLeadTime);
        }
        return true;
    }
}
