// Base for items only the pig collects (the entities are in Entities/PickUps/).
// The hook passes through them.
public abstract class PickUp : CatchableEntity
{
    public override bool CanBeCaughtBy(Catcher catcher) { return catcher == Catcher.Player; }
}
