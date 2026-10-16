using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Owner: B (Player & Rocks)
[RequireComponent(typeof(Rigidbody2D))]
public class PigController : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;

    [Header("Digest (0 = off)")]
    public float digestTime = 0f;       // after eating, the pig can't eat for digestTime + digestPerValue * value
    public float digestPerValue = 0f;
    public Color digestTint = new Color(0.65f, 0.9f, 0.55f);

    [Header("Block (Space)")]
    public bool canBlock = false;       // turned on per level by LevelLoader
    public float blockDuration = 0.8f;  // how long one block lasts
    public float blockCooldown = 1f;    // wait after a block ends before the next one
    public Color blockTint = new Color(0.45f, 0.75f, 1f);

    // Treasure asks this before letting the pig eat it
    public bool CanEat => Time.time >= digestUntil;

    public bool UsesDigest => digestTime > 0f || digestPerValue > 0f;
    public float DigestLeft => Mathf.Max(0f, digestUntil - Time.time);
    public float DigestLength { get; private set; }

    // Hook asks this when it touches the pig: a blocking pig bounces the hook back
    public bool IsBlocking => Time.time < blockUntil;

    // Time left in the current block, and until the next one
    public float BlockLeft => Mathf.Max(0f, blockUntil - Time.time);
    public float BlockReadyIn => Mathf.Max(0f, blockReadyAt - Time.time);

    // Hooks bounced off a block, and treasure knocked off a hook, this round (the tutorial's goals)
    public int HooksBlocked { get; private set; }
    public int TreasureStolen { get; private set; }
    public void OnHookBlocked() { HooksBlocked++; }
    public void OnTreasureKnocked() { TreasureStolen++; }

    private bool facingRight = true; // Default facing is right
    private Rigidbody2D body;
    private SpriteRenderer sr;
    private Color baseColor;
    private Vector2 input;
    private float digestUntil;
    private float blockUntil;
    private float blockReadyAt;

    // Cooldowns are drawn on the pig: a tinted copy of the body, cut by a mask that shrinks from
    // the top (ears) down to the bottom as the cooldown runs out, so the pink comes back top first
    private SpriteRenderer cooldownTint;
    private SpriteMask cooldownMask;
    private Bounds bodyBounds;   // the body sprite, in the pig's local space

    void Awake()
    {
        // Move through physics so solid objects (rocks, walls) block the pig
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true; // Don't spin when bumping into things
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Never sleep: Treasure re-checks every physics step while touched, so a pig standing on
        // gold eats it as soon as its digest ends
        body.sleepMode = RigidbodySleepMode2D.NeverSleep;

        sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            baseColor = sr.color;
            CreateCooldownTint();
        }
    }

    void CreateCooldownTint()
    {
        // Sort the pig's parts among themselves, then make room for the tint just above the body
        // (under the ears, eyes and snout)
        if (GetComponent<SortingGroup>() == null) gameObject.AddComponent<SortingGroup>();
        foreach (SpriteRenderer part in GetComponentsInChildren<SpriteRenderer>(true))
            if (part != sr && part.sortingOrder > sr.sortingOrder) part.sortingOrder++;
        int order = sr.sortingOrder + 1;

        var tint = new GameObject("CooldownTint");
        tint.transform.SetParent(transform, false);
        cooldownTint = tint.AddComponent<SpriteRenderer>();
        cooldownTint.sprite = sr.sprite;
        cooldownTint.sharedMaterial = sr.sharedMaterial;
        cooldownTint.sortingLayerID = sr.sortingLayerID;
        cooldownTint.sortingOrder = order;
        cooldownTint.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        cooldownTint.enabled = false;

        // A 1x1 unit square pivoted at its bottom edge: scaling y grows it upward from the bottom
        Texture2D white = Texture2D.whiteTexture;
        var mask = new GameObject("CooldownMask");
        mask.transform.SetParent(transform, false);
        cooldownMask = mask.AddComponent<SpriteMask>();
        cooldownMask.sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height),
                                            new Vector2(0.5f, 0f), white.width);
        cooldownMask.isCustomRangeActive = true;   // only cut the tint, nothing else
        cooldownMask.frontSortingLayerID = cooldownMask.backSortingLayerID = sr.sortingLayerID;
        cooldownMask.frontSortingOrder = order;
        cooldownMask.backSortingOrder = order - 1;

        bodyBounds = sr.sprite.bounds;
        mask.transform.localPosition = new Vector3(bodyBounds.center.x, bodyBounds.min.y, 0f);
    }

    // Digest shows green, else the block cooldown shows blue; each drains from the top down.
    // While blocking the whole body is blue.
    void UpdateCooldownTint()
    {
        if (cooldownTint == null) return;

        float left = 0f;
        if (!CanEat)
        {
            left = DigestLength > 0f ? DigestLeft / DigestLength : 0f;
            cooldownTint.color = digestTint;
        }
        else if (!IsBlocking && BlockReadyIn > 0f)
        {
            left = blockCooldown > 0f ? BlockReadyIn / blockCooldown : 0f;
            cooldownTint.color = blockTint;
        }

        cooldownTint.enabled = sr.enabled && left > 0f;
        cooldownMask.transform.localScale = new Vector3(bodyBounds.size.x, bodyBounds.size.y * Mathf.Clamp01(left), 1f);
    }

    // Called by Treasure when the pig eats it
    public void Digest(int value)
    {
        DigestLength = digestTime + digestPerValue * value;
        digestUntil = Time.time + DigestLength;
    }

    void Update()
    {
        ReadInput();
        UpdateBlink();
        if (sr != null) sr.color = IsBlocking ? blockTint : baseColor;
        UpdateCooldownTint();
    }

    void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        input = Vector2.zero;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.wKey.isPressed) input.y += 1f;

        if (keyboard.spaceKey.wasPressedThisFrame) TryBlock();

        // Flip only when the horizontal direction changes; keep facing when moving vertically
        if (input.x < 0f && facingRight)
        {
            Flip();
        }
        else if (input.x > 0f && !facingRight)
        {
            Flip();
        }
    }

    // Start a block if the skill is on and off cooldown
    void TryBlock()
    {
        if (!canBlock || Time.time < blockReadyAt) return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

        blockUntil = Time.time + blockDuration;
        blockReadyAt = blockUntil + blockCooldown;
    }

    // Blink while invincible (right after losing a Life)
    void UpdateBlink()
    {
        if (sr == null)
        {
            return;
        }

        bool invincible = GameManager.Instance != null && GameManager.Instance.IsInvincible;
        if (!invincible)
        {
            sr.enabled = true;
            return;
        }

        // Toggle visible/hidden 10 times per second
        int tick = Mathf.FloorToInt(Time.time * 10f);
        sr.enabled = tick % 2 == 0;
    }

    void FixedUpdate()
    {
        // No GameManager in the scene = free movement (handy in Dev scenes)
        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
        if (!playing)
        {
            // Round is over: stand still
            body.linearVelocity = Vector2.zero;
            return;
        }

        // Normalize so diagonals are not faster; physics stops us at colliders
        body.linearVelocity = moveSpeed * input.normalized;
    }

    // Mirror the whole pig (and its children) horizontally
    void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x = -scale.x;
        transform.localScale = scale;
    }
}
