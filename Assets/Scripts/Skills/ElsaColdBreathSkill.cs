using UnityEngine;

// Created by the selected skill definition at runtime; never attached to an Elsa prefab.
[DefaultExecutionOrder(100)]
public class ElsaColdBreathSkill : CharacterSkill
{
    [Tooltip("Active main wave only. Movement unlocks and cooldown begins when this ends.")]
    [Min(.01f)] public float skillDuration;
    [Min(0f)] public float skillCooldown;
    [Range(0f,110f)] public float skillMouthHalfArc;
    [Tooltip("Visual-only particle fade after the active cast; does not block movement or apply frost.")]
    [Min(0f)] public float afterEffectDuration = .5f;
    public ColdBreathFlipbook2D coldBreathPrefab;
    public float breathLength, breathHalfArc, breathMouthOffset;
    public bool matchMouthToBreath;
    public ElsaMouthPose mouthPose;
    ColdBreathFlipbook2D breath;
    bool casting;
    bool afterPlaying;
    float afterStarted;
    float castStarted, readyAt;
    Rigidbody2D castingBody;
    RigidbodyConstraints2D previousConstraints;
    bool movementLocked;
    public ColdBreathFlipbook2D BreathEffect => breath;
    public bool IsCasting => casting && Time.time - castStarted < Mathf.Max(.01f, skillDuration);
    public override bool IsActive => IsCasting;
    public override bool BlocksMovement => IsCasting;
    public override float ReadyIn => IsCasting
        ? Mathf.Max(0f, skillDuration - (Time.time - castStarted)) + Mathf.Max(0f, skillCooldown)
        : Mathf.Max(0f, readyAt - Time.time);
    public override float Wait => Mathf.Max(.01f, skillDuration) + Mathf.Max(0f, skillCooldown);

    public override void Initialize(PlayerCharacter owner, CharacterSkillDefinition definition)
    {
        base.Initialize(owner, definition);
        var settings = (ColdBreathSkillDefinition)definition;
        skillDuration = settings.skillDuration; skillCooldown = settings.skillCooldown;
        afterEffectDuration = settings.afterEffectDuration;
        skillMouthHalfArc = settings.skillMouthHalfArc; coldBreathPrefab = settings.coldBreathPrefab;
        breathLength = settings.breathLength; breathHalfArc = settings.breathHalfArc;
        breathMouthOffset = settings.breathMouthOffset; matchMouthToBreath = settings.matchMouthToBreath;
        mouthPose = owner.GetComponent<ElsaMouthPose>();
        if (mouthPose == null) mouthPose = owner.gameObject.AddComponent<ElsaMouthPose>();
        mouthPose.iceTint = settings.iceTint;
        UpdateSkillPose();
    }
    void Update()
    {
        if (Owner == null || !Owner.isActiveAndEnabled) { Cancel(); return; }
        if (casting && !IsCasting) FinishCast();
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) Cancel();
    }
    void LateUpdate() { if (Owner != null) UpdateSkillPose(); }
    public override bool TryActivate()
    {
        if (Owner == null || !Owner.isActiveAndEnabled || !PlayerCharacter.IsPlaying || casting || Time.time < readyAt) return false;
        castStarted = Time.time; casting = true; afterPlaying = false;
        LockMovement();
        UpdateSkillPose(); return true;
    }
    void FinishCast()
    {
        casting = false;
        afterStarted = castStarted + Mathf.Max(.01f, skillDuration);
        afterPlaying = breath != null && afterEffectDuration > 0f;
        // Leave the remaining particles at the cast location as Elsa walks away.
        if (breath != null) breath.transform.SetParent(null, true);
        UnlockMovement(); readyAt = afterStarted + Mathf.Max(0f, skillCooldown); UpdateSkillPose();
    }
    public void UpdateSkillPose()
    {
        if (mouthPose == null) return;
        bool active = IsCasting;
        float opening = active && matchMouthToBreath && coldBreathPrefab != null
            ? mouthPose.OpeningForHalfArc(Mathf.Clamp(breathHalfArc,1f,89f)) : skillMouthHalfArc;
        mouthPose.SetPose(!active && Time.time >= readyAt, active ? opening : 0f);
        UpdateBreath(active);
    }
    void UpdateBreath(bool active)
    {
        if (Owner == null) return;
        if (breath == null && active && coldBreathPrefab != null)
        {
            breath = Instantiate(coldBreathPrefab, Owner.transform, false); breath.name = "Elsa Cold Breath";
            breath.externalPlayback = true; breath.loop = false;
            var renderer = breath.GetComponent<MeshRenderer>();
            renderer.sortingLayerID = Owner.body != null ? Owner.body.sortingLayerID : 0;
            renderer.sortingOrder = Owner.body != null ? Owner.body.sortingOrder + 1 : 1;
            breath.Replay();
        }
        if (breath == null) return;
        bool tail = afterPlaying && Time.time - afterStarted < Mathf.Max(0f, afterEffectDuration);
        if (!tail) afterPlaying = false;
        bool visible = active || tail;
        if (breath.gameObject.activeSelf != visible) { breath.gameObject.SetActive(visible); if (active) breath.Replay(); }
        if (!active)
        {
            if (tail)
            {
                float fade = Mathf.Clamp01((Time.time - afterStarted) / Mathf.Max(.01f, afterEffectDuration));
                breath.ShowTime(Mathf.Lerp(ColdBreathFlipbook2D.MainWaveEndTime, ColdBreathFlipbook2D.PlaybackDuration, fade));
            }
            return;
        }
        if (breath.transform.parent != Owner.transform)
        {
            breath.transform.SetParent(Owner.transform, false);
            breath.transform.localScale = coldBreathPrefab.transform.localScale;
        }
        breath.length = Mathf.Max(.01f, breathLength); breath.halfArc = Mathf.Clamp(breathHalfArc,1f,89f);
        float progress = Mathf.Clamp01((Time.time-castStarted)/Mathf.Max(.01f,skillDuration));
        breath.ShowTime(progress*ColdBreathFlipbook2D.MainWaveEndTime);
        if (mouthPose.TryGetMouthFrame(out Vector3 at, out Vector2 forward))
        {
            breath.transform.position = at + (Vector3)forward * Mathf.Max(0f,breathMouthOffset);
            Vector3 local = Owner.transform.InverseTransformVector(forward);
            breath.transform.localRotation = Quaternion.Euler(0,0,Mathf.Atan2(local.y,local.x)*Mathf.Rad2Deg-90f);
            if (breath.ContactLength > 0f)
                FrostField2D.ExposeTransformed(Owner.transform, breath.transform, breath.ContactLength, breath.halfArc, true);
        }
    }
    public override void Cancel()
    {
        casting = false; afterPlaying = false; UnlockMovement(); readyAt = 0f; UpdateSkillPose();
    }
    public override void Unequip()
    {
        casting = false; afterPlaying = false; UnlockMovement(); readyAt = 0f;
        if (mouthPose != null) mouthPose.SetPose(false,0f);
        DestroyBreath();
    }
    void OnDisable() { Cancel(); }
    void LockMovement()
    {
        if (movementLocked || Owner == null) return;
        castingBody = Owner.GetComponent<Rigidbody2D>();
        if (castingBody == null) return;
        previousConstraints = castingBody.constraints;
        movementLocked = true;
        castingBody.linearVelocity = Vector2.zero;
        castingBody.constraints |= RigidbodyConstraints2D.FreezePosition;
    }
    void UnlockMovement()
    {
        if (!movementLocked) return;
        if (castingBody != null)
        {
            castingBody.linearVelocity = Vector2.zero;
            castingBody.constraints = (castingBody.constraints & ~RigidbodyConstraints2D.FreezePosition)
                | (previousConstraints & RigidbodyConstraints2D.FreezePosition);
        }
        movementLocked = false;
    }
    void DestroyBreath()
    {
        if (breath == null) return;
        GameObject effect = breath.gameObject; breath = null; effect.SetActive(false);
        if (Application.isPlaying) Destroy(effect); else DestroyImmediate(effect);
    }
    void OnDestroy() { UnlockMovement(); DestroyBreath(); }
}
