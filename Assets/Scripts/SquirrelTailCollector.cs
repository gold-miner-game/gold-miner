using System.Collections.Generic;
using UnityEngine;

// The squirrel's skill (from Penny's version). The tail is see-through, and firm for as long as
// Space is held (SquirrelController sets it, and halves the speed meanwhile). While firm it:
// - picks up everything it sweeps over that the body would (treasure and Life items), sharing the
//   body's digest: a bite by either starts it, and while digesting neither can eat;
// - holds every hook it touches: the hook can't get past while they touch, and carries on hookStall
//   seconds after they stop touching (the tail moved away or went see-through).
// The tail has no collider: it only looks for pickups and hooks, so hooks never catch it and it
// never pushes anything. Treasure riding a hook has its collider off, so the tail can't steal it.
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public class SquirrelTailCollector : MonoBehaviour
{
    [Header("Tail skill (hold Space)")]
    [Tooltip("How see-through the tail is while not firm: 0 = invisible, 1 = solid")]
    [Range(0f, 1f)] public float idleOpacity = .3f;

    [Header("Hook block")]
    [Tooltip("Seconds a hook still waits after it stops touching the firm tail")]
    [Min(0f)] public float hookStall = 1f;

    // Firm: solid, collecting and stopping hooks
    public bool IsCollecting { get; private set; }

    // After the tutorial the squirrel's record decides (the store can change it); before, hookStall
    public float HookHold => PlayerCharacter.Record is SquirrelStatus s ? s.getHookHold() : hookStall;

    [Header("Collection")]
    [Tooltip("The tail's sprites. Empty: every sprite under SquirrelTailHeading's Tail Pivot")]
    public SpriteRenderer[] tailShapes;
    public LayerMask collectionLayers = ~0;
    [Tooltip("Largest gap between checks as the tail or a hook sweeps")]
    [Min(.01f)] public float sweepSpacing = .1f;

    private readonly List<Collider2D> hits = new List<Collider2D>();
    private readonly HashSet<int> claimedThisFrame = new HashSet<int>();
    private readonly Dictionary<Hook, (Vector2 at, int shot)> hookLast = new Dictionary<Hook, (Vector2, int)>();
    private Hook[] hooks = new Hook[0];
    private Pose[] previous;
    private PlayerCharacter player;
    private Rigidbody2D body;
    private float[] solidAlpha;   // each tail sprite's own opacity, used while firm

    private struct Pose
    {
        public Vector2 center;
        public Vector2 size;
        public float angle;
        public bool valid;
    }

    private void Awake()
    {
        player = GetComponent<PlayerCharacter>();
        body = GetComponent<Rigidbody2D>();
        if (tailShapes == null || tailShapes.Length == 0)
        {
            var heading = GetComponent<SquirrelTailHeading>();
            if (heading != null && heading.tailPivot != null)
                tailShapes = heading.tailPivot.GetComponentsInChildren<SpriteRenderer>();
        }
        solidAlpha = new float[tailShapes.Length];
        for (int i = 0; i < tailShapes.Length; i++)
            solidAlpha[i] = tailShapes[i] != null ? tailShapes[i].color.a : 1f;
        SetSolid(false);
    }

    // After every Awake, so the body's sorting group (PlayerCharacter) is already there
    private void Start()
    {
        // The tail's sprites overlap. See-through, the overlap would be drawn twice and look darker:
        // hide the first sprite wherever the others are, so every part of the tail is drawn once
        if (tailShapes == null || tailShapes.Length < 2 || tailShapes[0] == null) return;
        SpriteRenderer under = tailShapes[0];
        under.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        for (int i = 1; i < tailShapes.Length; i++)
        {
            SpriteRenderer over = tailShapes[i];
            if (over == null || over.sprite == null) continue;
            var mask = new GameObject("TailOverlapMask").AddComponent<SpriteMask>();
            mask.transform.SetParent(over.transform, false);   // the exact shape and pose of that sprite
            mask.sprite = over.sprite;
            mask.isCustomRangeActive = true;                     // only cut the tail, nothing else
            mask.frontSortingLayerID = mask.backSortingLayerID = under.sortingLayerID;
            mask.frontSortingOrder = under.sortingOrder;
            mask.backSortingOrder = under.sortingOrder - 1;
        }
    }

    // Called by SquirrelController every frame: firm while Space is held during a round
    public void SetFirm(bool firm)
    {
        if (firm && !IsCollecting)
        {
            // LevelLoader makes the miners' hooks when the level loads: look them up now
            hooks = FindObjectsByType<Hook>(FindObjectsSortMode.None);
            hookLast.Clear();
        }
        IsCollecting = firm;
    }

    private static Pose ShapePose(SpriteRenderer renderer)
    {
        Vector3 spriteSize = renderer.sprite.bounds.size;
        Vector3 right = renderer.transform.TransformVector(Vector3.right);
        Vector3 up = renderer.transform.TransformVector(Vector3.up);
        return new Pose
        {
            center = renderer.transform.TransformPoint(renderer.sprite.bounds.center),
            // Match the real on-screen footprint of the tilted idle tail
            size = new Vector2(new Vector2(right.x, right.y).magnitude * spriteSize.x,
                new Vector2(up.x, up.y).magnitude * spriteSize.y),
            angle = Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg - 90f,
            valid = true
        };
    }

    private void LateUpdate()
    {
        bool playing = PlayerCharacter.IsPlaying;
        if (!playing) IsCollecting = false;
        SetSolid(IsCollecting);
        // Moving or standing still, the tail collects and blocks only while firm
        if (!IsCollecting || !playing || tailShapes == null || body == null || !body.simulated)
        {
            ResetSweep();
            return;
        }
        if (previous == null || previous.Length != tailShapes.Length) previous = new Pose[tailShapes.Length];
        claimedThisFrame.Clear();
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(collectionLayers);

        var poses = new List<Pose>(tailShapes.Length);
        for (int i = 0; i < tailShapes.Length; i++)
        {
            var shape = tailShapes[i];
            if (shape == null || shape.sprite == null || !shape.gameObject.activeInHierarchy)
            {
                previous[i] = default;
                continue;
            }
            Pose current = ShapePose(shape);
            // A tail turned exactly edge-on has no height on screen: nothing to check
            if (current.size.x < .00001f || current.size.y < .00001f)
            {
                previous[i] = default;
                continue;
            }
            poses.Add(current);
            // Check along the way from last frame's pose, so a fast swing doesn't skip treasure
            Pose last = previous[i].valid ? previous[i] : current;
            float arc = Mathf.Abs(Mathf.DeltaAngle(last.angle, current.angle)) * Mathf.Deg2Rad
                * Mathf.Max(last.size.magnitude, current.size.magnitude) * .5f;
            int samples = Mathf.Clamp(Mathf.CeilToInt((Vector2.Distance(last.center, current.center) + arc)
                / Mathf.Max(.01f, sweepSpacing)), 1, 128);
            for (int sample = 1; sample <= samples; sample++)
            {
                float t = (float)sample / samples;
                hits.Clear();
                Physics2D.OverlapCapsule(Vector2.Lerp(last.center, current.center, t),
                    Vector2.Lerp(last.size, current.size, t), CapsuleDirection2D.Vertical,
                    Mathf.LerpAngle(last.angle, current.angle, t), filter, hits);
                for (int hit = 0; hit < hits.Count; hit++) TryCollect(hits[hit]);
            }
            previous[i] = current;
        }
        BlockHooks(poses);
    }

    private void TryCollect(Collider2D other)
    {
        if (other == null || player == null || other.transform.IsChildOf(transform)) return;
        if (!claimedThisFrame.Add(other.gameObject.GetInstanceID())) return;

        // The same as the body touching them, with the same digest
        Treasure treasure = other.GetComponent<Treasure>();
        if (treasure != null)
        {
            PlayAnalytics.DigestContact(treasure, player);
            treasure.TryEat(player);
        }

        LifePickup life = other.GetComponent<LifePickup>();
        if (life != null) life.TryPickUp(player);
    }

    // Stop each moving hook whose claw touches the tail. The claw's path since last frame is
    // checked too, so a fast hook can't jump over the thin tail
    private void BlockHooks(List<Pose> poses)
    {
        foreach (Hook hook in hooks)
        {
            if (hook == null || !hook.isActiveAndEnabled) continue;
            Vector2 now = hook.transform.position;
            // Only the path within one shot: a new shot jumps the hook to its start
            Vector2 from = hookLast.TryGetValue(hook, out var seen) && seen.shot == hook.ShotCount ? seen.at : now;
            hookLast[hook] = (now, hook.ShotCount);
            if (!hook.IsBusy) continue;

            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from, now) / Mathf.Max(.01f, sweepSpacing)), 1, 64);
            for (int s = 1; s <= samples; s++)
            {
                Vector2 point = Vector2.Lerp(from, now, (float)s / samples);
                if (!Touches(poses, point, hook.hitRadius)) continue;
                hook.Hold(HookHold);   // every frame they touch
                break;
            }
        }
    }

    // Whether a circle (the claw) overlaps any of the tail's shapes on screen
    private static bool Touches(List<Pose> poses, Vector2 point, float radius)
    {
        foreach (Pose pose in poses)
        {
            Vector2 local = Quaternion.Euler(0f, 0f, -pose.angle) * (point - pose.center);
            if (Mathf.Abs(local.x) <= pose.size.x * .5f + radius && Mathf.Abs(local.y) <= pose.size.y * .5f + radius)
                return true;
        }
        return false;
    }

    // Solid (the sprites' own opacity) while firm, see-through otherwise
    private void SetSolid(bool solid)
    {
        if (tailShapes == null || solidAlpha == null) return;
        for (int i = 0; i < tailShapes.Length; i++)
        {
            if (tailShapes[i] == null) continue;
            Color c = tailShapes[i].color;
            c.a = solid ? solidAlpha[i] : solidAlpha[i] * idleOpacity;
            tailShapes[i].color = c;
        }
    }

    private void ResetSweep()
    {
        previous = null;
        hits.Clear();
        claimedThisFrame.Clear();
        hookLast.Clear();
    }

    private void OnDisable()
    {
        IsCollecting = false;
        SetSolid(false);
        ResetSweep();
    }
}
