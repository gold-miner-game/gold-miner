using UnityEngine;

// Owner: B (Player & Rocks)
// Mr. Hyde (the pig). Moves, eats and blinks like every character (PlayerCharacter);
// K blocks after Level 1. J cuts every nearby rope.
public class MrHydeController : PlayerCharacter
{
    public override bool CanBlock => SkillUnlockSummary.BlockUnlocked
        || (LevelLoader.Tutorial && canBlock);

    // J beside a rope cuts it. A lesson can turn that off (suppressCut).
    public bool suppressCut;
    public override bool CanCutRope => base.CanCutRope && !suppressCut;

    // K: start a block if the skill is on and off cooldown
    protected override void UseSkill()
    {
        TryBlock();
    }

    protected override void Update()
    {
        base.Update();
        ReadAttack();
    }

    // A hit that landed: the block's white half sphere on the player's body, facing what it hit
    public static void ShowHit(PlayerCharacter player, HookHealth target)
    {
        Collider2D body = player.GetComponent<Collider2D>();
        Vector2 at = player.transform.position;
        Vector2 toward = target.Box != null ? target.Box.ClosestPoint(at) - at : (Vector2)target.transform.position - at;
        if (toward.sqrMagnitude < 0.0001f) toward = Vector2.up;
        Vector2 surface = body != null ? body.ClosestPoint(at + toward.normalized * 10f) : at;
        BlockFlash.Show(surface, toward, body != null ? body.bounds.size.x : 1f);
    }

    // --- Attack (left click, any level) -------------------------------------------------------------
    // A fast sweep of a sixth of a circle round the pig (AttackSwing). It points right, or at the
    // closest enemy when one is in reach, and hits every enemy in reach inside the sweep, pushing it away (Knockback)

    [Header("Attack (left click)")]
    [Tooltip("An enemy this close (world units from the pig's centre to its box) is aimed at and can be hit")]
    public float attackReach = 1.3f;
    [Tooltip("How long the sweep takes, in seconds")]
    public float attackSwingTime = 0.12f;
    [Tooltip("Wait after a swing starts before the next one")]
    public float attackCooldown = 0.35f;
    [Tooltip("The sweep, in degrees (60 = a sixth of a circle)")]
    public float attackArc = 60f;
    [Tooltip("How far past the pig's body the slash reaches (its outer edge), in world units")]
    public float attackRingGap = 0.45f;
    [Tooltip("How far a hit pushes the enemy away from the pig, in world units")]
    public float attackPush = 1.2f;
    [Tooltip("Seconds the push takes")]
    public float attackPushTime = 0.18f;

    private float attackReadyAt;
    private readonly System.Collections.Generic.List<HookHealth> swingTargets = new System.Collections.Generic.List<HookHealth>();

    void ReadAttack()
    {
        // Left click is not an attack in any level.
    }

    public void Attack()
    {
        attackReadyAt = Time.time + attackCooldown;
        Collider2D body = GetComponent<Collider2D>();
        Vector2 from = body != null ? (Vector2)body.bounds.center : (Vector2)transform.position;
        float bodyRadius = body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) : 0.5f;

        // Right by default; at the closest enemy when one is close enough to hit
        Vector2 dir = Vector2.right;
        HookHealth closest = HookHealth.InReach(from, attackReach);
        if (closest != null)
        {
            Vector2 to = (Vector2)closest.Box.bounds.center - from;
            if (to.sqrMagnitude > 0.0001f) dir = to.normalized;
        }

        AttackSwing.Show(transform, dir, bodyRadius + attackRingGap, attackSwingTime, attackArc);

        HookHealth.InArc(from, attackReach, dir, attackArc * 0.5f + 15f, swingTargets);
        if (closest != null && !swingTargets.Contains(closest)) swingTargets.Add(closest);
        foreach (HookHealth target in swingTargets)
        {
            target.Damage();
            // Pushed straight away from the pig
            Vector2 away = (Vector2)target.Box.bounds.center - from;
            Knockback.Push(target, away.sqrMagnitude > 0.0001f ? away : dir, attackPush, attackPushTime);
        }
    }

}
