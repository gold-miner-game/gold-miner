using System;
using UnityEngine;

// Generic runtime loadout slot. Elsa is the first character migrated to it.
public class RuntimeSkillSlot : MonoBehaviour
{
    PlayerCharacter owner;
    CharacterSkillDefinition[] available;
    public string CharacterId { get; private set; }
    public CharacterSkill ActiveSkill { get; private set; }
    public CharacterSkillDefinition CurrentDefinition { get; private set; }
    public CharacterSkillDefinition DefaultDefinition { get; private set; }
    public CharacterSkillDefinition[] AvailableDefinitions => available ?? Array.Empty<CharacterSkillDefinition>();
    public event Action Changed;

    public void Initialize(PlayerCharacter character, string characterId)
    {
        owner = character; CharacterId = characterId;
        available = Resources.LoadAll<CharacterSkillDefinition>("Skills");
        Array.Sort(available, (a,b) => string.Compare(a.displayName, b.displayName, StringComparison.Ordinal));
        var loadout = Resources.Load<CharacterSkillLoadout>("CharacterSkillLoadout");
        DefaultDefinition = loadout != null ? loadout.Find(characterId) : null;
        Equip(DefaultDefinition);
    }

    public void Equip(CharacterSkillDefinition definition)
    {
        if (owner == null) owner = GetComponent<PlayerCharacter>();
        if (owner == null) return;
        if (CurrentDefinition == definition && ActiveSkill != null) return;
        if (ActiveSkill != null)
        {
            ActiveSkill.enabled = false;
            ActiveSkill.Unequip();
            if (Application.isPlaying) Destroy(ActiveSkill); else DestroyImmediate(ActiveSkill);
        }
        ActiveSkill = null; CurrentDefinition = definition;
        if (definition != null) ActiveSkill = definition.CreateRuntime(owner);
        Changed?.Invoke();
    }
    public bool TryActivate() => ActiveSkill != null && ActiveSkill.TryActivate();
    public void Cancel() { if (ActiveSkill != null) ActiveSkill.Cancel(); }
}
