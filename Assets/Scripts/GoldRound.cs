using UnityEngine;

// Owner: Butian (roam mode)
// "Gold Round": a walking hook hole that competes for the gold instead of hunting the player. It
// walks (A* round the wall rocks, like RedEnemy) to the nearest free treasure, stops once it is
// within Reach, and pulls it home with a yellow hook that flies through the wall rocks
// ("gold_hook_through_rocks"; off: it needs a clear line, like a Red Round); what it banks goes to the miner's
// side of the score, like a normal miner's. Whether its hook hurts the player is the level's call
// ("gold_hook_hurts"). On the hole object (ai.gameObject), so the hook comes out of wherever it walked to.
[RequireComponent(typeof(SurvivalHookAI))]
public class GoldRound : RedEnemy
{
    public static readonly Color Gold = new Color(1f, 0.82f, 0.2f);

    [Tooltip("It shoots once the treasure is this close and nothing solid is in between")]
    public float reach = 5f;
    [Tooltip("Seconds between looks for a nearer piece of treasure while walking")]
    public float retargetEvery = 0.5f;
    [Tooltip("Its hook flies through the wall rocks (Hook.passRocks), so it shoots over walls instead of walking round them")]
    public bool throughWalls = true;

    // After the gold, not the player: no part in the hunting pack
    public override bool InPack => false;

    SurvivalHookAI ai;
    Treasure goal;
    float retargetAt;

    protected override void Start()
    {
        base.Start();
        ai = GetComponent<SurvivalHookAI>();
        if (GetComponent<PaperMineCart>() == null) gameObject.AddComponent<PaperMineCart>().kind = PaperMineCart.Kind.Gold;   // art only
        ai.goldOnly = true;
        ai.range = 0f;
        ai.holdFire = true;
        ai.shotColor = Gold;
        WakeUp();   // it is after the gold from the start, wherever the player is
    }

    void FixedUpdate()
    {
        if (ai == null) return;
        if (Pushed()) return;
        // From the telegraph until the hook is home it stands still and keeps its aim
        if (ai.Aiming || (ai.hook != null && ai.hook.IsBusy)) return;

        if (goal == null || goal.IsTaken || !goal.isActiveAndEnabled || Time.time >= retargetAt)
        {
            goal = Nearest();
            retargetAt = Time.time + retargetEvery;
        }
        Transform at = goal != null ? goal.transform : null;
        target = at;
        ai.target = at;
        if (at == null) { ai.holdFire = true; return; }   // nothing left to take
        if (!Ready(out float distance)) { ai.holdFire = true; return; }

        bool canShoot = distance <= reach && (throughWalls || ClearLine(body.position));
        ai.holdFire = !canShoot;
        if (canShoot) return;
        Chase(moveSpeed * Time.fixedDeltaTime);
    }

    // The closest treasure nobody is pulling and nothing has frozen
    Treasure Nearest()
    {
        Treasure best = null;
        float bestDist = float.PositiveInfinity;
        Vector2 me = body != null ? body.position : (Vector2)transform.position;
        foreach (Treasure t in Treasure.Active)
        {
            if (t == null || t.IsTaken || t.IsFrozen || !t.isActiveAndEnabled) continue;
            float d = ((Vector2)t.transform.position - me).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }

    // The hook and its telegraph are the hole's siblings (HookHole.Build), not its children, so keep
    // the resting hook in the hole as it walks (RedRound does the same)
    void LateUpdate()
    {
        if (ai == null || ai.hook == null || ai.hook.IsBusy) return;
        ai.hook.transform.position = transform.position;
    }
}
