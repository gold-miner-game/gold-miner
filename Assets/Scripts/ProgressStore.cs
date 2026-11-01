using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Meta progression that survives app restarts (PlayerPrefs): which map nodes are cleared and the
// chosen character id. Kept apart from CrossLevelStatus, which is the live, one-run player record.
public static class ProgressStore
{
    const string CompletedKey = "gm.progress.completed";   // ';'-separated node ids
    const string CharacterKey = "gm.progress.character";
    const string RevealedKey = "gm.progress.revealed";    // ';'-separated ids of hidden hub portals already shown

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

    // A hidden hub portal (HubPortal) turns from rock into portal once; after that it stays a portal
    public static bool WasRevealed(string nodeId)
    {
        foreach (string id in PlayerPrefs.GetString(RevealedKey, "").Split(';'))
            if (id == nodeId) return true;
        return false;
    }

    public static void MarkRevealed(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || WasRevealed(nodeId)) return;
        string raw = PlayerPrefs.GetString(RevealedKey, "");
        PlayerPrefs.SetString(RevealedKey, raw == "" ? nodeId : raw + ";" + nodeId);
        PlayerPrefs.Save();
    }

    // The tutorial run (WorldMap.tutorialLevels) is saved under this id once it is finished.
    public const string TutorialDoneId = "tutorial";
    // tut_block = the last tutorial node of the old map, so older saves skip the run too
    public static bool TutorialDone => IsCompleted(TutorialDoneId) || IsCompleted("tut_block");

    public static bool HasPickedCharacter => PlayerPrefs.HasKey(CharacterKey);

    // Dev helper: wipe all meta progress (also clear the in-memory cache).
    public static void ResetAll()
    {
        completed = null;
        PlayerPrefs.DeleteKey(CompletedKey);
        PlayerPrefs.DeleteKey(CharacterKey);
        PlayerPrefs.DeleteKey(RevealedKey);
        PlayerPrefs.Save();
        CrossLevelStatus.ClearSavedWallet();
    }
}
