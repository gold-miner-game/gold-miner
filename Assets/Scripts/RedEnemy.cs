using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (attack mode)
// What attack mode's enemies share (RedRound, RedSquare): asleep until the player comes within Wake
// Range (or it is hit), then it walks to the player at Move Speed along an A* path round the rock
// walls (EnemyPathfinder), replanned as the player moves; it only steers aside for the other enemies. A kinematic body: the player bumps into it, it never gets pushed.
public abstract class RedEnemy : MonoBehaviour
{
    public float moveSpeed = 1f;
    [Tooltip("It starts hunting when the player comes this close (and keeps hunting after)")]
    public float wakeRange = 11f;

    public Transform target;   // the player
    public bool Hunting { get; private set; }

    protected Rigidbody2D body;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];
    ContactFilter2D solid;

    [Tooltip("Seconds between path replans while chasing")]
    public float replanEvery = 0.4f;

    readonly List<Vector2> path = new List<Vector2>();
    int waypoint;
    float replanAt;
    Vector2 plannedFor;
    float clearance = -1f;
    Vector2 stuckFrom;
    float stuckCheckAt, noShortcutsUntil;

    static readonly float[] Turns = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };

    protected virtual void Start()
    {
        body = GetComponent<Rigidbody2D>();
        if (body == null) body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        solid = new ContactFilter2D();
        solid.NoFilter();
        solid.useTriggers = false;
    }

    // Hit while asleep (from behind cover): it comes after the player
    public void WakeUp() { Hunting = true; }

    // True while the round is on and there is a player; also wakes it when the player is near
    protected bool Ready(out float distance)
    {
        distance = 0f;
        GameManager gm = GameManager.Instance;
        if (target == null || body == null || gm == null || !gm.IsPlaying) return false;
        distance = Vector2.Distance(body.position, target.position);
        if (!Hunting && distance <= wakeRange) Hunting = true;
        return true;
    }

    // Its radius: the widest of its colliders, for the path grid
    float Clearance()
    {
        if (clearance > 0f) return clearance;
        float r = 0f;
        var cols = new List<Collider2D>();
        body.GetAttachedColliders(cols);
        foreach (Collider2D c in cols)
            if (!c.isTrigger) r = Mathf.Max(r, Mathf.Max(c.bounds.extents.x, c.bounds.extents.y));
        clearance = r > 0f ? r : 0.45f;
        return clearance;
    }

    // `length` along the A* path to the player (straight at them when no sector or path is known)
    protected void Chase(float length)
    {
        Vector2 me = body.position, goal = target.position;
        if (!EnemyPathfinder.HasSector) { Step((goal - me).normalized, length); return; }

        float r = Clearance();

        // Stuck (caught on a wall corner, or wedged against another enemy): replan now, and follow the
        // grid's cells exactly for a while instead of cutting corners
        if (Time.time >= stuckCheckAt)
        {
            bool stuck = (me - stuckFrom).sqrMagnitude < 0.0025f;   // under 5 cm in half a second
            stuckFrom = me;
            stuckCheckAt = Time.time + 0.5f;
            if (stuck) { replanAt = 0f; noShortcutsUntil = Time.time + 1.5f; }
        }

        if (Time.time >= replanAt || waypoint >= path.Count || (goal - plannedFor).sqrMagnitude > 1f)
        {
            replanAt = Time.time + replanEvery;
            plannedFor = goal;
            waypoint = 0;
            if (!EnemyPathfinder.FindPath(me, goal, r, path)) path.Clear();
        }
        if (path.Count == 0) { Step((goal - me).normalized, length); return; }

        // Done with the waypoints it has reached, and cut straight to a later one when nothing is in
        // between (smooths the grid's zig-zags without leaving the path's way round the walls)
        while (waypoint < path.Count - 1 && (path[waypoint] - me).sqrMagnitude < 0.04f) waypoint++;
        if (Time.time >= noShortcutsUntil)
            for (int ahead = Mathf.Min(path.Count - 1, waypoint + 4); ahead > waypoint; ahead--)
                if (EnemyPathfinder.Clear(me, path[ahead], r + 0.05f)) { waypoint = ahead; break; }

        Vector2 to = path[waypoint] - me;
        if (to.sqrMagnitude < 0.0001f) return;
        Step(to.normalized, Mathf.Min(length, to.magnitude));
    }

    // `length` toward `dir`, or the nearest turn of it that isn't blocked
    protected void Step(Vector2 dir, float length)
    {
        foreach (float turn in Turns)
        {
            Vector2 d = Quaternion.Euler(0f, 0f, turn) * dir;
            if (Blocked(d, length + 0.05f)) continue;
            body.MovePosition(body.position + d * length);
            return;
        }
    }

    protected bool Blocked(Vector2 dir, float length)
    {
        int n = body.Cast(dir, solid, hits, length);
        for (int i = 0; i < n; i++)
            if (hits[i].collider != null && !hits[i].collider.isTrigger && hits[i].rigidbody != body) return true;
        return false;
    }

    // No wall rock between `from` and the player
    protected bool ClearLine(Vector2 from)
    {
        foreach (RaycastHit2D hit in Physics2D.LinecastAll(from, target.position))
        {
            Rock rock = hit.collider != null ? hit.collider.GetComponent<Rock>() : null;
            if (rock != null && rock.isWall) return false;
        }
        return true;
    }
}
