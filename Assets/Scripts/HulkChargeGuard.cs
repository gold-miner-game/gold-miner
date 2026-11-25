using System.Collections.Generic;
using UnityEngine;

// Source-aware defence: the front 60 degrees only, never global invulnerability.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class HulkChargeGuard : MonoBehaviour
{
    public const float Arc = 60f;
    public const float RadiusScale = .75f;
    const int ArcSteps = 24;
    static readonly List<HulkChargeGuard> active = new List<HulkChargeGuard>();
    readonly HashSet<RedEnemy> pushed = new HashSet<RedEnemy>();
    readonly List<Collider2D> overlaps = new List<Collider2D>();
    readonly List<RaycastHit2D> sightHits = new List<RaycastHit2D>();
    RhinoController owner;
    Collider2D body;
    LineRenderer outline;
    Material outlineMaterial;
    readonly Vector3[] outlinePoints = new Vector3[ArcSteps + 2];

    public bool IsGuarding => Application.isPlaying && isActiveAndEnabled && owner != null
        && owner.IsCharging && PlayerCharacter.IsPlaying;
    public Vector2 Centre => body != null ? (Vector2)body.bounds.center : (Vector2)transform.position;
    public float Radius => body != null ? Mathf.Max(body.bounds.size.x, body.bounds.size.y) * RadiusScale
        : Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)) * RadiusScale;
    public Vector2 Forward => owner != null ? owner.ChargeDirection : Vector2.right;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active.Clear(); }
    void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    void OnDisable() { active.Remove(this); EndCharge(); }
    public void Initialize(RhinoController character) { owner = character; body = character.GetComponent<Collider2D>(); }
    public void BeginCharge() { pushed.Clear(); Tick(); }
    public void EndCharge() { if (outline != null) outline.enabled = false; }
    void Update() { Tick(); }
    void FixedUpdate() { Tick(); }
    void LateUpdate() { UpdateOutline(); }

    Vector2 ArcPoint(float angle) => Centre + (Vector2)(Quaternion.Euler(0, 0, angle) * Forward) * Radius;

    // Closest point on the finite sector, including both radial edges and its arc.
    public Vector2 ClosestPoint(Vector2 point)
    {
        Vector2 delta = point - Centre;
        if (delta.sqrMagnitude < 1e-10f) return Centre;
        float angle = Mathf.Clamp(Vector2.SignedAngle(Forward, delta), -Arc * .5f, Arc * .5f);
        Vector2 ray = Quaternion.Euler(0, 0, angle) * Forward;
        return Centre + ray * Mathf.Clamp(Vector2.Dot(delta, ray), 0f, Radius);
    }

    public bool Contains(Vector2 point, float padding = 0f)
        => IsGuarding && (point - ClosestPoint(point)).sqrMagnitude <= padding * padding + 1e-8f;

    static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + t * ab)).sqrMagnitude;
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a, cd = d - c;
        float det = Cross(ab, cd);
        if (Mathf.Abs(det) > 1e-8f)
        {
            float t = Cross(c - a, cd) / det, u = Cross(c - a, ab) / det;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }
        return Mathf.Min(Mathf.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
            Mathf.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
    }

    // A swept claw cannot skip a small sector between render frames.
    public bool Intercepts(Vector2 from, Vector2 to, float padding)
    {
        if (!IsGuarding) return false;
        if (Contains(from, padding) || Contains(to, padding)) return true;
        float limit = padding * padding + 1e-8f;
        Vector2 first = ArcPoint(-Arc * .5f), last = ArcPoint(Arc * .5f);
        if (SegmentDistance(from, to, Centre, first) <= limit
            || SegmentDistance(from, to, Centre, last) <= limit) return true;
        Vector2 previous = first;
        for (int i = 1; i <= ArcSteps; i++)
        {
            Vector2 next = ArcPoint(Mathf.Lerp(-Arc * .5f, Arc * .5f, i / (float)ArcSteps));
            if (SegmentDistance(from, to, previous, next) <= limit) return true;
            previous = next;
        }
        return false;
    }

    public bool TryDeflect(Hook hook, Vector2 from, Vector2 to)
    {
        if (hook == null || !hook.IsBusy || hook.Defended || !Intercepts(from, to, hook.hitRadius)) return false;
        float low = 0f, high = 1f;
        for (int i = 0; i < 12; i++)
        {
            float mid = (low + high) * .5f;
            if (Intercepts(from, Vector2.Lerp(from, to, mid), hook.hitRadius)) high = mid; else low = mid;
        }
        hook.DeflectFromCharge(owner, Vector2.Lerp(from, to, high));
        return true;
    }
    public static bool TryDeflectHook(Hook hook, Vector2 from, Vector2 to)
    {
        foreach (HulkChargeGuard guard in active)
            if (guard != null && guard.TryDeflect(hook, from, to)) return true;
        return false;
    }

    bool Touches(Collider2D target)
    {
        if (target == null || !target.enabled || !IsGuarding) return false;
        Vector2 point = target.ClosestPoint(Centre);
        if (Contains(point) && ClearTo(point, target)) return true;
        for (int i = 0; i <= ArcSteps; i++)
        {
            point = target.ClosestPoint(ArcPoint(Mathf.Lerp(-Arc * .5f, Arc * .5f, i / (float)ArcSteps)));
            if (Contains(point) && ClearTo(point, target)) return true;
        }
        return false;
    }
    bool ClearTo(Vector2 point, Collider2D target)
    {
        Vector2 delta = point - Centre;
        if (delta.sqrMagnitude < 1e-8f) return true;
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        Physics2D.Raycast(Centre, delta.normalized, filter, sightHits, delta.magnitude);
        foreach (RaycastHit2D hit in sightHits)
        {
            Collider2D obstacle = hit.collider;
            if (obstacle == null || obstacle == target || obstacle.transform.IsChildOf(owner.transform)) continue;
            if (obstacle.GetComponentInParent<RedEnemy>() != null || obstacle.GetComponentInParent<Treasure>() != null) continue;
            return false;
        }
        return true;
    }
    void Repel(RedEnemy enemy)
    {
        if (!pushed.Add(enemy)) return;
        // A physical repel shatters the ice first, so a frozen enemy can actually
        // be displaced rather than consuming its knockback timer while stationary.
        if (enemy.IsFrozen) FrostField2D.BreakIce(enemy.transform);
        Vector2 away = enemy.Position - Centre;
        enemy.PushBack(away.sqrMagnitude > .0001f ? away.normalized : Forward, 2f, .2f);
    }
    public static bool TryBlockEnemy(PlayerCharacter player, RedEnemy enemy)
    {
        HulkChargeGuard guard = player != null ? player.GetComponent<HulkChargeGuard>() : null;
        if (guard == null || enemy == null || !guard.Touches(enemy.GetComponent<Collider2D>())) return false;
        guard.Repel(enemy);
        return true;
    }
    public bool TrySmashRock(Rock rock)
    {
        if (rock == null || rock.RhinoProof || !Touches(rock.GetComponent<Collider2D>())) return false;
        if (rock.IsFrozen) FrostField2D.BreakIce(rock.transform);
        return rock.SmashImmediately();
    }
    void Tick()
    {
        if (!IsGuarding) { EndCharge(); return; }
        Hook.RepelFrom(this);
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        Physics2D.OverlapCircle(Centre, Radius, filter, overlaps);
        foreach (Collider2D target in overlaps)
        {
            if (target == null || target.transform.IsChildOf(owner.transform)) continue;
            Rock rock = target.GetComponentInParent<Rock>();
            if (rock != null) TrySmashRock(rock);
            RedEnemy enemy = target.GetComponentInParent<RedEnemy>();
            if (enemy != null && Touches(target)) Repel(enemy);
        }
    }
    void UpdateOutline()
    {
        if (!IsGuarding) { EndCharge(); return; }
        if (outline == null)
        {
            var go = new GameObject("Hulk 60 degree guard"); go.transform.SetParent(transform, false);
            outline = go.AddComponent<LineRenderer>();
            outlineMaterial = new Material(Shader.Find("Sprites/Default"));
            outline.sharedMaterial = outlineMaterial;
            outline.useWorldSpace = true; outline.loop = true;
            outline.positionCount = outlinePoints.Length;
            outline.startColor = outline.endColor = new Color(1f, .8f, .7f, .7f);
            outline.sortingLayerID = owner.body != null ? owner.body.sortingLayerID : 0;
            outline.sortingOrder = 30010;
        }
        outline.enabled = true;
        outline.startWidth = outline.endWidth = Mathf.Max(.008f, Radius * .025f);
        outlinePoints[0] = Centre;
        for (int i = 0; i <= ArcSteps; i++)
            outlinePoints[i + 1] = ArcPoint(Mathf.Lerp(-Arc * .5f, Arc * .5f, i / (float)ArcSteps));
        outline.SetPositions(outlinePoints);
    }
    void OnDestroy()
    {
        active.Remove(this);
        if (outlineMaterial != null) Destroy(outlineMaterial);
    }
}
