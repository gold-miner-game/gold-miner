using UnityEngine;

// Owner: Butian (Survival mode)
// Uses what the player wants, the gold, to catch it on the way there.
// 1. Guess the goal: the nearest treasure roughly in the direction the player is moving.
// 2. Assume it runs straight there at its current speed, and find the point on that path where the
//    hook (telegraph, then flight) and the player arrive at the same moment: catch it in the middle.
// 3. No meaningful guess (not moving, no treasure ahead, the player reaches the gold first, or the
//    meeting point is out of reach): return false, and SurvivalHookAI shoots straight at the player.
public class TreasureInterceptAimer : HookAimer
{
    [Tooltip("Only treasure this close to the player can be the goal")]
    public float lureRadius = 8f;
    [Tooltip("Treasure within this angle (degrees) of the player's movement can be the goal")]
    [Range(5f, 90f)] public float intentAngle = 30f;
    [Tooltip("Slower than this (units/s) the player isn't heading anywhere: no guess")]
    public float minSpeed = 0.5f;
    [Tooltip("No guess if the meeting point is further than this from the hook (0 = no limit)")]
    public float maxReach = 11f;
    [Tooltip("Points checked along the path before narrowing down the meeting point")]
    [Range(8, 64)] public int samples = 32;

    // The last shot's guess, for the gizmo
    public Treasure LastGoal { get; private set; }
    private Vector2 lastMeet;
    private bool lastShot;

    public override bool TryAim(Vector2 origin, Transform target, float delay, float hookSpeed, out Vector2 aimAt)
    {
        Vector2 at = target.position;
        aimAt = at;
        lastShot = false;
        LastGoal = null;

        Vector2 velocity = TargetVelocity(target);
        float speed = velocity.magnitude;
        if (speed < minSpeed || hookSpeed <= 0f) return false;

        Treasure goal = GuessGoal(at, velocity / speed);
        LastGoal = goal;
        if (goal == null) return false;

        // Positive: the hook gets to the point `s` along the path after the player has passed it
        Vector2 goalAt = goal.transform.position;
        float pathLength = Vector2.Distance(at, goalAt);
        System.Func<float, float> late = s =>
            delay + Vector2.Distance(origin, Vector2.MoveTowards(at, goalAt, s)) / hookSpeed - s / speed;

        // At the player (s = 0) the hook is always late; look for the first point where it isn't
        float before = 0f;
        float found = -1f;
        for (int i = 1; i <= samples; i++)
        {
            float s = pathLength * i / samples;
            if (late(s) <= 0f) { found = s; break; }
            before = s;
        }
        if (found < 0f) return false;   // the player reaches the gold first

        // Narrow down to where both arrive together
        for (int i = 0; i < 12; i++)
        {
            float mid = 0.5f * (before + found);
            if (late(mid) <= 0f) found = mid;
            else before = mid;
        }

        Vector2 meet = Vector2.MoveTowards(at, goalAt, found);
        if (maxReach > 0f && Vector2.Distance(origin, meet) > maxReach) return false;

        aimAt = meet;
        lastMeet = meet;
        lastShot = true;
        return true;
    }

    // The nearest treasure within intentAngle of the player's heading
    Treasure GuessGoal(Vector2 at, Vector2 heading)
    {
        float minDot = Mathf.Cos(intentAngle * Mathf.Deg2Rad);
        Treasure best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Treasure t in Treasure.Active)
        {
            Vector2 to = (Vector2)t.transform.position - at;
            float distance = to.magnitude;
            if (distance > lureRadius || distance < 0.001f) continue;
            if (Vector2.Dot(heading, to / distance) < minDot) continue;
            if (distance < bestDistance) { bestDistance = distance; best = t; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        if (LastGoal == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(LastGoal.transform.position, 0.5f);
        if (!lastShot) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lastMeet, 0.3f);
    }
}
