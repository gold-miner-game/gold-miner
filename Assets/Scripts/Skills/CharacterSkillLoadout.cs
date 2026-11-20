using UnityEngine;

[CreateAssetMenu(menuName = "Gold Miner/Character Skill Loadout")]
public class CharacterSkillLoadout : ScriptableObject
{
    [System.Serializable]
    public struct Entry { public string characterId; public CharacterSkillDefinition skill; }
    public Entry[] entries;
    public CharacterSkillDefinition Find(string characterId)
    {
        if (entries != null)
            foreach (Entry entry in entries) if (entry.characterId == characterId) return entry.skill;
        return null;
    }
}
