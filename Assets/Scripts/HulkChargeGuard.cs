using System.Collections.Generic;
using UnityEngine;

// Circular protection and a separate front 60-degree rock-breaking sector.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class HulkChargeGuard : MonoBehaviour
{
    public const float Arc = 60f;
    public const float RadiusScale = .75f;
    const int ArcSteps = 24;
    const int ShieldSteps = 64;
    static readonly List<HulkChargeGuard> active = new List<HulkChargeGuard>();
    readonly HashSet<RedEnemy> pushed = new HashSet<RedEnemy>();
    readonly List<Collider2D> overlaps = new List<Collider2D>();
    readonly List<RaycastHit2D> sightHits = new List<RaycastHit2D>();
    RhinoController owner;
    Collider2D body;
    LineRenderer outline;
    LineRenderer attackOutline;
    Material outlineMaterial;
    readonly Vector3[] outlinePoints = new Vector3[ShieldSteps];
    readonly Vector3[] attackPoints = new Vector3[ArcSteps + 2];

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
    public void BeginCharge() { pushed.Clear(); Tick(); UpdateOutline(); }
    public void EndCharge()
    {
        if (outline != null) outline.enabled = false;
        if (attackOutline != null) attackOutline.enabled = false;
    }
    void Update() { Tick(); }
    void FixedUpdate() { Tick(); }
    void LateUpdate() { UpdateOutline(); }

    Vector2 ArcPoint(float angle) => Centre + (Vector2)(Quaternion.Euler(0, 0, angle) * Forward) * Radius;

    // Closest point on the finite sector, including both radial edges and its arc.
    public Vector2 ClosestAttackPoint(Vector2 point)
    {
        Vector2 delta = point - Centre;
        if (delta.sqrMagnitude < 1e-10f) return Centre;
        float angle = Mathf.Clamp(Vector2.SignedAngle(Forward, delta), -Arc * .5f, Arc * .5f);
        Vector2 ray = Quaternion.Euler(0, 0, angle) * Forward;
        return Centre + ray * Mathf.Clamp(Vector2.Dot(delta, ray), 0f, Radius);
    }

    public bool Contains(Vector2 point, float padding = 0f)
        => IsGuarding && (point - Centre).sqrMagnitude <= (Radius + padding) * (Radius + padding) + 1e-8f;

    public bool ContainsAttack(Vector2 point)
        => IsGuarding && (point - ClosestAttackPoint(point)).sqrMagnitude <= 1e-8f;

    static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + t * ab)).sqrMagnitude;
    }
    // Exact swept-circle protection, including side and rear approaches.
    public bool Intercepts(Vector2 from, Vector2 to, float padding)
    {
        float radius = Radius + padding;
        return IsGuarding && PointSegment(Centre, from, to) <= radius * radius + 1e-8f;
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

    bool Touches(Collider2D target, bool attack = false)
    {
        if (target == null || !target.enabled || !IsGuarding) return false;
        Vector2 point = target.ClosestPoint(Centre);
        if ((attack ? ContainsAttack(point) : Contains(point)) && ClearTo(point, target)) return true;
        int steps = attack ? ArcSteps : ShieldSteps;
        float arc = attack ? Arc : 360f;
        for (int i = 0; i <= steps; i++)
        {
            point = target.ClosestPoint(ArcPoint(Mathf.Lerp(-arc * .5f, arc * .5f, i / (float)steps)));
            if ((attack ? ContainsAttack(point) : Contains(point)) && ClearTo(point, target)) return true;
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
        Vector2 direction = away.sqrMagnitude > .0001f ? away.normalized : Forward;
        enemy.PushBack(direction, 2f, .2f);
        // Mobile-enemy blocks need the same visible feedback as deflected claws.
        BlockFlash.Show(Centre + direction * Radius, direction, Radius * .6f);
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
        if (rock == null || rock.RhinoProof || !Touches(rock.GetComponent<Collider2D>(), true)) return false;
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
            outlineMaterial = new Material(Shader.Find("Sprites/Default"));
            outline = MakeOutline("Hulk 360 degree shield", new Color(.65f, .93f, 1f, .9f), ShieldSteps, 30010);
            attackOutline = MakeOutline("Hulk 60 degree attack", new Color(1f, .8f, .5f, .95f), attackPoints.Length, 30011);
        }
        outline.enabled = attackOutline.enabled = true;
        // Preserve legibility in the following camera without expanding gameplay range.
        Camera camera = Camera.main;
        float pixel = camera != null && camera.orthographic ? 2f * camera.orthographicSize / Mathf.Max(1, camera.pixelHeight) : 0f;
        outline.startWidth = outline.endWidth = Mathf.Max(Radius * .035f, pixel * 2f);
        attackOutline.startWidth = attackOutline.endWidth = Mathf.Max(Radius * .025f, pixel * 1.5f);
        for (int i = 0; i < ShieldSteps; i++) outlinePoints[i] = ArcPoint(i * 360f / ShieldSteps);
        attackPoints[0] = Centre;
        for (int i = 0; i <= ArcSteps; i++)
            attackPoints[i + 1] = ArcPoint(Mathf.Lerp(-Arc * .5f, Arc * .5f, i / (float)ArcSteps));
        outline.SetPositions(outlinePoints);
        attackOutline.SetPositions(attackPoints);
    }
    LineRenderer MakeOutline(string name, Color color, int points, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        // These range indicators are not physical objects that Elsa can freeze.
        go.AddComponent<FrostIgnore2D>();
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = outlineMaterial;
        line.useWorldSpace = true; line.loop = true;
        line.positionCount = points;
        line.startColor = line.endColor = color;
        line.sortingLayerID = owner.body != null ? owner.body.sortingLayerID : 0;
        line.sortingOrder = order;
        return line;
    }
    void OnDestroy()
    {
        active.Remove(this);
        if (outline != null) Destroy(outline.gameObject);
        if (attackOutline != null) Destroy(attackOutline.gameObject);
        if (outlineMaterial != null) Destroy(outlineMaterial);
    }
}
