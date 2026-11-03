using UnityEngine;

// Owner: B (Player & Rocks)
// Mr. Hyde (the pig). Moves, eats and blinks like every character (PlayerCharacter);
// its skill is the block: Space bounces a hook off, or knocks treasure off a passing hook. Next to a
// hook (HookHealth: a miner or a hook hole) the same press also hits it.
public class MrHydeController : PlayerCharacter
{
    [Header("Block (Space)")]
    public bool canBlock = false;       // turned on per level by LevelLoader
    public float blockDuration = 0.8f;  // how long one block lasts
    public float blockCooldown = 1f;    // wait after a block ends before the next one
    public Color blockTint = new Color(0.45f, 0.75f, 1f);
    [Tooltip("A block this close to a hook (a miner or a hook hole, with HookHealth) also hits it, in world units from the pig's centre to the hook's box")]
    public float attackReach = 1.3f;

    public override bool IsBlocking => Time.time < blockUntil || base.IsBlocking;
    public override string SkillHint => CanBlock ? "[Space] block" : "";
    public override bool SkillActive => IsBlocking;
    public override float SkillReadyIn => BlockReadyIn;
    public override float SkillWait => BlockDuration + BlockCooldown;

    // After the tutorial the record decides (the store can change it); in the tutorial, the level does
    public bool CanBlock => Record != null ? Record.getCanBlock() : canBlock;
    public float BlockDuration => Record != null ? Record.getBlockDuration() : blockDuration;
    public float BlockCooldown => Record != null ? Record.getBlockCooldown() : blockCooldown;

    // Time left in the current block, and until the next one
    public float BlockLeft => Mathf.Max(0f, blockUntil - Time.time);
    public float BlockReadyIn => Mathf.Max(0f, blockReadyAt - Time.time);

    private float blockUntil;
    private float blockReadyAt;

    // Start a block if the skill is on and off cooldown
    protected override void UseSkill()
    {
        TryBlock();
    }

    // Maze QTEs also call this when Space is pressed during a freeze-frame.
    public bool TryBlock()
    {
        if (!CanBlock || Time.time < blockReadyAt) return false;
        blockUntil = Time.time + BlockDuration;
        blockReadyAt = blockUntil + BlockCooldown;
        hitThisBlock.Clear();
        AttackInReach();
        return true;
    }

    // Enemies already hit by the current block (each takes one hit per block)
    private readonly System.Collections.Generic.HashSet<HookHealth> hitThisBlock = new System.Collections.Generic.HashSet<HookHealth>();

    // The block attacks for as long as it is up, not only on the frame Space went down: walking into
    // an enemy with the block raised, or a Red Square ramming the block, still lands the hit
    protected override void Update()
    {
        base.Update();
        if (Time.time < blockUntil) AttackInReach();
    }

    void AttackInReach()
    {
        Collider2D body = GetComponent<Collider2D>();
        Vector2 from = body != null ? (Vector2)body.bounds.center : (Vector2)transform.position;
        HookHealth target = HookHealth.InReach(from, attackReach);
        if (target == null || hitThisBlock.Contains(target)) return;
        if (target.Damage()) { hitThisBlock.Add(target); ShowHit(this, target); }
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
