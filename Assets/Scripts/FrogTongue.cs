using UnityEngine;

// The frog's skill (from Penny's FrogTongue). Space (via FrogController) shoots the tongue straight out
// of the mouth, the way the frog faces, and opens the mouth meanwhile. The frog keeps moving and turning;
// the tongue comes out of the mouth wherever it is and swings with it. The tongue is as wide as the mouth
// and reaches maximumLengthMultiplier times the frog's width. The first treasure any part of it touches
// is grabbed (even one a miner's hook is pulling home). If the middle grabbed it, the part beyond pulls
// in to it at snapSpeedMultiplier times the shooting speed; then the tongue comes back at retractSpeed,
// and the treasure is eaten when it arrives: money and digest, like the body eating it. The tongue can shoot while digesting, but the frog can't
// eat then: the catch is dropped at the mouth, and the body eats it once the digest ends.
// No cooldown: Space works again as soon as the tongue is back.
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public class FrogTongue : MonoBehaviour
{
    public enum Phase { Ready, Extending, Snapping, Retracting }

    [Header("References")]
    public SpriteRenderer mouth;
    public SpriteRenderer mouthOutline;
    [Tooltip("Its width sets how far the tongue reaches")]
    public SpriteRenderer frogBodyArtwork;
    public SpriteRenderer tongue;
    [Tooltip("Moves with the end of the tongue; a grabbed treasure rides on it")]
    public Transform tongueTip;

    [Header("Skill (Space)")]
    [Tooltip("Reach, times the frog's width")]
    [Min(.01f)] public float maximumLengthMultiplier = 6f;
    [Min(.01f)] public float extensionSpeed = 8f;
    [Min(.01f)] public float retractSpeed = 6f;
    [Tooltip("When the middle of the tongue grabs a treasure, the part beyond pulls in this many times faster than it shoots out")]
    [Min(1f)] public float snapSpeedMultiplier = 3f;
    [Tooltip("How much the mouth opens while the tongue is out")]
    [Min(1f)] public float mouthOpenMultiplier = 1.4f;

    // After the tutorial the frog's record decides (the store can change them); before, these Inspector values
    public float Reach => PlayerCharacter.Record is FrogStatus s ? s.getTongueReach() : maximumLengthMultiplier;
    public float ShootSpeed => PlayerCharacter.Record is FrogStatus s ? s.getTongueSpeed() : extensionSpeed;
    public float ReturnSpeed => PlayerCharacter.Record is FrogStatus s ? s.getTongueReturnSpeed() : retractSpeed;
    public float SnapMultiplier => PlayerCharacter.Record is FrogStatus s ? s.getTongueSnapMultiplier() : snapSpeedMultiplier;

    public Phase State { get; private set; }
    public bool IsUsingSkill => State != Phase.Ready;
    public bool CanFire => isActiveAndEnabled && State == Phase.Ready && PlayerCharacter.IsPlaying;
    public float ShotWidth { get; private set; }
    public float ShotMaximumLength { get; private set; }

    private PlayerCharacter player;
    private bool mouthOpened;
    private Vector2 origin, direction, lastDirection;
    private float length;
    private float grabDistance;   // how far along the tongue the carried treasure is
    private Treasure carried;
    private readonly System.Collections.Generic.List<Treasure> candidates = new System.Collections.Generic.List<Treasure>();
    private Vector3 mouthScale, outlineScale, mouthPosition, outlinePosition;

    void Awake()
    {
        player = GetComponent<PlayerCharacter>();
        if (tongue != null) tongue.enabled = false;
    }

    // A fresh record (just picked) starts from this prefab's numbers
    void Start()
    {
        if (PlayerCharacter.Record is FrogStatus s)
            s.seed(maximumLengthMultiplier, extensionSpeed, retractSpeed, snapSpeedMultiplier);
    }

    // Called by FrogController when Space is pressed during a round. Returns true if the tongue shot
    public bool Fire()
    {
        if (!CanFire || mouth == null || mouth.sprite == null || frogBodyArtwork == null
            || frogBodyArtwork.sprite == null || tongue == null || tongue.sprite == null || tongueTip == null)
            return false;

        // The normal mouth's width (measured before it opens). The direction is the face as shown,
        // even partway through a turn
        ShotWidth = WorldSize(mouth, Vector3.right, mouth.sprite.bounds.size.x);
        float diameter = Mathf.Max(WorldSize(frogBodyArtwork, Vector3.right, frogBodyArtwork.sprite.bounds.size.x),
            WorldSize(frogBodyArtwork, Vector3.up, frogBodyArtwork.sprite.bounds.size.y));
        ShotMaximumLength = Mathf.Max(0f, diameter) * Mathf.Max(0f, Reach);
        if (ShotWidth <= .00001f || ShotMaximumLength <= .00001f) return false;
        direction = ((Vector2)mouth.transform.TransformVector(Vector3.up)).normalized;
        if (direction.sqrMagnitude < .000001f) return false;

        OpenMouth();
        origin = mouth.transform.TransformPoint(mouth.sprite.bounds.center);
        tongue.color = mouth.color;
        lastDirection = direction;
        length = 0f;
        carried = null;
        State = Phase.Extending;
        UpdateTongueVisual();
        return true;
    }

    void FixedUpdate()
    {
        if (!IsUsingSkill) return;
        if (!PlayerCharacter.IsPlaying) { CancelSkill(); return; }
        FollowMouth();

        float shoot = Mathf.Max(.01f, ShootSpeed);
        if (State == Phase.Extending)
        {
            length = Mathf.Min(ShotMaximumLength, length + shoot * Time.fixedDeltaTime);
            TryGrab();
            if (State == Phase.Extending && length >= ShotMaximumLength) State = Phase.Retracting;
        }
        else if (State == Phase.Snapping)
        {
            // The part beyond the treasure pulls in fast, then the tongue comes back as usual
            length = Mathf.MoveTowards(length, grabDistance, shoot * Mathf.Max(1f, SnapMultiplier) * Time.fixedDeltaTime);
            if (length <= grabDistance + .0001f) { length = grabDistance; State = Phase.Retracting; }
        }
        else
        {
            if (carried == null) TryGrab();   // an empty tongue still grabs what it swings into
            if (State == Phase.Retracting)
            {
                length = Mathf.Max(0f, length - Mathf.Max(.01f, ReturnSpeed) * Time.fixedDeltaTime);
                grabDistance = Mathf.Min(grabDistance, length);
                if (length <= 0f) { CompleteReturn(); return; }
            }
        }
        lastDirection = direction;
        UpdateTongueVisual();
    }

    // Grab the nearest treasure any part of the tongue touches. The tongue may have swung since the
    // last step, so the directions in between are checked too and a fast swing can't skip a treasure
    void TryGrab()
    {
        candidates.Clear();
        candidates.AddRange(Treasure.Active);
        candidates.AddRange(Treasure.OnHooks);   // treasure riding a miner's hook
        float swing = Vector2.Angle(lastDirection, direction) * Mathf.Deg2Rad * length;
        int samples = Mathf.Clamp(Mathf.CeilToInt(swing / Mathf.Max(.05f, ShotWidth)), 1, 32);
        for (int s = 1; s <= samples; s++)
        {
            Vector2 dir = Vector3.Slerp(lastDirection, direction, (float)s / samples);
            if (!FindHit(dir, out Treasure hit, out float hitDistance) || !TryTake(hit)) continue;
            carried = hit;
            grabDistance = hitDistance;
            direction = dir;   // where it was caught; the tongue follows the mouth again next step
            if (hitDistance < length - .0001f) State = Phase.Snapping;   // caught by the middle
            else { length = hitDistance; State = Phase.Retracting; }
            return;
        }
    }

    // The nearest treasure the tongue overlaps when pointing dir (drawn box, so treasure on a hook counts too)
    bool FindHit(Vector2 dir, out Treasure nearest, out float nearestDistance)
    {
        nearest = null;
        nearestDistance = float.PositiveInfinity;
        foreach (Treasure treasure in candidates)
        {
            if (treasure == null || !treasure.isActiveAndEnabled || treasure.transform.IsChildOf(transform)) continue;
            var artwork = treasure.GetComponent<SpriteRenderer>();
            if (artwork == null || !artwork.enabled) continue;
            Bounds bounds = artwork.bounds;
            if (!TryHit(origin, dir, ShotWidth * .5f, length, bounds.center, bounds.extents, out float distance)
                || distance >= nearestDistance) continue;
            nearest = treasure;
            nearestDistance = distance;
        }
        return nearest != null;
    }

    // Take a loose treasure, or one off a miner's hook (that hook goes home empty)
    bool TryTake(Treasure treasure)
    {
        if (treasure == null || tongueTip == null) return false;
        if (treasure.IsTaken)
        {
            bool released = false;
            foreach (Hook hook in FindObjectsByType<Hook>(FindObjectsSortMode.None))
                if (hook.Release(treasure)) { released = true; break; }
            if (!released) return false;   // held by something else: leave it
        }
        treasure.GrabByHook(tongueTip);
        return true;
    }

    void LateUpdate()
    {
        if (!IsUsingSkill) return;
        FollowMouth();
        UpdateTongueVisual();
    }

    // The frog may be moving and turning: the tongue always starts at the (open) mouth and points its way
    void FollowMouth()
    {
        if (mouth == null || mouth.sprite == null) return;
        origin = mouth.transform.TransformPoint(mouth.sprite.bounds.center);
        Vector2 facing = mouth.transform.TransformVector(Vector3.up);
        if (facing.sqrMagnitude > .000001f) direction = facing.normalized;
    }

    void UpdateTongueVisual()
    {
        if (tongueTip != null)
        {
            Vector3 tipPosition = origin + direction * length;
            tipPosition.z = mouth.transform.position.z;
            tongueTip.position = tipPosition;
        }
        // The treasure stays where the tongue caught it until the part beyond has pulled in
        if (carried != null)
        {
            Vector3 held = origin + direction * Mathf.Min(length, grabDistance);
            held.z = carried.transform.position.z;
            carried.transform.position = held;
        }
        if (tongue == null) return;
        tongue.enabled = IsUsingSkill && length > .00001f;
        if (!tongue.enabled) return;
        Transform visual = tongue.transform;
        visual.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        Vector3 parentRight = visual.parent != null
            ? visual.parent.TransformVector(visual.localRotation * Vector3.right) : Vector3.right;
        Vector3 parentUp = visual.parent != null
            ? visual.parent.TransformVector(visual.localRotation * Vector3.up) : Vector3.up;
        Vector3 size = tongue.sprite.bounds.size;
        visual.localScale = new Vector3(ShotWidth / Mathf.Max(.00001f, size.x * parentRight.magnitude),
            length / Mathf.Max(.00001f, size.y * parentUp.magnitude), 1f);
        Vector3 center = origin + direction * (length * .5f);
        center.z = mouth.transform.position.z;
        visual.position = center - visual.TransformVector(tongue.sprite.bounds.center);
    }

    void OpenMouth()
    {
        mouthScale = mouth.transform.localScale;
        mouthPosition = mouth.transform.localPosition;
        if (mouthOutline != null)
        {
            outlineScale = mouthOutline.transform.localScale;
            outlinePosition = mouthOutline.transform.localPosition;
        }
        SpriteRenderer edge = mouthOutline != null && mouthOutline.sprite != null ? mouthOutline : mouth;
        float multiplier = Mathf.Max(1f, mouthOpenMultiplier);
        float height = WorldSize(edge, Vector3.up, edge.sprite.bounds.size.y);
        // Grow inward, so the mouth still touches the round edge rather than crossing it
        Vector3 shift = -edge.transform.TransformVector(Vector3.up).normalized * height * .5f * (multiplier - 1f);
        mouth.transform.localScale = new Vector3(mouthScale.x * multiplier, mouthScale.y * multiplier, mouthScale.z);
        mouth.transform.position += shift;
        if (mouthOutline != null)
        {
            mouthOutline.transform.localScale = new Vector3(outlineScale.x * multiplier, outlineScale.y * multiplier, outlineScale.z);
            mouthOutline.transform.position += shift;
        }
        mouthOpened = true;
    }

    void RestoreMouth()
    {
        if (!mouthOpened) return;
        if (mouth != null) { mouth.transform.localScale = mouthScale; mouth.transform.localPosition = mouthPosition; }
        if (mouthOutline != null) { mouthOutline.transform.localScale = outlineScale; mouthOutline.transform.localPosition = outlinePosition; }
        mouthOpened = false;
    }

    // The tongue is back: eat what it brought, or drop it at the mouth while still digesting
    void CompleteReturn()
    {
        Treasure returned = carried;
        carried = null;
        if (returned != null)
        {
            if (player == null || player.CanEat) returned.DeliverToPlayer(player);
            else returned.DropFromHook();   // free where it is; touching the body, it's eaten when the digest ends
        }
        ResetSkill();
    }

    // Round over (or the frog switched off) with the tongue out: drop the treasure where it is
    void CancelSkill()
    {
        if (carried != null) carried.DropFromHook();
        carried = null;
        ResetSkill();
    }

    void ResetSkill()
    {
        State = Phase.Ready;
        length = 0f;
        grabDistance = 0f;
        candidates.Clear();
        if (tongue != null) tongue.enabled = false;
        RestoreMouth();
        if (tongueTip != null && mouth != null) tongueTip.position = mouth.transform.position;
    }

    // Penny's exact test of a straight tongue (a rectangle growing from origin) against a treasure's
    // box. Gives the shortest tongue length that touches it
    static bool TryHit(Vector2 origin, Vector2 dir, float halfWidth, float length, Vector2 center, Vector2 extents,
        out float distance)
    {
        distance = 0f;
        if (length < 0f || halfWidth <= 0f || dir.sqrMagnitude < .000001f) return false;
        dir.Normalize();
        float x = center.x - origin.x, y = center.y - origin.y;
        float ex = Mathf.Abs(extents.x), ey = Mathf.Abs(extents.y);
        float across = -dir.y * x + dir.x * y;
        float acrossExtent = Mathf.Abs(dir.y) * ex + Mathf.Abs(dir.x) * ey;
        if (Mathf.Abs(across) > acrossExtent + halfWidth) return false;
        float along = dir.x * x + dir.y * y;
        float alongExtent = Mathf.Abs(dir.x) * ex + Mathf.Abs(dir.y) * ey;
        if (along + alongExtent < 0f) return false;
        float near = Mathf.Max(0f, along - alongExtent), far = length;
        float expandedX = ex + Mathf.Abs(dir.y) * halfWidth;
        float expandedY = ey + Mathf.Abs(dir.x) * halfWidth;
        if (!Clip(dir.x, x - expandedX, x + expandedX, ref near, ref far)
            || !Clip(dir.y, y - expandedY, y + expandedY, ref near, ref far)) return false;
        distance = near;
        return near <= far && near <= length;
    }

    static bool Clip(float d, float minimum, float maximum, ref float near, ref float far)
    {
        if (Mathf.Abs(d) < .000001f) return minimum <= 0f && maximum >= 0f;
        float a = minimum / d, b = maximum / d;
        if (a > b) { float swap = a; a = b; b = swap; }
        near = Mathf.Max(near, a); far = Mathf.Min(far, b);
        return near <= far;
    }

    static float WorldSize(SpriteRenderer artwork, Vector3 axis, float nativeSize)
        => artwork.transform.TransformVector(axis).magnitude * nativeSize;

    void OnDisable() { CancelSkill(); }
}
