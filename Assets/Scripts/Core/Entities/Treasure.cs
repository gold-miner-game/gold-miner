using System.Collections.Generic;
using UnityEngine;

// Owner: E (Treasure & Docs)
// Base for gold and diamonds (the entities are in Entities/Treasures/).
// Both the pig and the miner's hook can catch a treasure.
// Not abstract, so Tools > Build Prefabs can still add a plain Treasure.
public class Treasure : CatchableEntity
{
    // Every treasure still in the arena. MinerAI and GameManager read this.
    public static readonly List<Treasure> Active = new List<Treasure>();

    public TreasureType type = TreasureType.SmallGold;
    public int value = 50;

    protected virtual void OnEnable()  { if (!IsTaken) Active.Add(this); }
    protected virtual void OnDisable() { Active.Remove(this); }

    protected override void OnTaken() { Active.Remove(this); }

    public override void GiveReward(Catcher catcher)
    {
        if (GameManager.Instance == null) return;

        if (catcher == Catcher.Player) GameManager.Instance.AddPlayerMoney(value);
        else GameManager.Instance.AddMinerMoney(value);
    }

    // Called by Hook when it reaches the treasure (kept so Hook.cs does not change)
    public void GrabByHook(Transform hook)
    {
        CatchCommand.Execute(this, Catcher.Miner, hook);
    }
}
