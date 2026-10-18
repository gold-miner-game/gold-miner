// JK. Rolling's cross-level record. Picking her on the select screen starts a fresh one.
// Holds the roll's numbers so the store can change them. They start from the Cat prefab's
// CatController values the first time she plays (seed), then the record is what counts.
public class CatStatus : CrossLevelStatus
{
    float rollSpeedMultiplier = 3f;
    float rollDuration = .5f;
    float rollCooldown = 1f;
    bool seeded;

    public virtual float getRollSpeedMultiplier() { return rollSpeedMultiplier; }
    public virtual void setRollSpeedMultiplier(float value) { rollSpeedMultiplier = value; }

    public virtual float getRollDuration() { return rollDuration; }
    public virtual void setRollDuration(float value) { rollDuration = value; }

    public virtual float getRollCooldown() { return rollCooldown; }
    public virtual void setRollCooldown(float value) { rollCooldown = value; }

    // Called by CatController: take the prefab's numbers once, at the start of a run
    public virtual void seed(float speedMultiplier, float duration, float cooldown)
    {
        if (seeded) return;
        seeded = true;
        rollSpeedMultiplier = speedMultiplier;
        rollDuration = duration;
        rollCooldown = cooldown;
    }

    protected override void copyFrom(CrossLevelStatus other)
    {
        base.copyFrom(other);
        if (!(other is CatStatus cat)) return;
        rollSpeedMultiplier = cat.rollSpeedMultiplier;
        rollDuration = cat.rollDuration;
        rollCooldown = cat.rollCooldown;
        seeded = cat.seeded;
    }
}
