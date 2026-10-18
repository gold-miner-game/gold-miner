// Default player (Mr. Hyde). Other characters extend CrossLevelStatus the same way and call Prefer<TheirStatus>().
public class PigStatus : CrossLevelStatus
{
    // Block is Mr. Hyde's signature skill, so it is on from the start of a run. The store can tune
    // its duration/cooldown, and a level file can still override it.
    protected override void onReady()
    {
        base.onReady();
        setCanBlock(true);
    }
}
