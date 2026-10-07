using System.Collections.Generic;
using UnityEngine;

public enum TreasureType { SmallGold, MediumGold, LargeGold, Diamond }

// Owner: E (Treasure & Docs)
public class Treasure : MonoBehaviour
{
    // Every treasure still in the arena. MinerAI and GameManager read this.
    public static readonly List<Treasure> Active = new List<Treasure>();

    public TreasureType type = TreasureType.SmallGold;
    public int value = 50;

    public bool IsTaken { get; private set; }

    void OnEnable()  { if (!IsTaken) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    // The pig touches the treasure
    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsTaken) return;
        if (other.GetComponent<PigController>() == null) return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

        IsTaken = true;
        Active.Remove(this);
        if (GameManager.Instance != null) GameManager.Instance.AddPlayerMoney(value);
        Destroy(gameObject);
    }

    // The hook reaches the treasure: the miner is paid now, the sprite rides the hook back
    public void GrabByHook(Transform hook)
    {
        if (IsTaken) return;
        IsTaken = true;
        Active.Remove(this);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        transform.SetParent(hook, true);
        transform.position = hook.position;

        if (GameManager.Instance != null) GameManager.Instance.AddMinerMoney(value);
    }
}
