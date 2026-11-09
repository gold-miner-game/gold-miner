using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Which coordinates a tutorial prop (a spotlight Hole, a Callout) is placed in:
//   Follow Player off   world coordinates: it stays where you put it
//   Follow Player on    the player's: place it in the Scene view relative to the level's
//                       PlayerStart, and in play it keeps that same place relative to the player
// With Target set (and Follow Player off) it is placed relative to that object instead.
public class TutorialFollow : MonoBehaviour
{
    [Tooltip("On: placed relative to the player (as you put it next to PlayerStart) and moves with them. Off: stays where it is")]
    public bool followPlayer = false;
    [Tooltip("Follow this instead (when Follow Player is off). Empty = stay put")]
    public Transform target;

    Vector2 offset;
    bool placed;
    PlayerCharacter player;

    // Where it sits relative to what it follows, from the scene's layout, before anything moves
    void Awake()
    {
        Vector3 anchor;
        if (Anchor(out anchor))
        {
            offset = (Vector2)(transform.position - anchor);
            placed = true;
        }
    }

    // PlayerStart for the player (the player starts there), else the target's own position
    bool Anchor(out Vector3 at)
    {
        at = Vector3.zero;
        if (followPlayer)
        {
            LevelLayout layout = LevelLayout.Active != null ? LevelLayout.Active : FindFirstObjectByType<LevelLayout>();
            if (layout == null) return false;
            at = layout.PlayerStart;
            return true;
        }
        if (target == null) return false;
        at = target.position;
        return true;
    }

    void LateUpdate()
    {
        if (!placed) return;
        Transform follow = target;
        if (followPlayer)
        {
            if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
            follow = player != null ? player.transform : null;
        }
        if (follow == null) return;
        transform.position = new Vector3(follow.position.x + offset.x, follow.position.y + offset.y, transform.position.z);
    }

    // A line to what it is placed relative to, so the Scene view shows which coordinates it uses
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying || !Anchor(out Vector3 anchor)) return;
        Gizmos.color = new Color(1f, 0.72f, 0.82f);
        Gizmos.DrawLine(anchor, transform.position);
        Gizmos.DrawWireSphere(anchor, 0.15f);
    }
}
