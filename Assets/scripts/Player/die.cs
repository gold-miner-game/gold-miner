using UnityEngine;
using UnityEngine.Events;

// Put this on the Player. If the miner's hook catches the player, it's game over.
public class die : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent onGameOver; // Hook up UI / sounds here later

    public bool IsDead { get; private set; }

    // Called by MinerHook when the hook catches the player
    public void Die(Transform hook)
    {
        if (IsDead)
        {
            return;
        }
        IsDead = true;

        // Stop the player: no more moving or fetching
        if (TryGetComponent(out fetch fetcher)) fetcher.enabled = false;
        if (TryGetComponent(out player movement)) movement.enabled = false;
        if (TryGetComponent(out Rigidbody2D body))
        {
            body.linearVelocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        // Stick to the hook where it caught us
        transform.position = hook.position;

        Debug.Log("Game Over: the player was caught by the hook");
        onGameOver.Invoke();

        // LevelManager shows GAME OVER and handles retry
        if (LevelManager.Instance != null)
        {
            LevelManager.Instance.PlayerCaught();
        }

        // Freeze the game
        Time.timeScale = 0f;
    }
}
