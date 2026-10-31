using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// [TutorialEvent] string fields: a text box with a ▾ button listing the events that exist (the
// game's TutorialEvents.Known, every step's ".start" / ".end", and the events dialogue lines fire),
// and a warning under it when the name matches none of them, e.g. "lost_life" for "life_lost".
[CustomPropertyDrawer(typeof(TutorialEventAttribute))]
public class TutorialEventDrawer : PropertyDrawer
{
    const float ButtonWidth = 22f;
    const float WarningHeight = 30f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        return Warning(property) != null ? line + WarningHeight + 2f : line;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }
        Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect field = new Rect(row.x, row.y, row.width - ButtonWidth - 2f, row.height);
        Rect button = new Rect(row.xMax - ButtonWidth, row.y, ButtonWidth, row.height);

        EditorGUI.PropertyField(field, property, label);
        if (GUI.Button(button, "▾", EditorStyles.miniButton)) ShowMenu(property);

        string warning = Warning(property);
        if (warning != null)
        {
            Rect box = new Rect(position.x + EditorGUIUtility.labelWidth, row.yMax + 2f,
                position.width - EditorGUIUtility.labelWidth, WarningHeight);
            EditorGUI.HelpBox(box, warning, MessageType.Warning);
        }
    }

    void ShowMenu(SerializedProperty property)
    {
        var menu = new GenericMenu();
        string current = property.stringValue;
        SerializedObject owner = property.serializedObject;
        string path = property.propertyPath;
        System.Action<string> set = value =>
        {
            owner.Update();
            owner.FindProperty(path).stringValue = value;
            owner.ApplyModifiedProperties();
        };

        foreach (string e in TutorialEvents.Known)
            menu.AddItem(new GUIContent("Game/" + e), e == current, () => set(e));
        foreach (string step in StepNames())
        {
            string start = step + ".start", end = step + ".end";
            menu.AddItem(new GUIContent("Steps/" + step + "/start"), start == current, () => set(start));
            menu.AddItem(new GUIContent("Steps/" + step + "/end"), end == current, () => set(end));
        }
        foreach (string e in DialogueEvents())
            menu.AddItem(new GUIContent("Dialogue lines/" + e), e == current, () => set(e));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(current), () => set(""));
        menu.ShowAsContext();
    }

    string Warning(SerializedProperty property)
    {
        if (property.propertyType != SerializedPropertyType.String) return null;
        string value = property.stringValue;
        if (string.IsNullOrEmpty(value)) return null;
        if (((TutorialEventAttribute)attribute).customAllowed) return null;
        if (AllEvents().Contains(value)) return null;

        string near = Nearest(value);
        return "Nothing fires \"" + value + "\"" + (near != null ? ". Did you mean \"" + near + "\"?" : ". Pick one with ▾.");
    }

    static HashSet<string> AllEvents()
    {
        var all = new HashSet<string>(TutorialEvents.Known);
        foreach (string step in StepNames()) { all.Add(step + ".start"); all.Add(step + ".end"); }
        foreach (string e in DialogueEvents()) all.Add(e);
        return all;
    }

    static IEnumerable<string> StepNames()
    {
        return Object.FindObjectsByType<TutorialStep>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(s => s.name).Distinct().OrderBy(n => n);
    }

    static IEnumerable<string> DialogueEvents()
    {
        return Object.FindObjectsByType<TutorialDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(d => d.lines != null)
            .SelectMany(d => d.lines)
            .Where(l => l != null && !string.IsNullOrEmpty(l.fireEvent))
            .Select(l => l.fireEvent).Distinct();
    }

    // The closest existing name, when it is close: the same words in another order (lost_life ->
    // life_lost), a different case, or a few letters off
    static string Nearest(string value)
    {
        string Words(string s) => string.Join("_", s.ToLowerInvariant().Split('_', '.', ' ').OrderBy(w => w));
        string best = null;
        int bestScore = int.MaxValue;
        foreach (string e in AllEvents())
        {
            int score = Words(e) == Words(value) ? 0 : Distance(e.ToLowerInvariant(), value.ToLowerInvariant());
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return bestScore <= Mathf.Max(2, value.Length / 3) ? best : null;
    }

    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    // Tools > Gold Miner > Log Tutorial Events: print each event to the Console while playing
    const string LogKey = "GoldMiner.LogTutorialEvents";
    const string LogMenu = "Tools/Gold Miner/Log Tutorial Events";

    [InitializeOnLoadMethod]
    static void RestoreLog() { TutorialEvents.Log = EditorPrefs.GetBool(LogKey, false); }

    [MenuItem(LogMenu, false, 130)]
    static void ToggleLog()
    {
        TutorialEvents.Log = !TutorialEvents.Log;
        EditorPrefs.SetBool(LogKey, TutorialEvents.Log);
    }

    [MenuItem(LogMenu, true)]
    static bool ToggleLogCheck()
    {
        Menu.SetChecked(LogMenu, TutorialEvents.Log);
        return true;
    }
}
