using UnityEngine;

// Runtime skill behavior. Character prefabs never serialize these components.
public abstract class CharacterSkill : MonoBehaviour
{
    public PlayerCharacter Owner { get; private set; }
    public CharacterSkillDefinition Definition { get; private set; }
    public virtual string SkillName => Definition != null ? Definition.displayName : "skill";
    public abstract bool IsActive { get; }
    public virtual bool BlocksMovement => false;
    public abstract float ReadyIn { get; }
    public abstract float Wait { get; }
    public virtual void Initialize(PlayerCharacter owner, CharacterSkillDefinition definition)
    {
        Owner = owner; Definition = definition;
    }
    public abstract bool TryActivate();
    public abstract void Cancel();
    public virtual void Unequip() { Cancel(); }
}
