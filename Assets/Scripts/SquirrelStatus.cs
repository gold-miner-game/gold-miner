// Scrat's cross-level record. Picking him on the select screen starts a fresh one.
// Holds the firm tail's numbers so the store can change them. They start from the Squirrel prefab's
// values (SquirrelController, SquirrelTailCollector) the first time he plays (seed).
public class SquirrelStatus : CrossLevelStatus
{
    float firmSpeedMultiplier = .5f;
    float hookHold = 1f;
    bool seeded;

    // Speed while the tail is firm, times the normal speed
    public virtual float getFirmSpeedMultiplier() { return firmSpeedMultiplier; }
    public virtual void setFirmSpeedMultiplier(float value) { firmSpeedMultiplier = value; }

    // Seconds a hook still waits after it stops touching the firm tail
    public virtual float getHookHold() { return hookHold; }
    public virtual void setHookHold(float value) { hookHold = value; }

    // Called by SquirrelController: take the prefab's numbers once, at the start of a run
    public virtual void seed(float firmSpeed, float hold)
    {
        if (seeded) return;
        seeded = true;
        firmSpeedMultiplier = firmSpeed;
        hookHold = hold;
    }

    protected override void copyFrom(CrossLevelStatus other)
    {
        base.copyFrom(other);
        if (!(other is SquirrelStatus squirrel)) return;
        firmSpeedMultiplier = squirrel.firmSpeedMultiplier;
        hookHold = squirrel.hookHold;
        seeded = squirrel.seeded;
    }
}
