using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ElsaColdBreathSkill))]
public class ElsaColdBreathSkillEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var skill = (ElsaColdBreathSkill)target;
        var pose = skill.mouthPose;
        if (pose != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ice appearance", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Color tint = EditorGUILayout.ColorField("Ice color", pose.iceTint);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(pose, "Change Elsa ice color");
                pose.iceTint = tint; pose.ApplyPose(); EditorUtility.SetDirty(pose);
            }
            if (pose.ice != null)
            {
                var ice = pose.ice.transform;
                EditorGUI.BeginChangeCheck();
                Vector3 position = EditorGUILayout.Vector3Field("Ice local position", ice.localPosition);
                Vector3 scale = EditorGUILayout.Vector3Field("Ice local scale", ice.localScale);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(ice, "Adjust Elsa ice");
                    ice.localPosition = position; ice.localScale = scale; EditorUtility.SetDirty(ice);
                }
            }
        }
        EditorGUILayout.HelpBox("Values update live. Edit the ColdBreath skill asset outside Play Mode for permanent defaults. Ice shape is also editable under Artwork / Blue Tongue.", MessageType.Info);
    }
}
