using UnityEngine;

// Independent ability asset. New skills can be added without editing a character prefab.
public abstract class CharacterSkillDefinition : ScriptableObject
{
    public string skillId;
    public string displayName;
    public abstract CharacterSkill CreateRuntime(PlayerCharacter owner);
}
