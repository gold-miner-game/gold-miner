using UnityEngine;

// Optional feature. Owner: E (Treasure & Docs)
public class LifePickup : MonoBehaviour
{
    private bool taken;

    void OnTriggerEnter2D(Collider2D other)
    {
        TryPickUp(other.GetComponent<PlayerCharacter>());
    }

    // Also called by the squirrel's tail. Returns true if the character picked it up
    public bool TryPickUp(PlayerCharacter player)
    {
        if (taken || player == null) return false;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return false;

        taken = true;   // the body and tail can touch it in the same frame: only one Life
        GameManager.Instance.AddLife();
        Destroy(gameObject);
        return true;
    }
}
