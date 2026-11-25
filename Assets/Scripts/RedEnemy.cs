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
    public bool IsFrozen => FrostField2D.IsFrozen(transform);

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
        if (target == null || body == null || gm == null || !gm.IsPlaying || IsFrozen) return false;
        if (InPack) EnemyPack.Tick(target);
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

    // In the pack (EnemyPack) when cooperation is on: it walks to the spot the pack gives it instead
    // of straight at the player. The Gold Round, which is after the gold, stays out
    public virtual bool InPack => true;
    public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
    public static IReadOnlyList<RedEnemy> All => all;

    // The pack's spot for it this moment, when it has one
    protected bool PackOrder(out Vector2 goal, out EnemyPack.Role role)
    {
        goal = default; role = EnemyPack.Role.None;
        return InPack && EnemyPack.TryGetOrder(this, out goal, out role);
    }

    // Standing (within `within`) on the pack's spot for it
    protected bool AtPackSpot(float within)
    {
        return PackOrder(out Vector2 goal, out _) && (goal - Position).sqrMagnitude <= within * within;
    }

    // `length` toward the player: along the A* path to the pack's spot for it when cooperating, else
    // to the player (straight at them when no sector or path is known)
    protected void Chase(float length)
    {
        Vector2 goal = target.position;
        if (PackOrder(out Vector2 spot, out _)) goal = spot;
        MoveTo(goal, length);
    }

    // `length` along the A* path round the walls to `goal`
    protected void MoveTo(Vector2 goal, float length)
    {
        Vector2 me = body.position;
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

    // `length` toward `dir` (bent away from the other enemies), or the nearest turn of it that isn't blocked
    protected void Step(Vector2 dir, float length)
    {
        dir = AvoidOthers(dir);
        foreach (float turn in Turns)
        {
            Vector2 d = Quaternion.Euler(0f, 0f, turn) * dir;
            if (Blocked(d, length + 0.05f)) continue;
            body.MovePosition(body.position + d * length);
            return;
        }
    }

    // --- Pushed back (the pig's attack) --------------------------------------------------------------
    Vector2 pushDir;
    float pushSpeed, pushUntil = -1f;
    public bool BeingPushed => Time.time < pushUntil;   // read-only, for the art

    // Slide `distance` world units along `away` over `time` seconds, stopped by walls; it doesn't
    // chase meanwhile
    public void PushBack(Vector2 away, float distance, float time)
    {
        if (away.sqrMagnitude < 0.0001f) return;
        Sfx.Play("enemy_hit", transform.position); Sfx.Play("enemy_yowl", transform.position);   // sound: knocked back
        pushDir = away.normalized;
        pushSpeed = distance / Mathf.Max(0.01f, time);
        pushUntil = Time.time + time;
        replanAt = 0f;   // it was moved: a fresh path afterwards
    }

    // True while being pushed (and moves it): FixedUpdate does nothing else then
    protected bool Pushed()
    {
        if (IsFrozen)
        {
            if (body != null) body.linearVelocity = Vector2.zero;
            return true;
        }
        if (body == null || Time.time >= pushUntil) return false;
        float step = pushSpeed * Time.fixedDeltaTime;
        if (Blocked(pushDir, step + 0.05f)) { pushUntil = -1f; return false; }
        body.MovePosition(body.position + pushDir * step);
        return true;
    }

    // --- Getting past each other ---------------------------------------------------------------------
    // The enemies don't block each other (two walking head-on used to wedge, every way out "blocked"
    // by the other). Instead each one is pushed away from any that come too close, and one coming
    // straight at it steps to its own right, so the two pass on opposite sides.

    static readonly List<RedEnemy> all = new List<RedEnemy>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); }

    protected virtual void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    protected virtual void OnDisable() { all.Remove(this); }

    Vector2 AvoidOthers(Vector2 dir)
    {
        if (body == null || dir.sqrMagnitude < 0.0001f) return dir;
        Vector2 me = body.position;
        float r = Clearance();
        Vector2 push = Vector2.zero;
        Vector2 right = new Vector2(dir.y, -dir.x);
        foreach (RedEnemy other in all)
        {
            if (other == null || other == this || other.body == null) continue;
            Vector2 away = me - other.body.position;
            float d = away.magnitude;
            float range = r + other.Clearance() + 0.35f;
            // A cooperating pack spreads out: on its way to its slot a member keeps EnemyPack.Spacing
            // from the others (not once it is inside the circle and going for the player)
            if (other.InPack && EnemyPack.RoleOf(this) == EnemyPack.Role.Blocker)
                range = Mathf.Max(range, EnemyPack.Spacing);
            if (d >= range) continue;
            // On top of each other: split by who is who, so they never pick the same side
            Vector2 apart = d > 0.001f ? away / d : (GetInstanceID() < other.GetInstanceID() ? right : -right);
            push += apart * (1f - d / range);
            // Coming at each other: both step right, so they pass on opposite sides
            if (Vector2.Dot(dir, -apart) > 0.6f) push += right * 0.8f * (1f - d / range);
        }
        if (push.sqrMagnitude < 0.0001f) return dir;
        Vector2 bent = dir + push * 1.5f;
        return bent.sqrMagnitude > 0.0001f ? bent.normalized : right;
    }

    protected bool Blocked(Vector2 dir, float length)
    {
        int n = body.Cast(dir, solid, hits, length);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null || c.isTrigger || hits[i].rigidbody == body) continue;
            if (hits[i].rigidbody != null && hits[i].rigidbody.GetComponent<RedEnemy>() != null) continue;   // another enemy: steered round, not a wall
            return true;
        }
        return false;
    }

    // Play mode, Scene view: a line to its pack spot, coloured by its role (EnemyPack.RoleColor)
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (all.Count > 0 && all[0] == this) EnemyPack.DrawState();   // once, not once per enemy
        if (!PackOrder(out Vector2 goal, out EnemyPack.Role role)) return;
        Gizmos.color = EnemyPack.RoleColor(role);
        Gizmos.DrawLine(Position, goal);
        Gizmos.DrawWireSphere(goal, 0.25f);
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
