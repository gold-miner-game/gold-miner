using UnityEngine;

// Base for everything that can be caught. It brings a PickUpTrigger with it,
// so the pig picks it up automatically and entity scripts only say what they
// give and who may catch them. The catch itself is CatchCommand.
[RequireComponent(typeof(PickUpTrigger))]
public abstract class CatchableEntity : MonoBehaviour
{
    public bool IsTaken { get; private set; }

    protected virtual void Awake()
    {
        // Prefabs made before PickUpTrigger existed do not have it yet
        if (GetComponent<PickUpTrigger>() == null) gameObject.AddComponent<PickUpTrigger>();
    }

    // Treasure: pig and hook. Pick-ups: pig only.
    public virtual bool CanBeCaughtBy(Catcher catcher) { return true; }

    // Money, a Life, ... for whoever caught it
    public abstract void GiveReward(Catcher catcher);

    // Only CatchCommand calls this
    public void MarkTaken()
    {
        if (IsTaken) return;
        IsTaken = true;
        OnTaken();
    }

    protected virtual void OnTaken() { }
}
