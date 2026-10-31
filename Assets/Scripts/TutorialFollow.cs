using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Keeps a tutorial prop (a spotlight Hole, a Callout) on the player or on another object while the
// game runs. In the editor it stays where you put it.
public class TutorialFollow : MonoBehaviour
{
    [Tooltip("Follow the player's character")]
    public bool followPlayer = true;
    [Tooltip("Follow this instead (when Follow Player is off)")]
    public Transform target;
    [Tooltip("Kept this far from what it follows, in world units")]
    public Vector2 offset;

    PlayerCharacter player;

    void LateUpdate()
    {
        Transform follow = target;
        if (followPlayer)
        {
            if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
            follow = player != null ? player.transform : null;
        }
        if (follow == null) return;
        transform.position = new Vector3(follow.position.x + offset.x, follow.position.y + offset.y, transform.position.z);
    }
}
