using UnityEngine;

[CreateAssetMenu(menuName = "Gold Miner/Skills/Cold Breath")]
public class ColdBreathSkillDefinition : CharacterSkillDefinition
{
    [Tooltip("Active cast, ending with the last major wave. Excludes trailing particles.")]
    [Min(.01f)] public float skillDuration = 1.5f;
    [Tooltip("Visual particles after the active cast; cooldown has already begun.")]
    [Min(0f)] public float afterEffectDuration = .5f;
    [Min(0f)] public float skillCooldown = 5f;
    [Range(0f, 110f)] public float skillMouthHalfArc = 25f;
    public ColdBreathFlipbook2D coldBreathPrefab;
    [Min(.01f)] public float breathLength = 4.572f;
    [Range(1f, 89f)] public float breathHalfArc = 45f;
    [Min(0f)] public float breathMouthOffset = .04f;
    public bool matchMouthToBreath = true;
    public Color iceTint = new Color(.8f, .86f, .9f, 1f);
    public override CharacterSkill CreateRuntime(PlayerCharacter owner)
    {
        var skill = owner.gameObject.AddComponent<ElsaColdBreathSkill>();
        skill.Initialize(owner, this);
        return skill;
    }
}
