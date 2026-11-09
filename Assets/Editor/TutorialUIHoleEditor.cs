using UnityEditor;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// TutorialUIHole's Inspector: drag a UI object (from the Game scene's HUD, open next to the level)
// onto "Pick UI object" to fill in its name, and see whether that name is found.
[CustomEditor(typeof(TutorialUIHole))]
class TutorialUIHoleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var hole = (TutorialUIHole)target;

        EditorGUILayout.Space();
        var picked = (RectTransform)EditorGUILayout.ObjectField("Pick UI object", null, typeof(RectTransform), true);
        if (picked != null)
        {
            Undo.RecordObject(hole, "Pick UI object");
            hole.target = picked.name;
            EditorUtility.SetDirty(hole);
        }

        if (string.IsNullOrEmpty(hole.target))
        {
            EditorGUILayout.HelpBox("Drag a HUD object (e.g. Timer, Lives, WalletValue) from the Hierarchy onto \"Pick UI object\".", MessageType.Info);
            return;
        }

        int matches = 0;
        foreach (RectTransform rect in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (rect.name == hole.target && rect.GetComponentInParent<Canvas>(true) != null) matches++;
        if (matches == 0)
            EditorGUILayout.HelpBox("No UI object named \"" + hole.target + "\" in the open scenes. Open Game.unity next to this level to check.", MessageType.Warning);
        else if (matches > 1)
            EditorGUILayout.HelpBox(matches + " UI objects are named \"" + hole.target + "\"; the first one found is lit. Rename the one you mean to make it unique.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Lights \"" + hole.target + "\".", MessageType.None);
    }
}
