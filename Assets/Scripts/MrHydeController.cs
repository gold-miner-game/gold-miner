using UnityEngine;

// Owner: B (Player & Rocks)
// Mr. Hyde (the pig). Moves, eats and blinks like every character (PlayerCharacter);
// its skill is the block: Space bounces a hook off, or knocks treasure off a passing hook.
public class MrHydeController : PlayerCharacter
{
    [Header("Block (Space)")]
    public bool canBlock = false;       // turned on per level by LevelLoader
    public float blockDuration = 0.8f;  // how long one block lasts
    public float blockCooldown = 1f;    // wait after a block ends before the next one
    public Color blockTint = new Color(0.45f, 0.75f, 1f);

    public override bool IsBlocking => Time.time < blockUntil;
    public override string SkillHint => CanBlock ? "[Space] block" : "";

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
        if (!CanBlock || Time.time < blockReadyAt) return;

        blockUntil = Time.time + BlockDuration;
        blockReadyAt = blockUntil + BlockCooldown;
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
