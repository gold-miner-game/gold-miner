using UnityEngine;

// Owner: Butian (roam mode)
// A Red Round's aim in a cooperating pack (EnemyPack): it shoots where the player will be if they keep
// running (the smoothed run, over the telegraph and the hook's flight), not where they are. It only
// leads part of the way, picked fresh each shot, so running straight on isn't a sure dodge either. A
// player who changes course during the telegraph gets away.
public class PackLeadAimer : HookAimer
{
    [Tooltip("The share of the full lead each shot takes, picked between these")]
    public float leadMin = 0.5f;
    public float leadMax = 1f;
    [Tooltip("Never leads further than this, in world units")]
    public float maxLead = 4f;

    public override bool TryAim(Vector2 origin, Transform target, float delay, float hookSpeed, out Vector2 aimAt)
    {
        aimAt = target.position;
        Vector2 v = EnemyPack.Enabled ? EnemyPack.PlayerVelocity : TargetVelocity(target);
        if (v.sqrMagnitude < 0.25f) return false;   // standing still: straight at them
        float flight = Vector2.Distance(origin, target.position) / Mathf.Max(0.1f, hookSpeed);
        Vector2 lead = Vector2.ClampMagnitude(v * (delay + flight) * Random.Range(leadMin, leadMax), maxLead);
        aimAt = (Vector2)target.position + lead;
        return true;
    }
}
