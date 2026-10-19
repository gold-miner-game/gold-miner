using UnityEngine;

// Owner: Butian (Survival mode)
// How a survival hook aims (survival mode only). Put one next to a SurvivalHookAI and set its aimer:
// it decides where each shot goes, or that it has no meaningful guess (the hook then aims at the player).
// Subclass it for other predictors: LinearHookAimer, TreasureInterceptAimer.
public abstract class HookAimer : MonoBehaviour
{
    private Transform cachedTarget;
    private Rigidbody2D targetBody;

    // Where to aim a shot at `target`, fired from `origin` after a telegraph of `delay` seconds, with
    // the hook flying out at `hookSpeed` world units per second. False: no meaningful guess
    public abstract bool TryAim(Vector2 origin, Transform target, float delay, float hookSpeed, out Vector2 aimAt);

    // The target's current velocity. Characters move through their Rigidbody2D (PlayerCharacter)
    protected Vector2 TargetVelocity(Transform target)
    {
        if (target != cachedTarget)
        {
            cachedTarget = target;
            targetBody = target.GetComponent<Rigidbody2D>();
        }
        return targetBody != null ? targetBody.linearVelocity : Vector2.zero;
    }
}
