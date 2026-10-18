// Rero Rero's cross-level record. Picking the frog on the select screen starts a fresh one.
// Holds the tongue's numbers so the store can change them. They start from the Frog prefab's
// FrogTongue values the first time it plays (seed), then the record is what counts.
public class FrogStatus : CrossLevelStatus
{
    float tongueReach = 6f;          // times the frog's width
    float tongueSpeed = 8f;          // shooting out
    float tongueReturnSpeed = 6f;    // coming back
    float tongueSnapMultiplier = 3f; // the part beyond a mid-tongue grab, times tongueSpeed
    bool seeded;

    public virtual float getTongueReach() { return tongueReach; }
    public virtual void setTongueReach(float value) { tongueReach = value; }

    public virtual float getTongueSpeed() { return tongueSpeed; }
    public virtual void setTongueSpeed(float value) { tongueSpeed = value; }

    public virtual float getTongueReturnSpeed() { return tongueReturnSpeed; }
    public virtual void setTongueReturnSpeed(float value) { tongueReturnSpeed = value; }

    public virtual float getTongueSnapMultiplier() { return tongueSnapMultiplier; }
    public virtual void setTongueSnapMultiplier(float value) { tongueSnapMultiplier = value; }

    // Called by FrogTongue: take the prefab's numbers once, at the start of a run
    public virtual void seed(float reach, float speed, float returnSpeed, float snapMultiplier)
    {
        if (seeded) return;
        seeded = true;
        tongueReach = reach;
        tongueSpeed = speed;
        tongueReturnSpeed = returnSpeed;
        tongueSnapMultiplier = snapMultiplier;
    }

    protected override void copyFrom(CrossLevelStatus other)
    {
        base.copyFrom(other);
        if (!(other is FrogStatus frog)) return;
        tongueReach = frog.tongueReach;
        tongueSpeed = frog.tongueSpeed;
        tongueReturnSpeed = frog.tongueReturnSpeed;
        tongueSnapMultiplier = frog.tongueSnapMultiplier;
        seeded = frog.seeded;
    }
}
