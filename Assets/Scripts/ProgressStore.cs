using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Meta progression that survives app restarts (PlayerPrefs): cleared nodes, chosen character,
// and per-battle best win margins (used only to credit wallet on a new record — not a second currency).
public static class ProgressStore
{
    const string CompletedKey = "gm.progress.completed";   // ';'-separated node ids
    const string CharacterKey = "gm.progress.character";
    const string RevealedKey = "gm.progress.revealed";    // ';'-separated ids of hidden hub portals already shown
    const string MarginsKey = "gm.progress.bestMargins"; // "id:margin;id:margin"
    const string MapGoldKey = "gm.progress.mapGold";     // ';'-separated ids of the world map's one-time gold already eaten

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

    // Best (player − miner) margin ever recorded for this battle. Used only for new-record credits.
    public static int GetBestMargin(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return 0;
        return BestMargins().TryGetValue(levelId, out int m) ? m : 0;
    }

    // Keep only the historical max per level. Does not touch the wallet.
    public static void RecordBestMargin(string levelId, int margin)
    {
        if (string.IsNullOrEmpty(levelId) || margin <= 0) return;
        int prev = GetBestMargin(levelId);
        if (margin <= prev) return;
        BestMargins()[levelId] = margin;
        SaveMargins();
    }

    // One-shot: dual-ledger saves often kept high best-margins with wallet still $0. Seed the
    // wallet from those bests once so Basic / Level 1 wins are not stuck at gain=0 forever.
    const string SingleWalletMigratedKey = "gm.progress.singleWalletMigrated";

    public static void EnsureSingleWalletMigration()
    {
        if (PlayerPrefs.HasKey(SingleWalletMigratedKey)) return;
        PlayerPrefs.SetInt(SingleWalletMigratedKey, 1);

        int sum = 0;
        foreach (var kv in BestMargins())
            if (kv.Value > 0) sum += kv.Value;

        // Only repair the classic "map showed $X / store $0" case — never top up a spent wallet.
        CrossLevelStatus record = CrossLevelStatus.Ensure();
        if (sum > 0 && record.getWallet() == 0)
            record.addWallet(sum);

        PlayerPrefs.DeleteKey("gm.progress.marginsCredited");
        PlayerPrefs.Save();
    }

    // If margin beats the saved best for this battle, raise the best and credit the wallet by the
    // improvement. Returns the $ added (0 if no new record). Never credits a non-positive margin.
    // Idempotent for the same margin.
    public static int TryCreditBestMargin(string levelId, int margin)
    {
        EnsureSingleWalletMigration();
        if (string.IsNullOrEmpty(levelId) || margin <= 0) return 0;
        int prev = Mathf.Max(0, GetBestMargin(levelId));
        int gain = margin - prev;
        if (gain <= 0) return 0;
        BestMargins()[levelId] = margin;
        SaveMargins();
        CrossLevelStatus.Ensure().addWallet(gain);
        return gain;
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

    // The world map's one-time gold (MapGold): once eaten it never comes back
    public static bool MapGoldTaken(string goldId)
    {
        foreach (string id in PlayerPrefs.GetString(MapGoldKey, "").Split(';'))
            if (id == goldId) return true;
        return false;
    }

    public static void MarkMapGoldTaken(string goldId)
    {
        if (string.IsNullOrEmpty(goldId) || MapGoldTaken(goldId)) return;
        string raw = PlayerPrefs.GetString(MapGoldKey, "");
        PlayerPrefs.SetString(MapGoldKey, raw == "" ? goldId : raw + ";" + goldId);
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
        bestMargins = null;
        PlayerPrefs.DeleteKey(CompletedKey);
        PlayerPrefs.DeleteKey(CharacterKey);
        PlayerPrefs.DeleteKey(RevealedKey);
        PlayerPrefs.DeleteKey(MarginsKey);
        PlayerPrefs.DeleteKey(MapGoldKey);
        PlayerPrefs.DeleteKey(MapHub.HpKey);
        PlayerPrefs.DeleteKey("gm.progress.marginsCredited"); // legacy dual-ledger key
        PlayerPrefs.DeleteKey(SingleWalletMigratedKey);
        PlayerPrefs.Save();
        // The stories and steps that play only the first time (TutorialStep Only First Time) play again
        TutorialStep.ResetSeen();
        CrossLevelStatus.ClearSavedWallet();
        MazeHubState.Clear();
    }
}
