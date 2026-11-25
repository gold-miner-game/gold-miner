using System.Collections.Generic;
using UnityEngine;

// Hulk's K charge accelerates instead of using the normal fixed movement speed.
public class RhinoController : PlayerCharacter
{
    [Header("Charge (K) - tune during Play mode")]
    [Min(.01f)] public float skillDuration = .8f;
    [Range(-60f, 60f)] public float rotateAngle = 15f;
    [Min(0f)] public float acceleration = 12f;
    [Min(.01f)] public float maximumChargeSpeed = 20f;
    [Min(0f)] public float skillCooldown = 5f;
    float ScaledCooldown => skillCooldown * PlayerCharacter.CooldownScale;   // the level can lengthen it (skill_cooldown_scale)
    public Color chargedHornColor = Color.red;

    [Header("Charge shake")]
    [Tooltip("Complete shakes per second (Hz). Zero disables shaking.")]
    [Min(0f)] public float shakeFrequency = 12f;
    [Tooltip("Maximum swing to either side of the charge tilt, in degrees. Total arc is twice this value.")]
    [Range(0f, 45f)] public float shakeHalfArc = 3f;

    [Header("Moving ears")]
    [Min(0f)] public float earWaveFrequency = 3.5f;
    [Range(0f, 45f)] public float earWaveAngle = 14f;

    [Header("Charge artwork")]
    public Transform visualRoot;
    public SpriteRenderer largeHorn;
    [Tooltip("Extra distance ahead of the next physics step to smash a rock before colliding.")]
    [Min(0f)] public float smashReach = .08f;
    [Tooltip("Legacy serialized setting. K now breaks ordinary rocks immediately inside its guard sector.")]
    [Min(0f)] public float rockBreakSpeedFactor = 3f / 61f;

    public bool IsCharging => charging && Time.time - chargeStarted < Mathf.Max(.01f, skillDuration);
    public Vector2 ChargeVelocity => chargeVelocity;
    public Vector2 ChargeDirection => chargeVelocity.sqrMagnitude > .0001f ? chargeVelocity.normalized : chargeHeading;
    HulkChargeGuard chargeGuard;
    public override bool HasSkill => true;
    public override string SkillName => "charge";
    public override bool SkillActive => IsCharging;
    public override float SkillReadyIn => IsCharging
        ? Mathf.Max(0f, skillDuration - (Time.time - chargeStarted)) + Mathf.Max(0f, ScaledCooldown)
        : Mathf.Max(0f, readyAt - Time.time);
    public override float SkillWait => Mathf.Max(.01f, skillDuration) + Mathf.Max(0f, ScaledCooldown);
    protected override bool FlipsWhenTurning => false;
    protected override bool UsesSharedCooldownTint => false;

    bool charging;
    float chargeStarted;
    float readyAt;
    Vector2 chargeVelocity;
    Vector2 chargeHeading = Vector2.right;
    Quaternion restRotation;
    Color restHornColor;
    bool artworkReady;
    readonly List<SpriteRenderer> chargeSources = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> chargeOverlays = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> digestEarSources = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> digestEarOverlays = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> detailSources = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> detailOverlays = new List<SpriteRenderer>();
    Material overlapTintMaterial;
    Material overlapEarMaterial;
    SpriteMask chargeMask;
    Sprite chargeMaskSprite;
    Sprite chargeGrowthSprite;
    Texture2D chargeGrowthTexture;
    const int ChargeTintOrder = 30000;
    readonly RaycastHit2D[] smashHits = new RaycastHit2D[64];
    readonly List<EarPose> ears = new List<EarPose>();
    float earWavePhase;
    float earWaveWeight;

    class EarPose
    {
        public Transform part;
        public Vector3 restPosition;
        public Quaternion restRotation;
        public Vector3 baseOffset;
        public float phase;
    }

    protected override void Awake()
    {
        if (visualRoot == null) visualRoot = transform.Find("Artwork");
        if (largeHorn == null)
            foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>(true))
                if (sprite.name == "Large Horn") { largeHorn = sprite; break; }
        restRotation = visualRoot != null ? visualRoot.localRotation : Quaternion.identity;
        restHornColor = largeHorn != null ? largeHorn.color : Color.white;
        // Capture only the actual character artwork, before the shared HUD/digest overlays exist.
        Transform artwork = visualRoot != null ? visualRoot : transform;
        CaptureEars(artwork);
        foreach (SpriteRenderer sprite in artwork.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (IsChargeSurface(sprite.transform)) chargeSources.Add(sprite);
            else if (IsEar(sprite.transform, artwork)) digestEarSources.Add(sprite);
            else detailSources.Add(sprite);
        }
        artworkReady = true;
        base.Awake();
        chargeGuard = GetComponent<HulkChargeGuard>();
        if (chargeGuard == null) chargeGuard = gameObject.AddComponent<HulkChargeGuard>();
        chargeGuard.Initialize(this);
        BuildChargeTint();
    }

    protected override void Update()
    {
        if (charging && !IsCharging) EndCharge();
        if (charging && GameManager.Instance != null && !GameManager.Instance.IsPlaying) EndCharge();
        base.Update();
        if (!IsCharging && IsPlaying) FaceDirection(MoveInput);
    }

    protected override void UseSkill()
    {
        if (!IsPlaying || charging || Time.time < readyAt) return;
        chargeHeading = MoveInput.sqrMagnitude > .0001f ? MoveInput.normalized
            : transform.localScale.x < 0f ? Vector2.left : Vector2.right;
        chargeVelocity = Body != null ? Body.linearVelocity : Vector2.zero;
        chargeStarted = Time.time;
        charging = true;
        FaceDirection(chargeHeading);
        chargeGuard.BeginCharge();
        UpdateChargeArtwork();
    }

    protected override Vector2 Velocity(Vector2 input)
    {
        if (!IsCharging) return base.Velocity(input);
        if (!IsPlaying) return Vector2.zero;
        if (input.sqrMagnitude > .0001f) chargeHeading = input.normalized;
        chargeVelocity = Accelerate(chargeVelocity, chargeHeading, Time.fixedDeltaTime);
        Vector2 forward = chargeVelocity.sqrMagnitude > .0001f ? chargeVelocity.normalized : chargeHeading;
        FaceDirection(forward);
        SmashAhead(forward, chargeVelocity.magnitude * Time.fixedDeltaTime + Mathf.Max(0f, smashReach));
        return chargeVelocity;
    }

    // Exposed separately from physics so acceleration is consistent and can be regression-tested.
    Vector2 Accelerate(Vector2 velocity, Vector2 heading, float seconds)
    {
        return Vector2.ClampMagnitude(velocity + heading.normalized * Mathf.Max(0f, acceleration)
            * Mathf.Max(0f, seconds), Mathf.Max(.01f, maximumChargeSpeed));
    }

    void SmashAhead(Vector2 forward, float distance)
    {
        if (Body == null) return;
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        int count = Body.Cast(forward, filter, smashHits, distance);
        // Every existing solid obstacle stops the sweep, including a rock still breaking.
        float stopAt = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider2D collider = smashHits[i].collider;
            if (collider == null || collider.GetComponentInParent<PlayerCharacter>() == this) continue;
            stopAt = Mathf.Min(stopAt, smashHits[i].distance);
        }
        for (int i = 0; i < count; i++)
        {
            if (smashHits[i].collider == null || smashHits[i].distance > stopAt + .001f) continue;
            Rock rock = smashHits[i].collider.GetComponentInParent<Rock>();
            if (rock != null && Vector2.Dot((Vector2)rock.transform.position - Body.worldCenterOfMass, forward) > 0f)
                chargeGuard.TrySmashRock(rock);
        }
    }


    void FaceDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) < .001f) return;
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (direction.x < 0f ? -1f : 1f);
        transform.localScale = scale;
    }

    void LateUpdate()
    {
        UpdateMovingEars();
        UpdateChargeArtwork();
    }

    void CaptureEars(Transform artwork)
    {
        foreach (Transform part in artwork.GetComponentsInChildren<Transform>(true))
        {
            if (part.name != "Top Ear" && part.name != "Back Ear") continue;
            bool found = false;
            Bounds bounds = new Bounds();
            foreach (SpriteRenderer sprite in part.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite.sprite == null) continue;
                Bounds local = LocalBounds(part, sprite);
                if (!found) { bounds = local; found = true; }
                else bounds.Encapsulate(local);
            }
            if (!found) continue;
            // The capsule runs along local X. Its lower end stays attached to the head.
            Vector3 hinge = new Vector3(bounds.min.x, bounds.center.y, 0f);
            ears.Add(new EarPose
            {
                part = part,
                restPosition = part.localPosition,
                restRotation = part.localRotation,
                baseOffset = Vector3.Scale(hinge, part.localScale),
                phase = part.name == "Back Ear" ? 0.9f : 0f
            });
        }
    }

    void UpdateMovingEars()
    {
        float speed = IsPlaying && Body != null ? Body.linearVelocity.magnitude : 0f;
        float motion = Mathf.Clamp01(speed / Mathf.Max(.01f, CurrentSpeed));
        earWaveWeight = Mathf.MoveTowards(earWaveWeight, motion, Time.deltaTime * 10f);
        if (motion > .01f)
            earWavePhase = (earWavePhase + Time.deltaTime * earWaveFrequency * 2f * Mathf.PI) % (2f * Mathf.PI);
        foreach (EarPose ear in ears)
        {
            if (ear.part == null) continue;
            float angle = Mathf.Sin(earWavePhase + ear.phase) * earWaveAngle * earWaveWeight;
            Quaternion rotation = ear.restRotation * Quaternion.Euler(0f, 0f, angle);
            Vector3 hinge = ear.restPosition + ear.restRotation * ear.baseOffset;
            ear.part.localRotation = rotation;
            ear.part.localPosition = hinge - rotation * ear.baseOffset;
        }
    }

    void ResetEars()
    {
        earWaveWeight = 0f;
        earWavePhase = 0f;
        foreach (EarPose ear in ears)
        {
            if (ear.part == null) continue;
            ear.part.localPosition = ear.restPosition;
            ear.part.localRotation = ear.restRotation;
        }
    }

    void UpdateChargeArtwork()
    {
        if (!artworkReady) return;
        if (visualRoot != null)
            visualRoot.localRotation = restRotation * Quaternion.Euler(0f, 0f,
                IsCharging ? -rotateAngle + ChargeShakeAngle(Time.time - chargeStarted) : 0f);
        if (largeHorn != null) largeHorn.color = IsCharging ? chargedHornColor : restHornColor;
        UpdateChargeTint();
    }

    float ChargeShakeAngle(float elapsed)
    {
        return Mathf.Sin(Mathf.Max(0f, elapsed) * Mathf.Max(0f, shakeFrequency) * 2f * Mathf.PI)
            * Mathf.Max(0f, shakeHalfArc);
    }

    void EndCharge(bool startCooldown = true)
    {
        if (charging && startCooldown) readyAt = Time.time + Mathf.Max(0f, ScaledCooldown);
        if (!startCooldown) readyAt = 0f;
        charging = false;
        if (chargeGuard != null) chargeGuard.EndCharge();
        chargeVelocity = Vector2.zero;
        UpdateChargeArtwork();
    }

    protected override void OnStopped()
    {
        // Pause/console input freezes the charge; ending a round cancels it.
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) EndCharge();
    }

    // Hulk uses his whole-artwork wipe for digestion as well as the charge cooldown.
    protected override bool SkillCooldown(out float left, out Color tint)
    {
        left = 0f;
        tint = chargedHornColor;
        return false;
    }

    static bool IsChargeSurface(Transform part)
    {
        return part.name == "Head" || part.name == "Body" || part.name == "Muzzle"
            || part.name == "Large Horn" || part.name == "Small Horn";
    }

    static bool IsEar(Transform part, Transform artwork)
    {
        for (Transform at = part; at != null && at != artwork; at = at.parent)
            if (at.name.IndexOf("Ear", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    void BuildChargeTint()
    {
        if (chargeSources.Count == 0) return;
        if (GetComponent<UnityEngine.Rendering.SortingGroup>() == null)
            gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
        int layer = chargeSources[0].sortingLayerID;
        for (int i = 0; i < chargeSources.Count + digestEarSources.Count; i++)
        {
            bool ear = i >= chargeSources.Count;
            SpriteRenderer source = ear ? digestEarSources[i - chargeSources.Count] : chargeSources[i];
            var go = new GameObject(ear ? "DigestTint" : "ChargeTint");
            go.transform.SetParent(source.transform, false);
            var overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = source.sprite;
            overlay.sharedMaterial = source.sharedMaterial;
            overlay.sortingLayerID = layer;
            overlay.sortingOrder = ear ? ChargeTintOrder - 2 : ChargeTintOrder;
            overlay.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            overlay.enabled = false;
            if (ear) digestEarOverlays.Add(overlay);
            else chargeOverlays.Add(overlay);
        }
        // Preserve face details above the red body overlays. Ears stay in their original
        // rear layer, so their visible outline stays cyan without drawing over the head.
        foreach (SpriteRenderer source in detailSources)
        {
            var go = new GameObject("ChargeFaceDetail");
            go.transform.SetParent(source.transform, false);
            var detail = go.AddComponent<SpriteRenderer>();
            detail.sprite = source.sprite;
            detail.sharedMaterial = source.sharedMaterial;
            detail.sortingLayerID = layer;
            detail.sortingOrder = ChargeTintOrder + 2 + source.sortingOrder;
            detail.maskInteraction = SpriteMaskInteraction.None;
            detail.enabled = false;
            detailOverlays.Add(detail);
        }
        Shader overlapShader = Resources.Load<Shader>("Shaders/RhinoTintOverlap");
        overlapTintMaterial = new Material(overlapShader);
        overlapEarMaterial = new Material(overlapShader);
        overlapEarMaterial.SetFloat("_RedEnabled", 0f);
        Texture2D white = Texture2D.whiteTexture;
        chargeMaskSprite = ArtLibrary.MaskShape("Fx/TornFill", new Vector2(.5f, 0f))   // art: a torn-paper fill line
            ?? Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(.5f, 0f), white.width);
        const int circleSize = 64;
        chargeGrowthTexture = new Texture2D(circleSize, circleSize, TextureFormat.RGBA32, false);
        chargeGrowthTexture.name = "Hulk charge expansion circle";
        chargeGrowthTexture.filterMode = FilterMode.Bilinear;
        chargeGrowthTexture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[circleSize * circleSize];
        for (int y = 0; y < circleSize; y++)
            for (int x = 0; x < circleSize; x++)
            {
                Vector2 point = new Vector2((x + .5f) / circleSize - .5f, (y + .5f) / circleSize - .5f);
                pixels[y * circleSize + x] = point.sqrMagnitude <= .25f ? Color.white : Color.clear;
            }
        chargeGrowthTexture.SetPixels(pixels);
        chargeGrowthTexture.Apply();
        chargeGrowthSprite = ArtLibrary.MaskShape("Fx/CutDisc", Vector2.one * .5f)   // art: a hand-cut disc
            ?? Sprite.Create(chargeGrowthTexture, new Rect(0, 0, circleSize, circleSize),
            Vector2.one * .5f, circleSize, 0, SpriteMeshType.FullRect);
        var maskObject = new GameObject("Whole Hulk Charge Mask");
        maskObject.transform.SetParent(transform, false);
        chargeMask = maskObject.AddComponent<SpriteMask>();
        chargeMask.sprite = chargeMaskSprite;
        chargeMask.isCustomRangeActive = true;
        chargeMask.frontSortingLayerID = chargeMask.backSortingLayerID = layer;
        chargeMask.frontSortingOrder = ChargeTintOrder + 1;
        chargeMask.backSortingOrder = ChargeTintOrder - 1;
        chargeMask.enabled = false;
    }

    float ChargeExpansion(float elapsed)
    {
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(.01f, skillDuration));
        // Quadratic radius: accelerating spread, with complete coverage at the endpoint.
        return progress * progress;
    }

    void UpdateChargeTint()
    {
        if (chargeMask == null) return;
        bool digesting = IsDigesting;
        bool active = IsCharging && !digesting;
        float chargeFraction = IsCharging
            ? ChargeExpansion(Time.time - chargeStarted)
            : ScaledCooldown > 0f ? Mathf.Clamp01((readyAt - Time.time) / ScaledCooldown) : 0f;
        bool overlapping = digesting && chargeFraction > 0f;
        Color tint = digesting ? digestTint : chargedHornColor;
        // Charge spreads out from the horn/head joint. Cooldown clears from top to bottom.
        // Keep both wipes, with digestion green taking priority where they overlap.
        float fraction = digesting ? Mathf.Clamp01(DigestLeft / DigestLength)
            : active ? ChargeExpansion(Time.time - chargeStarted)
            : ScaledCooldown > 0f ? Mathf.Clamp01((readyAt - Time.time) / ScaledCooldown) : 0f;
        bool show = fraction > 0f;
        for (int i = 0; i < detailSources.Count; i++)
        {
            SpriteRenderer source = detailSources[i];
            SpriteRenderer detail = detailOverlays[i];
            if (source == null || detail == null) continue;
            detail.enabled = show && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
            detail.sprite = source.sprite;
            detail.flipX = source.flipX; detail.flipY = source.flipY;
            detail.color = source.color;
        }
        bool foundBounds = false;
        Bounds bounds = new Bounds();
        Bounds redBounds = new Bounds();
        bool foundRedBounds = false;
        for (int i = 0; i < chargeSources.Count + digestEarSources.Count; i++)
        {
            bool ear = i >= chargeSources.Count;
            SpriteRenderer source = ear ? digestEarSources[i - chargeSources.Count] : chargeSources[i];
            SpriteRenderer overlay = ear ? digestEarOverlays[i - chargeSources.Count] : chargeOverlays[i];
            if (source == null || overlay == null) continue;
            bool include = !ear || digesting;
            overlay.enabled = include && show && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
            overlay.sprite = source.sprite;
            overlay.flipX = source.flipX; overlay.flipY = source.flipY;
            overlay.sharedMaterial = overlapping ? (ear ? overlapEarMaterial : overlapTintMaterial) : source.sharedMaterial;
            overlay.maskInteraction = overlapping ? SpriteMaskInteraction.None : SpriteMaskInteraction.VisibleInsideMask;
            overlay.color = overlapping ? new Color(1f, 1f, 1f, source.color.a)
                : new Color(tint.r, tint.g, tint.b, source.color.a * tint.a);
            if (!include || source.sprite == null || !source.gameObject.activeInHierarchy) continue;
            Bounds sprite = source.sprite.bounds;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = source.transform.TransformPoint(new Vector3(
                    corner % 2 == 0 ? sprite.min.x : sprite.max.x,
                    corner < 2 ? sprite.min.y : sprite.max.y, 0f));
                if (!foundBounds) { bounds = new Bounds(point, Vector3.zero); foundBounds = true; }
                else bounds.Encapsulate(point);
                if (!ear)
                {
                    if (!foundRedBounds) { redBounds = new Bounds(point, Vector3.zero); foundRedBounds = true; }
                    else redBounds.Encapsulate(point);
                }
            }
        }
        if (overlapping && foundBounds && foundRedBounds)
        {
            Vector3 joint = HornJointPosition();
            float radius = 0f;
            for (int corner = 0; corner < 4; corner++)
                radius = Mathf.Max(radius, Vector2.Distance(joint, new Vector2(
                    corner % 2 == 0 ? redBounds.min.x : redBounds.max.x,
                    corner < 2 ? redBounds.min.y : redBounds.max.y)));
            foreach (Material material in new[] { overlapTintMaterial, overlapEarMaterial })
            {
                material.SetColor("_ChargeColor", chargedHornColor);
                material.SetColor("_DigestColor", digestTint);
                material.SetVector("_RedShape", new Vector4(joint.x, joint.y,
                    (radius + .002f) * chargeFraction, redBounds.min.y + redBounds.size.y * chargeFraction));
                material.SetFloat("_Circular", IsCharging ? 1f : 0f);
                material.SetFloat("_GreenTop", bounds.min.y + bounds.size.y * fraction);
            }
        }
        chargeMask.backSortingOrder = digesting ? ChargeTintOrder - 3 : ChargeTintOrder - 1;
        chargeMask.enabled = show && foundBounds;
        if (!chargeMask.enabled) return;
        float width, height;
        Vector3 position;
        if (active)
        {
            chargeMask.sprite = chargeGrowthSprite;
            position = HornJointPosition();
            float radius = 0f;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 point = new Vector2(corner % 2 == 0 ? bounds.min.x : bounds.max.x,
                    corner < 2 ? bounds.min.y : bounds.max.y);
                radius = Mathf.Max(radius, Vector2.Distance(position, point));
            }
            width = height = (radius + .002f) * 2f * fraction;
        }
        else
        {
            chargeMask.sprite = chargeMaskSprite;
            width = bounds.size.x + .002f;
            height = bounds.size.y * fraction;
            position = new Vector3(bounds.center.x, bounds.min.y, transform.position.z);
        }
        chargeMask.transform.position = position;
        chargeMask.transform.rotation = Quaternion.identity;
        Vector3 parentScale = transform.lossyScale;
        chargeMask.transform.localScale = new Vector3(
            width / (Mathf.Abs(parentScale.x) > .0001f ? parentScale.x : 1f),
            height / (Mathf.Abs(parentScale.y) > .0001f ? parentScale.y : 1f), 1f);
    }

    Vector3 HornJointPosition()
    {
        if (largeHorn == null || largeHorn.sprite == null)
            return visualRoot != null ? visualRoot.position : transform.position;
        Bounds horn = largeHorn.sprite.bounds;
        return largeHorn.transform.TransformPoint(new Vector3(horn.center.x,
            largeHorn.flipY ? horn.max.y : horn.min.y, 0f));
    }

    void OnDisable() { EndCharge(false); ResetEars(); }
    void OnDestroy()
    {
        if (overlapTintMaterial != null) Destroy(overlapTintMaterial);
        if (overlapEarMaterial != null) Destroy(overlapEarMaterial);
        if (chargeMaskSprite != null) Destroy(chargeMaskSprite);
        if (chargeGrowthSprite != null) Destroy(chargeGrowthSprite);
        if (chargeGrowthTexture != null) Destroy(chargeGrowthTexture);
    }
}
