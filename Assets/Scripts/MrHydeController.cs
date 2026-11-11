using UnityEngine;

// Owner: B (Player & Rocks)
// Mr. Hyde (the pig). Moves, eats and blinks like every character (PlayerCharacter);
// its skill is the block: K bounces a hook off, or knocks treasure off a passing hook (it never
// hits). In the world map's battles the block is locked until the Level 2 summary unlocks it
// (SkillUnlockSummary). J beside a rope cuts it. Space and the left mouse button do nothing.
public class MrHydeController : PlayerCharacter
{
    [Header("Block (K)")]
    public bool canBlock = false;       // turned on per level by LevelLoader
    public float blockDuration = 0.8f;  // how long one block lasts
    public float blockCooldown = 1f;    // wait after a block ends before the next one
    public Color blockTint = new Color(0.45f, 0.75f, 1f);

    public override bool IsBlocking => Time.time < blockUntil || base.IsBlocking;
    // J beside a rope cuts it. A lesson can turn that off (suppressCut).
    public bool suppressCut;
    public override bool CanCutRope => Hook.RopeCuts && LevelBlock && !suppressCut;
    public override string SkillHint => (CanBlock ? "[K] block   " : "") + (CanCutRope ? "[J] cut rope" : "");
    public override bool SkillActive => IsBlocking;
    public override float SkillReadyIn => BlockReadyIn;
    public override float SkillWait => BlockDuration + BlockCooldown;

    // After the tutorial the record decides (the store can change it); in the tutorial, the level does
    bool LevelBlock => Record != null ? Record.getCanBlock() : canBlock;
    // ... and in a world map battle, only once the Level 2 summary has unlocked it
    public bool CanBlock => LevelBlock && (!GameFlow.InMapBattle || SkillUnlockSummary.BlockUnlocked);
    public float BlockDuration => Record != null ? Record.getBlockDuration() : blockDuration;
    public float BlockCooldown => Record != null ? Record.getBlockCooldown() : blockCooldown;

    // Time left in the current block, and until the next one
    public float BlockLeft => Mathf.Max(0f, blockUntil - Time.time);
    public float BlockReadyIn => Mathf.Max(0f, blockReadyAt - Time.time);

    private float blockUntil;
    private float blockReadyAt;

    // K: start a block if the skill is on and off cooldown
    protected override void UseSkill()
    {
        TryBlock();
    }

    // Maze QTEs also call this when K is pressed during a freeze-frame.
    public bool TryBlock()
    {
        if (!CanBlock || Time.time < blockReadyAt) return false;
        blockUntil = Time.time + BlockDuration;
        blockReadyAt = blockUntil + BlockCooldown;
        return true;
    }

    // The tutorial's scripted hit must not bounce off a block that is still running.
    public void CancelBlock()
    {
        blockUntil = 0f;
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

    // While blocking the whole body is blue
    protected override Color BodyColor => IsBlocking ? blockTint : BaseColor;

    // The block cooldown shows blue, draining from the top down
    protected override bool SkillCooldown(out float left, out Color tint)
    {
        tint = blockTint;
        left = !IsBlocking && BlockReadyIn > 0f && BlockCooldown > 0f ? BlockReadyIn / BlockCooldown : 0f;
        return left > 0f;
    }
}
