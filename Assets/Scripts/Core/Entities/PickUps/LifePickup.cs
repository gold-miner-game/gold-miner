// Life item (optional): the pig gains one Life
public class LifePickup : PickUp
{
    // A Life needs a GameManager; without one the item stays in the scene
    public override bool CanBeCaughtBy(Catcher catcher)
    {
        return base.CanBeCaughtBy(catcher) && GameManager.Instance != null;
    }

    public override void GiveReward(Catcher catcher)
    {
        GameManager.Instance.AddLife();
    }
}
