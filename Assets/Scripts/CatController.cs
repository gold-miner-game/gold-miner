using System.Collections.Generic;
using UnityEngine;

// The cat (JK. Rolling). Moves, eats and blinks like every character (PlayerCharacter), at the
// level's speed. Its skill is the roll (from Penny's CatRoll): Space dashes at speedMultiplier
// times the speed for dashDuration, while the artwork spins. WASD can steer the dash.
// Hooks pass through it while it rolls, without hurting it or being stopped. While rolling it eats every
// treasure it touches with no digest in between; when the roll ends, one digest starts for everything it
// ate. If it was already digesting when the roll started, it can't eat during the roll.
// During the cooldown after a roll the cat looks tired (Penny's Player-Tired-08 eyes: thin
// slits), and Space works again when its eyes open.
public class CatController : PlayerCharacter
{
    [Header("Roll (Space)")]
    [Min(0f)] public float speedMultiplier = 3f;
    [Tooltip("How long the dash and the spin last")]
    [Min(.01f)] public float dashDuration = .5f;
    [Tooltip("Wait after a roll ends before the next one (the cat looks tired)")]
    [Min(0f)] public float cooldown = 1f;

    [Header("Tired look (roll cooldown)")]
    [Tooltip("Shown normally; hidden while tired")]
    public GameObject[] normalEyes;
    [Tooltip("Shown only while tired (switched off in the prefab)")]
    public GameObject[] tiredEyes;
    [Tooltip("Only the artwork spins; physics and the collider stay upright. Made on Play if empty.")]
    public Transform visualRoot;
    [Tooltip("Full turns during one dash")]
    [Min(1)] public int rotationsPerRoll = 2;
    [Tooltip("Spin is counterclockwise; clockwise when rolling left (including left diagonals)")]
    public bool reverseSpinWhenRollingLeft = true;

    public bool IsRolling => Time.time < rollUntil;
    // Hooks pass through a rolling cat without hurting it
    public override bool IsInvulnerable => IsRolling;
    public float CooldownRemaining => Mathf.Max(0f, nextRollTime - Time.time);
    // After a roll, until Space works again
    public bool IsTired => !IsRolling && CooldownRemaining > 0f;
    public override string SkillHint => "[Space] roll";

    private Vector2 lastDirection = Vector2.right;
    private float rollUntil = float.NegativeInfinity;
    private float nextRollTime = float.NegativeInfinity;
    private Quaternion restVisualRotation;
    private float spinStart;
    private bool spinning;
    private float spinDirection = 1f;
    private bool lookingTired;
    private bool wasRolling;
    private int rollEaten;   // value eaten during this roll, digested when it ends

    protected override void Awake()
    {
        // Group the artwork first: moving the body after the base has added the sorting group and the
        // digest tint/mask leaves Unity's sorting stale, and the green tint never shows
        EnsureVisualRoot();
        if (visualRoot != null) restVisualRotation = visualRoot.localRotation;
        base.Awake();
        lastDirection = transform.localScale.x < 0f ? Vector2.left : Vector2.right;
    }

    // Put every sprite child under one pivot at the middle of the artwork, so it spins in place
    void EnsureVisualRoot()
    {
        if (visualRoot != null) return;
        var children = new List<Transform>();
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Transform child in transform)
        {
            // Hidden parts too (the tired eyes), so they turn with the rest
            var renderers = child.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length == 0) continue;
            children.Add(child);
            foreach (var renderer in renderers)
            {
                if (renderer.sprite == null) continue;
                Bounds part = LocalBounds(transform, renderer);
                if (!hasBounds) { bounds = part; hasBounds = true; }
                else bounds.Encapsulate(part);
            }
        }
        if (children.Count == 0) return;
        visualRoot = new GameObject("Cat Roll Visuals").transform;
        visualRoot.SetParent(transform, false);
        visualRoot.localPosition = bounds.center;
        foreach (Transform child in children) child.SetParent(visualRoot, true);
    }

    protected override void Update()
    {
        base.Update();
        if (MoveInput.sqrMagnitude > 0f) lastDirection = MoveInput.normalized;
        if (wasRolling && !IsRolling) EndRollDigest();
        wasRolling = IsRolling;
        SetTired(IsTired);
    }

    // While rolling, eating doesn't start a digest: it's saved up for the end of the roll. (A digest that
    // was already running when the roll started still stops all eating, as CanEat stays false.)
    public override void Digest(int value)
    {
        if (IsRolling) rollEaten += value;
        else base.Digest(value);
    }

    // The roll is over: one digest for everything eaten during it
    void EndRollDigest()
    {
        if (rollEaten > 0) StartDigest(rollEaten);
        rollEaten = 0;
    }

    // Swap the normal eyes for the tired ones
    void SetTired(bool tired)
    {
        if (tired == lookingTired) return;
        lookingTired = tired;
        if (normalEyes != null) foreach (GameObject eye in normalEyes) if (eye != null) eye.SetActive(!tired);
        if (tiredEyes != null) foreach (GameObject eye in tiredEyes) if (eye != null) eye.SetActive(tired);
    }

    protected override void UseSkill()
    {
        if (Time.time < nextRollTime) return;
        if (MoveInput.sqrMagnitude > 0f) lastDirection = MoveInput.normalized;

        rollUntil = Time.time + dashDuration;
        nextRollTime = rollUntil + cooldown;
        spinDirection = reverseSpinWhenRollingLeft && lastDirection.x < -.01f ? -1f : 1f;
        spinStart = Time.time;
        spinning = true;
    }

    // While rolling, keep dashing along the last direction held (no keys: keep going straight)
    protected override Vector2 Velocity(Vector2 input)
    {
        if (!IsRolling) return base.Velocity(input);
        return lastDirection * Mathf.Max(0f, moveSpeed) * Mathf.Max(0f, speedMultiplier);
    }

    protected override void OnStopped()
    {
        rollUntil = float.NegativeInfinity;
        FinishSpin();
        EndRollDigest();
        wasRolling = false;
    }

    void LateUpdate()
    {
        if (!spinning || visualRoot == null) return;
        float t = Mathf.Clamp01((Time.time - spinStart) / Mathf.Max(.001f, dashDuration));
        // Make up for a mirrored cat so the spin goes the right way on screen
        Vector3 parentScale = visualRoot.parent != null ? visualRoot.parent.lossyScale : Vector3.one;
        float mirrorSign = parentScale.x * parentScale.y < 0f ? -1f : 1f;
        visualRoot.localRotation = restVisualRotation * Quaternion.Euler(0f, 0f,
            360f * Mathf.Max(1, rotationsPerRoll) * t * spinDirection * mirrorSign);
        if (t >= 1f) FinishSpin();
    }

    void FinishSpin()
    {
        if (visualRoot != null) visualRoot.localRotation = restVisualRotation;
        spinning = false;
    }

    void OnDisable()
    {
        rollUntil = float.NegativeInfinity;
        FinishSpin();
        SetTired(false);
    }
}
