using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Meta progression that survives app restarts (PlayerPrefs): which map nodes are cleared and the
// chosen character id. Kept apart from CrossLevelStatus, which is the live, one-run player record.
public static class ProgressStore
{
    const string CompletedKey = "gm.progress.completed";   // ';'-separated node ids
    const string CharacterKey = "gm.progress.character";

    static HashSet<string> completed;

    static HashSet<string> Completed()
    {
        if (completed != null) return completed;
        completed = new HashSet<string>();
        string raw = PlayerPrefs.GetString(CompletedKey, "");
        foreach (string id in raw.Split(';'))
            if (!string.IsNullOrEmpty(id)) completed.Add(id);
        return completed;
    }

    public static bool IsCompleted(string nodeId)
    {
        return !string.IsNullOrEmpty(nodeId) && Completed().Contains(nodeId);
    }

    public static void MarkCompleted(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!Completed().Add(nodeId)) return;
        PlayerPrefs.SetString(CompletedKey, string.Join(";", Completed()));
        PlayerPrefs.Save();
    }

    public static string SelectedCharacter
    {
        get => PlayerPrefs.GetString(CharacterKey, "pig");
        set { PlayerPrefs.SetString(CharacterKey, value ?? "pig"); PlayerPrefs.Save(); }
    }

    public static bool HasPickedCharacter => PlayerPrefs.HasKey(CharacterKey);

    // Dev helper: wipe all meta progress (also clear the in-memory cache).
    public static void ResetAll()
    {
        completed = null;
        PlayerPrefs.DeleteKey(CompletedKey);
        PlayerPrefs.DeleteKey(CharacterKey);
        PlayerPrefs.Save();
    }
}
