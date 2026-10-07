using UnityEngine;

// Optional feature. Owner: E (Treasure & Docs)
public class LifePickup : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PigController>() == null) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;

        GameManager.Instance.AddLife();
        Destroy(gameObject);
    }
}
