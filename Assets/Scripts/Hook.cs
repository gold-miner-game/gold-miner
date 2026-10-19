using System.Collections.Generic;
using UnityEngine;

// Owner: D (Hook & UI)
// ExecuteAlways: the claw is also drawn in edit mode, so the scene shows the real look before Play
[ExecuteAlways]
public class Hook : MonoBehaviour
{
    [Header("Speed")]
    public float extendSpeed = 12f;
    public float retractSpeed = 16f;

    [Header("Weight (retract speed multiplier)")]
    public float smallGoldSpeed = 1.0f;
    public float mediumGoldSpeed = 0.7f;
    public float largeGoldSpeed = 0.45f;
    public float diamondSpeed = 1.1f;

    [Header("Collision")]
    public float hitRadius = 0.2f;
    public bool hitsPig = true;      // false: the hook passes through the pig (no-kill levels)

    [Header("Look")]
    public Color defaultColor = Color.yellow;
    public Material lineMaterial;    // defaults to the material of this object's sprite
    public float ropeWidth = 0.05f;
    public Color outlineColor = new Color(0.15f, 0.12f, 0.1f);
    public float outlineWidth = 0.03f;   // extra width on each side

    [Header("Claw")]
    public float clawLength = 0.35f;
    public float clawWidth = 0.06f;
    public float clawOpenAngle = 40f;    // degrees from the center line
    public float clawClosedAngle = 12f;
    public float clawCloseSpeed = 10f;   // how fast it opens/closes

    public bool IsBusy => state != State.Idle;

    // Held in place (by the squirrel's firm tail): nothing moves, then the shot carries on
    public bool IsStalled => Time.time < stallUntil;

    // Counts shots. A new shot jumps the hook to its start, which isn't movement along a path
    public int ShotCount { get; private set; }

    // The treasure this hook is pulling home, if any
    public Treasure Carried => carried;

    private enum State { Idle, Extending, Retracting }
    private State state = State.Idle;

    private Vector2 origin;
    private Vector2 dir = Vector2.down;
    private float maxLength;
    private float traveled;
    private Treasure carried;
    private float holdUntil;   // PullBack: the hook waits on its treasure until then
    private float stallUntil;  // Hold: the hook is stopped until then

    private Stroke rope;
    private Stroke clawLeft;
    private Stroke clawRight;
    private float clawAngle;
    private Color color;

    // Colliders this shot has already reacted to, so nothing is handled twice
    private readonly HashSet<Collider2D> touched = new HashSet<Collider2D>();

    void OnEnable()
    {
        // The placeholder sprite is hidden and only lends its material and sorting.
        // forceRenderingOff is not serialized, so the scene file is left untouched.
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        Material material = lineMaterial;
        int sortingLayer = 0;
        int sortingOrder = 5;
        if (sprite != null)
        {
            if (material == null) material = sprite.sharedMaterial;
            sortingLayer = sprite.sortingLayerID;
            sortingOrder = sprite.sortingOrder;
            sprite.forceRenderingOff = true;
        }

        // Back to front: rope outline, rope, claw outlines, claws (all above treasure at 0)
        rope = new Stroke(transform, "Rope", 2, material, sortingLayer, sortingOrder - 3);
        clawLeft = new Stroke(transform, "ClawLeft", 3, material, sortingLayer, sortingOrder - 1);
        clawRight = new Stroke(transform, "ClawRight", 3, material, sortingLayer, sortingOrder - 1);

        clawAngle = clawOpenAngle;
        color = defaultColor;
        UpdateRope();
        UpdateClaw();
    }

    void OnDisable()
    {
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null) sprite.forceRenderingOff = false;

        rope?.Destroy();
        clawLeft?.Destroy();
        clawRight?.Destroy();
        rope = clawLeft = clawRight = null;
    }

    public void Fire(Vector2 from, Vector2 direction, float length)
    {
        Fire(from, direction, length, defaultColor);
    }

    // Called by MinerAI when the telegraph ends. Yellow = treasure shot, red = player shot.
    public void Fire(Vector2 from, Vector2 direction, float length, Color shotColor)
    {
        if (IsBusy) return;

        origin = from;
        dir = direction.normalized;
        maxLength = length;
        traveled = 0f;
        carried = null;
        touched.Clear();
        stallUntil = 0f;
        ShotCount++;

        transform.position = origin;
        // The sprite points up by default; turn it along the trajectory
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        color = shotColor;
        state = State.Extending;
    }

    // Start a shot that has already caught `treasure`: the hook appears on it, holds still for
    // `hold` seconds with the rope out, then pulls it home (the steal tutorial)
    public void PullBack(Vector2 from, Treasure treasure, Color shotColor, float hold)
    {
        if (IsBusy || treasure == null || treasure.IsTaken) return;

        Vector2 at = treasure.transform.position;
        Fire(from, at - from, Vector2.Distance(from, at), shotColor);
        transform.position = at;
        traveled = maxLength;

        Collider2D col = treasure.GetComponent<Collider2D>();
        if (col != null) touched.Add(col);   // don't grab it again once knocked loose
        treasure.GrabByHook(transform);
        carried = treasure;
        HoldInClaw();
        holdUntil = Time.time + hold;
        state = State.Retracting;
    }

    // Keep the hook where it is. Called every frame while something holds it (the squirrel's firm tail
    // touching it): it can't get past for as long as that lasts, and carries on with its shot
    // `afterSeconds` after the last call
    public void Hold(float afterSeconds)
    {
        if (!IsBusy) return;
        stallUntil = Mathf.Max(stallUntil, Time.time + afterSeconds);
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            // When the round ends mid-shot, the hook still comes home but stops reacting
            bool roundOver = GameManager.Instance != null && !GameManager.Instance.IsPlaying;

            if (IsStalled && !roundOver)
            {
                // Held in place: only redraw
            }
            else if (state == State.Extending)
            {
                if (roundOver) state = State.Retracting;
                else Extend();
            }
            else if (state == State.Retracting && (roundOver || Time.time >= holdUntil))
            {
                Retract(!roundOver);
            }
        }

        UpdateRope();
        UpdateClaw();
    }

    void Extend()
    {
        float step = Mathf.Min(extendSpeed * Time.deltaTime, maxLength - traveled);

        foreach (RaycastHit2D hit in CastAhead(dir, step))
        {
            if (!HandleHit(hit.collider)) continue;

            // First valid object on the path: stop here and pull back
            transform.position += (Vector3)(dir * hit.distance);
            if (carried != null) HoldInClaw();
            state = State.Retracting;
            return;
        }

        transform.position += (Vector3)(dir * step);
        traveled += step;
        if (traveled >= maxLength - 0.001f) state = State.Retracting;
    }

    void Retract(bool react)
    {
        Vector2 pos = transform.position;
        float remaining = Vector2.Distance(pos, origin);
        float step = Mathf.Min(retractSpeed * RetractMultiplier() * Time.deltaTime, remaining);

        // An empty hook still reacts to what it touches on the way back. A carrying hook ignores
        // everything except a blocking pig, which knocks the treasure loose
        if (react && remaining > 0.0001f)
        {
            Vector2 back = (origin - pos) / remaining;
            foreach (RaycastHit2D hit in CastAhead(back, step))
            {
                if (carried != null)
                {
                    PlayerCharacter player = hit.collider.GetComponent<PlayerCharacter>();
                    if (player != null && player.IsBlocking)
                    {
                        if (carried != null) PlayAnalytics.Steal();
                        DropCarried();
                        player.OnTreasureKnocked();
                    }
                    continue;
                }
                HandleHit(hit.collider);
                if (carried != null) { HoldInClaw(); break; }
            }
        }

        Vector2 next = Vector2.MoveTowards(pos, origin, step);
        transform.position = next;
        if ((next - origin).sqrMagnitude > 0.0001f) return;

        // Back at the miner: now the treasure counts
        if (carried != null) carried.DeliverToMiner();
        carried = null;
        state = State.Idle;

        // Rest pointing down, ready for the next shot
        dir = Vector2.down;
        transform.rotation = Quaternion.identity;
    }

    // Everything the hook would touch moving this far, nearest first, excluding itself
    List<RaycastHit2D> CastAhead(Vector2 direction, float distance)
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(transform.position, hitRadius, direction, distance);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        List<RaycastHit2D> result = new List<RaycastHit2D>(hits.Length);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            result.Add(hit);
        }
        return result;
    }

    // Returns true if this object is a valid stop (treasure, rock or pig)
    bool HandleHit(Collider2D col)
    {
        // The pig is checked before the touched set, so a hook that passed through it can still be
        // bounced by a block later in the same shot
        PlayerCharacter player = col.GetComponent<PlayerCharacter>();
        if (player != null) return HandlePlayer(player, col);

        if (!touched.Add(col)) return false;

        Treasure treasure = col.GetComponent<Treasure>();
        if (treasure != null)
        {
            if (treasure.IsTaken) return false;
            treasure.GrabByHook(transform);
            carried = treasure;
            return true;
        }

        Rock rock = col.GetComponent<Rock>();
        if (rock != null)
        {
            rock.OnHookBlocked();
            return true;
        }

        return false; // walls, Life items: the hook passes through
    }

    bool HandlePlayer(PlayerCharacter player, Collider2D col)
    {
        // Blocking: the hook bounces off and goes home empty, even in no-kill levels
        if (player.IsBlocking)
        {
            if (!touched.Add(col)) return false;
            player.OnHookBlocked();
            return true;
        }
        // A rolling cat can't be hit: the hook passes through (it can still hit once the roll ends)
        if (player.IsInvulnerable) return false;
        if (!hitsPig) return false;
        if (!touched.Add(col)) return false;

        // No color check: any hook that touches the pig hurts it.
        // GameManager decides the outcome (lose a Life, or end the round).
        if (GameManager.Instance != null) GameManager.Instance.PlayerHit();
        return true;
    }

    // Heavier treasure is pulled back more slowly
    float RetractMultiplier()
    {
        return carried == null ? 1f : RetractMultiplier(carried.type);
    }

    // Also used by MinerAI to estimate how long a shot takes
    public float RetractMultiplier(TreasureType type)
    {
        switch (type)
        {
            case TreasureType.SmallGold:  return smallGoldSpeed;
            case TreasureType.MediumGold: return mediumGoldSpeed;
            case TreasureType.LargeGold:  return largeGoldSpeed;
            case TreasureType.Diamond:    return diamondSpeed;
            default:                      return 1f;
        }
    }

    // Let go of the treasure this hook carries (the frog's tongue takes it): it is free again where it
    // is, and the hook carries on home empty. Returns false if this hook doesn't hold it
    public bool Release(Treasure treasure)
    {
        if (treasure == null || carried != treasure) return false;
        DropCarried();
        return true;
    }

    // Put the treasure in front of the claw so the closed tips grip its near edge
    void DropCarried()
    {
        carried.DropFromHook();
        carried = null;
    }

    void HoldInClaw()
    {
        float radius = 0f;
        SpriteRenderer sr = carried.GetComponent<SpriteRenderer>();
        if (sr != null) radius = Mathf.Min(sr.bounds.extents.x, sr.bounds.extents.y);

        float edge = clawLength * 0.8f;   // a little short of the closed tips
        carried.transform.position = (Vector2)transform.position + dir * (edge + radius);
    }

    void UpdateRope()
    {
        if (rope == null) return;

        Vector2 tip = transform.position;
        bool visible = Application.isPlaying && state != State.Idle && (tip - origin).sqrMagnitude > 0.0001f;

        rope.SetVisible(visible);
        if (!visible) return;

        rope.SetStyle(ropeWidth, ropeWidth, outlineWidth, color, outlineColor);
        rope.SetPosition(0, origin);
        rope.SetPosition(1, tip);
    }

    void UpdateClaw()
    {
        if (clawLeft == null) return;

        // Closed while holding treasure, open otherwise
        float target = carried != null ? clawClosedAngle : clawOpenAngle;
        clawAngle = Application.isPlaying
            ? Mathf.Lerp(clawAngle, target, 1f - Mathf.Exp(-clawCloseSpeed * Time.deltaTime))
            : target;

        DrawArm(clawLeft, 1f);
        DrawArm(clawRight, -1f);
    }

    // One arm: out from the pivot at clawAngle, then bent back toward the center line
    void DrawArm(Stroke arm, float side)
    {
        Vector2 pivot = transform.position;
        Vector2 outward = Rotate(dir, side * clawAngle);
        Vector2 inward = Rotate(dir, -side * clawAngle * 0.8f);

        Vector2 elbow = pivot + outward * (clawLength * 0.6f);
        Vector2 tip = elbow + inward * (clawLength * 0.5f);

        arm.SetStyle(clawWidth, clawWidth * 0.6f, outlineWidth, color, outlineColor);
        arm.SetPosition(0, pivot);
        arm.SetPosition(1, elbow);
        arm.SetPosition(2, tip);
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        return Quaternion.Euler(0f, 0f, degrees) * v;
    }

    // A colored line drawn over a slightly wider dark line, so it reads on any background
    class Stroke
    {
        private readonly LineRenderer fill;
        private readonly LineRenderer outline;

        public Stroke(Transform parent, string name, int points, Material material, int sortingLayer, int sortingOrder)
        {
            outline = CreateLine(parent, name + "Outline", points, material, sortingLayer, sortingOrder);
            fill = CreateLine(parent, name, points, material, sortingLayer, sortingOrder + 1);
        }

        static LineRenderer CreateLine(Transform parent, string name, int points, Material material, int sortingLayer, int sortingOrder)
        {
            // Generated at runtime and in edit mode; never saved into the scene
            GameObject go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(parent, false);

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.sharedMaterial = material;
            line.sortingLayerID = sortingLayer;
            line.sortingOrder = sortingOrder;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            return line;
        }

        public void SetStyle(float startWidth, float endWidth, float border, Color fillColor, Color borderColor)
        {
            fill.startWidth = startWidth;
            fill.endWidth = endWidth;
            fill.startColor = fill.endColor = fillColor;

            outline.startWidth = startWidth + border * 2f;
            outline.endWidth = endWidth + border * 2f;
            outline.startColor = outline.endColor = borderColor;
        }

        public void SetPosition(int index, Vector2 position)
        {
            fill.SetPosition(index, position);
            outline.SetPosition(index, position);
        }

        public void SetVisible(bool visible)
        {
            fill.enabled = visible;
            outline.enabled = visible;
        }

        public void Destroy()
        {
            DestroyLine(fill);
            DestroyLine(outline);
        }

        static void DestroyLine(LineRenderer line)
        {
            if (line == null) return;
            if (Application.isPlaying) Object.Destroy(line.gameObject);
            else Object.DestroyImmediate(line.gameObject);
        }
    }
}
