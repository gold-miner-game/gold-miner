using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Shared by every character the player can be: Mr. Hyde, the cat (JK. Rolling), the squirrel (Scrat) and
// the frog (Rero Rero). Hook, Treasure, LifePickup and LevelLoader only talk to this class, so any character
// works in any level. It moves with WASD, eats treasure (with the level's digest time), blinks while
// invincible and calls UseSkill when Space is pressed. Each character adds its own skill.
// After the tutorial, the cross-level record (CrossLevelStatus, Zhan Li's) is the player: its speed
// multiplier (swift water), no-digest (ration) and shield work for every character, and each champion's
// own record (PigStatus, CatStatus, SquirrelStatus, FrogStatus) holds its skill numbers for the store.
[RequireComponent(typeof(Rigidbody2D))]
public abstract class PlayerCharacter : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;        // set per level by LevelLoader: pig_speed x game speed x speedScale
    [Tooltip("This character's speed relative to the level's speed (1 = Mr. Hyde's). Skills that change " +
             "the speed (the cat's roll, the squirrel's firm tail) multiply the result")]
    [Min(0f)] public float speedScale = 1f;

    [Header("Digest (0 = off)")]
    public float digestTime = 0f;       // after eating, it can't eat for digestTime + digestPerValue * value
    public float digestPerValue = 0f;
    public Color digestTint = new Color(0.65f, 0.9f, 0.55f);
    [Tooltip("A \"Digesting\" label floats this high (world units) above the character while it can't eat.")]
    public float digestLabelHeight = 0.95f;
    public Color digestLabelColor = new Color(0.25f, 0.8f, 0.3f);

    [Header("Look")]
    [Tooltip("The sprite the cooldown colors are drawn on. Empty: this object's own SpriteRenderer")]
    public SpriteRenderer body;

    // Treasure asks this before letting the character eat it
    public bool CanEat => Time.time >= digestUntil;

    public bool UsesDigest => digestTime > 0f || digestPerValue > 0f;
    public float DigestLeft => Mathf.Max(0f, digestUntil - Time.time);
    public float DigestLength { get; private set; }
    public bool IsDigesting => !CanEat && DigestLength > 0f;
    public float DigestProgress01 => IsDigesting ? 1f - DigestLeft / DigestLength : 0f;

    // Hook asks this when it touches the character: a blocking one bounces the hook back.
    // Only Mr. Hyde can block
    // A timed block (Mr. Hyde's Space) or the brace item, which blocks for the whole level
    public virtual bool IsBlocking => Record != null && Record.isBracing();

    // Hook asks this too: while true, hooks pass through without hurting it (the cat's roll)
    public virtual bool IsInvulnerable => false;

    // Shown in the level label, e.g. "[Space] block". Empty: no skill on this level
    public virtual string SkillHint => "";

    // For the HUD's skill row: the skill is running right now (a block, a roll, the tongue out),
    // seconds until Space works again (0 = ready), and the whole wait after a use (0 = no cooldown)
    public virtual bool SkillActive => false;
    public virtual float SkillReadyIn => 0f;
    public virtual float SkillWait => 0f;

    // Hooks bounced off a block, and treasure knocked off a hook, this round (the tutorial's goals)
    public int HooksBlocked { get; private set; }
    public int TreasureStolen { get; private set; }
    public void OnHookBlocked() { HooksBlocked++; }
    public void OnTreasureKnocked() { TreasureStolen++; }

    // WASD this frame (not normalized)
    public Vector2 MoveInput { get; private set; }

    // No GameManager in the scene = always playing (handy in Dev scenes)
    // Not while the HUD's pause popup has time stopped, so Space can't queue a skill behind it
    public static bool IsPlaying => (GameManager.Instance == null || GameManager.Instance.IsPlaying) && Time.timeScale > 0f;

    // The cross-level record, or null in the tutorial (which never touches it) or a Dev scene without one
    public static CrossLevelStatus Record => LevelLoader.Tutorial ? null : CrossLevelStatus.Active;

    // Speed right now: moveSpeed times the record's multiplier (swift water doubles it for a level)
    public float CurrentSpeed => Mathf.Max(0f, moveSpeed) * (Record != null ? Record.getSpeedMultiplier() : 1f);

    protected Rigidbody2D Body { get; private set; }
    protected Color BaseColor { get; private set; }

    // Mirror the whole character when it turns left or right
    protected virtual bool FlipsWhenTurning => true;

    private bool facingRight = true; // Default facing is right
    private float digestUntil;
    private SpriteRenderer[] parts;  // every sprite of the character, for blinking
    private bool[] partShown;        // which of them are switched on in the prefab

    // Cooldowns are drawn on the body: a tinted copy of it, cut by a mask that shrinks from the top
    // down to the bottom as the cooldown runs out, so the normal color comes back top first
    private SpriteRenderer cooldownTint;
    private SpriteMask cooldownMask;
    private SpriteRenderer shieldBubble;
    private Bounds bodyBounds;   // the body sprite, in the character's local space
    private GUIStyle digestLabelStyle;

    protected virtual void Awake()
    {
        // Move through physics so solid objects (rocks, walls) block the character
        Body = GetComponent<Rigidbody2D>();
        Body.bodyType = RigidbodyType2D.Dynamic;
        Body.gravityScale = 0f;
        Body.freezeRotation = true; // Don't spin when bumping into things
        Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        Body.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Never sleep: Treasure re-checks every physics step while touched, so a character standing
        // on gold eats it as soon as its digest ends
        Body.sleepMode = RigidbodySleepMode2D.NeverSleep;

        if (body == null) body = GetComponent<SpriteRenderer>();
        if (body != null)
        {
            BaseColor = body.color;
            CreateCooldownTint();
            CreateShieldBubble();
        }

        parts = GetComponentsInChildren<SpriteRenderer>(true);
        partShown = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++) partShown[i] = parts[i].enabled;
    }

    void CreateCooldownTint()
    {
        // Sort the parts among themselves, then make room for the tint just above the body
        // (under the ears, eyes and snout)
        if (GetComponent<SortingGroup>() == null) gameObject.AddComponent<SortingGroup>();
        foreach (SpriteRenderer part in GetComponentsInChildren<SpriteRenderer>(true))
            if (part != body && part.sortingOrder > body.sortingOrder) part.sortingOrder++;
        // The character's own masks (the frog's legs) move with the parts they cut
        foreach (SpriteMask own in GetComponentsInChildren<SpriteMask>(true))
        {
            if (!own.isCustomRangeActive) continue;
            if (own.frontSortingOrder > body.sortingOrder) own.frontSortingOrder++;
            if (own.backSortingOrder > body.sortingOrder) own.backSortingOrder++;
        }
        int order = body.sortingOrder + 1;

        // The tint sits on the body itself, so it has the body's exact shape, turn and size
        var tint = new GameObject("CooldownTint");
        tint.transform.SetParent(body.transform, false);
        cooldownTint = tint.AddComponent<SpriteRenderer>();
        cooldownTint.sprite = body.sprite;
        cooldownTint.sharedMaterial = body.sharedMaterial;
        cooldownTint.sortingLayerID = body.sortingLayerID;
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
        cooldownMask.frontSortingLayerID = cooldownMask.backSortingLayerID = body.sortingLayerID;
        cooldownMask.frontSortingOrder = order;
        cooldownMask.backSortingOrder = order - 1;

        // The body's box in the character's own space (the body can be a turned or scaled child)
        bodyBounds = LocalBounds(transform, body);
        mask.transform.localPosition = new Vector3(bodyBounds.center.x, bodyBounds.min.y, 0f);
    }

    // A larger, see-through copy of the body, drawn over everything while the record's shield is up
    void CreateShieldBubble()
    {
        int top = body.sortingOrder;
        foreach (SpriteRenderer part in GetComponentsInChildren<SpriteRenderer>(true))
            top = Mathf.Max(top, part.sortingOrder);
        var bubble = new GameObject("ShieldBubble");
        bubble.transform.SetParent(body.transform, false);
        bubble.transform.localScale = Vector3.one * 1.45f;
        shieldBubble = bubble.AddComponent<SpriteRenderer>();
        shieldBubble.sprite = body.sprite;
        shieldBubble.sharedMaterial = body.sharedMaterial;
        shieldBubble.sortingLayerID = body.sortingLayerID;
        shieldBubble.sortingOrder = top + 1;
        shieldBubble.color = new Color(0.45f, 0.85f, 1f, 0.4f);
        shieldBubble.enabled = false;
    }

    // A sprite's box in another transform's local space, from the sprite and the transforms only.
    // Renderer.bounds can still be stale in Awake right after Instantiate (a spawned character)
    public static Bounds LocalBounds(Transform space, SpriteRenderer renderer)
    {
        Bounds sprite = renderer.sprite.bounds;
        Bounds box = new Bounds();
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3(i % 2 == 0 ? sprite.min.x : sprite.max.x,
                                         i < 2 ? sprite.min.y : sprite.max.y, 0f);
            Vector3 local = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
            local.z = 0f;
            if (i == 0) box = new Bounds(local, Vector3.zero);
            else box.Encapsulate(local);
        }
        return box;
    }

    // Digest shows green, else the character's skill cooldown (if it shows one); each drains from
    // the top down
    void UpdateCooldownTint()
    {
        if (cooldownTint == null) return;

        float left = 0f;
        if (!CanEat)
        {
            left = DigestLength > 0f ? DigestLeft / DigestLength : 0f;
            cooldownTint.color = digestTint;
        }
        else if (SkillCooldown(out float skillLeft, out Color skillTint))
        {
            left = skillLeft;
            cooldownTint.color = skillTint;
        }

        cooldownTint.enabled = body.enabled && left > 0f;
        cooldownMask.transform.localScale = new Vector3(bodyBounds.size.x, bodyBounds.size.y * Mathf.Clamp01(left), 1f);
    }

    // Maze BlockQTE watches digest progress for a named treasure (e.g. D2).
    public string LastEatenId { get; private set; }

    // Called by Treasure when the character eats it. A character can put it off (the cat while rolling)
    public virtual void Digest(int value)
    {
        StartDigest(value);
    }

    public void DigestTreasure(Treasure treasure, int value)
    {
        LastEatenId = treasure != null ? treasure.name : "";
        StartDigest(value);
        if (MazeRuntime.Active != null) MazeRuntime.Active.NotifyEatStart(treasure);
    }

    // Maze BlockQTE miss: stop the digest so the pig can try the diamond again.
    public void CancelDigest()
    {
        DigestLength = 0f;
        digestUntil = Time.time;
        LastEatenId = "";
    }

    // Start digesting `value` worth of treasure now. The record's ration skill means no digest at all
    protected void StartDigest(int value)
    {
        if (Record != null && Record.hasNoDigest())
        {
            DigestLength = 0f;
            digestUntil = Time.time;
            return;
        }
        DigestLength = digestTime + digestPerValue * value;
        digestUntil = Time.time + DigestLength;
    }

    protected virtual void Update()
    {
        ReadInput();
        UpdateBlink();
        if (body != null) body.color = BodyColor;
        UpdateCooldownTint();
        if (shieldBubble != null) shieldBubble.enabled = Record != null && Record.isShielded();
        if (MazeRuntime.Active != null && !CanEat && DigestLength > 0f && !string.IsNullOrEmpty(LastEatenId))
        {
            float progress = 1f - DigestLeft / DigestLength;
            MazeRuntime.Active.NotifyEatProgress(LastEatenId, progress);
        }
    }

    // A "Digesting" label over the character's head while it can't eat. The green tint shows the
    // cooldown draining; this spells out why eating is blocked, so the digest rule actually reads.
    // Tutorial only: it is a teaching aid, so once the player knows the rule we drop it in battles.
    void OnGUI()
    {
        if (!LevelLoader.Tutorial || !UsesDigest || CanEat) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * digestLabelHeight);
        if (screen.z <= 0f) return;   // behind the camera

        if (digestLabelStyle == null)
        {
            digestLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
        digestLabelStyle.normal.textColor = digestLabelColor;

        // GUI y grows downward, the opposite of screen-space y
        Rect rect = new Rect(screen.x - 70f, Screen.height - screen.y - 24f, 140f, 22f);
        GUI.Label(rect, "Digesting\u2026", digestLabelStyle);
    }

    void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
        }
        MoveInput = input;

        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && IsPlaying) UseSkill();

        // Flip only when the horizontal direction changes; keep facing when moving vertically
        if (!FlipsWhenTurning) return;
        if (input.x < 0f && facingRight)
        {
            Flip();
        }
        else if (input.x > 0f && !facingRight)
        {
            Flip();
        }
    }

    // Blink while invincible (right after losing a Life)
    void UpdateBlink()
    {
        bool invincible = GameManager.Instance != null && GameManager.Instance.IsInvincible;

        // Toggle visible/hidden 10 times per second
        bool visible = !invincible || Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) parts[i].enabled = partShown[i] && visible;
    }

    void FixedUpdate()
    {
        if (!IsPlaying)
        {
            // Round is over: stand still
            Body.linearVelocity = Vector2.zero;
            OnStopped();
            return;
        }

        Body.linearVelocity = Velocity(MoveInput);
    }

    // Mirror the whole character (and its children) horizontally
    void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x = -scale.x;
        transform.localScale = scale;
    }

    // The velocity for this physics step. Normalize so diagonals are not faster; physics stops us at colliders
    protected virtual Vector2 Velocity(Vector2 input)
    {
        return CurrentSpeed * input.normalized;
    }

    // Space was pressed during a round
    protected virtual void UseSkill() { }

    // The round is over: cancel whatever the skill is doing
    protected virtual void OnStopped() { }

    // The body's color right now (Mr. Hyde turns blue while blocking)
    protected virtual Color BodyColor => BaseColor;

    // A skill cooldown to draw on the body: left is 1 just after use and 0 when ready
    protected virtual bool SkillCooldown(out float left, out Color tint)
    {
        left = 0f;
        tint = Color.clear;
        return false;
    }
}
