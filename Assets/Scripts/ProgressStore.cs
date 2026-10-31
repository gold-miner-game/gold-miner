using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Meta progression that survives app restarts (PlayerPrefs): cleared nodes, chosen character,
// and world-map unlock money (sum of each battle's best win margin: player − miner).
public static class ProgressStore
{
    const string CompletedKey = "gm.progress.completed";   // ';'-separated node ids
    const string CharacterKey = "gm.progress.character";
    const string MarginsKey = "gm.progress.bestMargins"; // "id:margin;id:margin"

    static HashSet<string> completed;
    static Dictionary<string, int> bestMargins;

    static HashSet<string> Completed()
    {
        if (completed != null) return completed;
        completed = new HashSet<string>();
        string raw = PlayerPrefs.GetString(CompletedKey, "");
        foreach (string id in raw.Split(';'))
            if (!string.IsNullOrEmpty(id)) completed.Add(id);
        return completed;
    }

    static Dictionary<string, int> BestMargins()
    {
        if (bestMargins != null) return bestMargins;
        bestMargins = new Dictionary<string, int>();
        string raw = PlayerPrefs.GetString(MarginsKey, "");
        foreach (string part in raw.Split(';'))
        {
            if (string.IsNullOrEmpty(part)) continue;
            int colon = part.IndexOf(':');
            if (colon <= 0) continue;
            string id = part.Substring(0, colon);
            if (int.TryParse(part.Substring(colon + 1), out int margin))
                bestMargins[id] = margin;
        }
        return bestMargins;
    }

    static void SaveMargins()
    {
        var parts = new List<string>();
        foreach (var kv in BestMargins())
            parts.Add(kv.Key + ":" + kv.Value);
        PlayerPrefs.SetString(MarginsKey, string.Join(";", parts));
        PlayerPrefs.Save();
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

    // Best (player − miner) margin ever recorded for this battle. Used for world-map unlock money.
    public static int GetBestMargin(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return 0;
        return BestMargins().TryGetValue(levelId, out int m) ? m : 0;
    }

    // Keep only the historical max per level. Map money rises by the improvement, never falls on unlock.
    public static void RecordBestMargin(string levelId, int margin)
    {
        if (string.IsNullOrEmpty(levelId) || margin <= 0) return;
        int prev = GetBestMargin(levelId);
        if (margin <= prev) return;
        BestMargins()[levelId] = margin;
        SaveMargins();
    }

    // Sum of each battle node's best win margin — unlocks later world-map levels (not spent).
    // Prefer battle_* keys so older saves that also stored level-asset names do not double-count.
    public static int MapMoney
    {
        get
        {
            int sum = 0;
            bool anyBattle = false;
            foreach (var kv in BestMargins())
            {
                if (kv.Value <= 0) continue;
                if (!kv.Key.StartsWith("battle_")) continue;
                anyBattle = true;
                sum += kv.Value;
            }
            if (anyBattle) return sum;
            foreach (var kv in BestMargins())
                if (kv.Value > 0) sum += kv.Value;
            return sum;
        }
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
        bestMargins = null;
        PlayerPrefs.DeleteKey(CompletedKey);
        PlayerPrefs.DeleteKey(CharacterKey);
        PlayerPrefs.DeleteKey(MarginsKey);
        PlayerPrefs.Save();
        CrossLevelStatus.ClearSavedWallet();
        MazeHubState.Clear();
    }
}
