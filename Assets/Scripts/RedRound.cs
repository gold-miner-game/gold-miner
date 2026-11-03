using UnityEngine;

// Owner: Butian (attack mode)
// "Red Round": the tutorial's round hook hole (SurvivalHookAI), red, and walking. Slow, with more HP
// than a Red Square. Once it has a clear shot (no wall rock in between) within Shoot Range it stops
// and shoots its red hook at the player; it stands still from the telegraph until the hook is home.
// On the hole object (ai.gameObject), so the hook comes out of wherever it has walked to.
[RequireComponent(typeof(SurvivalHookAI))]
public class RedRound : RedEnemy
{
    [Tooltip("It shoots when the player is this close and nothing solid is in between")]
    public float shootRange = 7f;
    [Tooltip("With a clear shot it stops walking this close to the player")]
    public float keepDistance = 4f;

    SurvivalHookAI ai;

    protected override void Start()
    {
        base.Start();
        ai = GetComponent<SurvivalHookAI>();
        ai.range = 0f;
        ai.holdFire = true;
    }

    void FixedUpdate()
    {
        if (ai == null) return;
        if (Pushed()) return;
        if (!Ready(out float distance)) { ai.holdFire = true; return; }

        bool canShoot = Hunting && distance <= shootRange && ClearLine(body.position);
        ai.holdFire = !canShoot;
        if (!Hunting || ai.Aiming) return;
        if (canShoot && distance <= keepDistance) return;   // close enough: hold the spot
        Chase(moveSpeed * Time.fixedDeltaTime);
    }

    // The hook and its telegraph are the hole's siblings (HookHole.Build), not its children, so they
    // don't walk with it: a resting hook (its claw drawn where it last came home) would stay behind
    // where the last shot was fired from. Keep it in the hole whenever it isn't out on a shot
    void LateUpdate()
    {
        if (ai == null || ai.hook == null || ai.hook.IsBusy) return;
        ai.hook.transform.position = transform.position;
    }
}
