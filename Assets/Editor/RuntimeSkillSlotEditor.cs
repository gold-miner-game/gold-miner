using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RuntimeSkillSlot))]
public class RuntimeSkillSlotEditor : Editor
{
    Editor skillEditor;
    void OnDisable() { if (skillEditor != null) DestroyImmediate(skillEditor); }
    public override void OnInspectorGUI()
    {
        var slot = (RuntimeSkillSlot)target;
        EditorGUILayout.LabelField("Character", slot.CharacterId ?? "Uninitialized");
        EditorGUILayout.HelpBox("Change the live K skill here. Play Mode changes reset when you stop. The skill is independent of the character prefab.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUI.BeginChangeCheck();
            var selected = (CharacterSkillDefinition)EditorGUILayout.ObjectField("K Skill", slot.CurrentDefinition, typeof(CharacterSkillDefinition), false);
            if (EditorGUI.EndChangeCheck()) slot.Equip(selected);
            if (GUILayout.Button("Restore default skill")) slot.Equip(slot.DefaultDefinition);
        }
        if (slot.ActiveSkill != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live skill settings", EditorStyles.boldLabel);
            CreateCachedEditor(slot.ActiveSkill, null, ref skillEditor);
            skillEditor.OnInspectorGUI();
        }
    }
}
