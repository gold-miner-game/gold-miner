using UnityEngine;

// Who caught the entity
public enum Catcher { Player, Miner }

// The catching sequence, shared by every catchable entity (gold, diamond, pick-ups):
//   Player - the pig walked into it: the player is paid and it disappears at once
//   Miner  - the hook reached it: the miner is paid now and it rides the hook back
//            (Hook destroys it when it gets back to the miner)
public static class CatchCommand
{
    // Returns true if the entity was caught
    public static bool Execute(CatchableEntity target, Catcher catcher, Transform hook = null)
    {
        if (target == null || target.IsTaken) return false;
        if (!target.CanBeCaughtBy(catcher)) return false;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return false;

        // 1. Take it out of play so nothing else can catch it
        target.MarkTaken();
        Collider2D col = target.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        // 2. Reward whoever caught it
        target.GiveReward(catcher);

        // 3. Let it ride the hook back, or remove it now
        if (catcher == Catcher.Miner && hook != null)
        {
            target.transform.SetParent(hook, true);
            target.transform.position = hook.position;
        }
        else
        {
            Object.Destroy(target.gameObject);
        }
        return true;
    }
}
