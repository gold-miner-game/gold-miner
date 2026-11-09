using UnityEngine;

// Owner: Penny (Level map & tutorial)
// A way out: the round is won when the player reaches it. SqueezePhase adds it to the Exit prefab
// when the exit opens; it can also be put on any object in a level scene.
public class LevelExit : MonoBehaviour
{
    [Tooltip("How close the player's centre has to come, in world units")]
    public float radius = 0.8f;
    public string winText = "You escaped!";

    public const string ReachedEvent = "exit_reached";

    PlayerCharacter player;

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying || Time.timeScale == 0f) return;   // not while a step freezes the game
        if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
        if (player == null) return;
        if (Vector2.Distance(player.transform.position, transform.position) > radius) return;

        TutorialEvents.Fire(ReachedEvent);
        gm.WinRound(winText);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
