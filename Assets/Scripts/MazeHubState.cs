using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Snapshot of the maze world-map hub so leaving for a battle and returning does not reset
// pig position, eaten treasure, or cleared tutorial lessons.
//
// Single currency: Hub MONEY = CrossLevelStatus.wallet (same pool the store spends).
// Maze eats credit the wallet directly; best margins only decide new-record gains.
// Stored in PlayerPrefs as well as statics — Apply must run AFTER GameManager.Awake (new scene).
public static class MazeHubState
{
    const string PrefsKey = "gm.mazeHub.v1";

    public static bool HasSave { get; private set; }
    public static Vector2 PlayerPos;
    // Legacy field kept in prefs layout for older saves; no longer part of DisplayMoney.
    public static int MazeBank;
    public static int Lives;
    // After returning from a battle, ignore portal triggers until this time (avoids instant re-entry).
    public static float PortalIgnoreUntil;

    public static readonly HashSet<string> EatenTreasureIds = new HashSet<string>();
    public static readonly HashSet<string> ClearedLessons = new HashSet<string>();

    // Spendable wallet (store + hub MONEY). Active may be null on a fresh hub load before Ensure.
    public static int WalletBalance
    {
        get
        {
            if (CrossLevelStatus.Active != null) return CrossLevelStatus.Active.getWallet();
            return PlayerPrefs.GetInt("gm.wallet", 0);
        }
    }

    // What the HUD should show on the world map — same number as the store.
    public static int DisplayMoney => WalletBalance;

    public static bool LessonCleared(string id) => ClearedLessons.Contains(id);

    public static bool TreasureWasEaten(string id)
        => !string.IsNullOrEmpty(id) && EatenTreasureIds.Contains(id);

    public static void MarkLessonCleared(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!ClearedLessons.Add(id)) return;
        WritePrefs();
    }

    // Call as soon as the pig eats maze gold/diamond — do not wait for CaptureFromScene.
    // Capture used to rebuild this set from TreasureById, but Apply removes eaten ids from that
    // dictionary, so a later Capture wiped the list and minerals reappeared after Level 1.
    public static void MarkTreasureEaten(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!EatenTreasureIds.Add(id)) return;
        SnapshotLivesFromGame();
        HasSave = true;
        WritePrefs();
        Debug.Log("MazeHubState: marked eaten " + id + " (total " + EatenTreasureIds.Count + ")");
    }

    public static void MarkTreasuresEaten(IEnumerable<string> ids)
    {
        if (ids == null) return;
        bool any = false;
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (EatenTreasureIds.Add(id)) any = true;
        }
        if (!any) return;
        SnapshotLivesFromGame();
        HasSave = true;
        WritePrefs();
        Debug.Log("MazeHubState: marked eaten batch (total " + EatenTreasureIds.Count + ")");
    }

    // Block-lesson miss: diamond is refunded / respawned, so it must leave the eaten set.
    public static void UnmarkTreasureEaten(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!EatenTreasureIds.Remove(id)) return;
        SnapshotLivesFromGame();
        if (HasSave) WritePrefs();
        Debug.Log("MazeHubState: unmarked eaten " + id + " (total " + EatenTreasureIds.Count + ")");
    }

    static void SnapshotLivesFromGame()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        // Never persist the Brace preview pip — it is re-applied from the armed skill on hub load.
        Lives = Mathf.Max(0, gm.Lives - gm.BonusLives);
    }

    public static void CaptureFromScene()
    {
        PlayerCharacter pig = Object.FindFirstObjectByType<PlayerCharacter>();
        GameManager gm = GameManager.Instance;
        if (pig != null) PlayerPos = pig.transform.position;
        if (gm != null) Lives = Mathf.Max(0, gm.Lives - gm.BonusLives);

        // Never Clear() the eaten set here — merge any treasures already gone from the live maze.
        MazeRuntime maze = MazeRuntime.Active;
        if (maze != null) maze.SyncMissingTreasureAsEaten();

        // Stand slightly south of the portal so restore does not re-fire LevelPortal.
        PlayerPos += Vector2.down * 0.55f;
        PortalIgnoreUntil = Time.unscaledTime + 2.5f;

        HasSave = true;
        WritePrefs();
        Debug.Log("MazeHubState: captured wallet=$" + DisplayMoney
                  + " lives=" + Lives + " eaten=" + EatenTreasureIds.Count + " pos=" + PlayerPos);
    }

    public static void ApplyToScene(MazeRuntime maze, PlayerCharacter pig, GameManager gm)
    {
        ReadPrefsIfNeeded();
        if (!HasSave) return;

        // Fresh grace every time the hub scene loads with a save.
        PortalIgnoreUntil = Time.unscaledTime + 2.5f;

        if (pig != null)
        {
            pig.transform.position = PlayerPos;
            var body = pig.GetComponent<Rigidbody2D>();
            if (body != null) body.position = PlayerPos;
        }

        ApplyDisplayMoney(gm, Lives);

        if (maze == null) return;
        maze.RemoveEatenTreasures();
    }

    // Push wallet onto GameManager so the hub MONEY sidebar matches the store.
    public static void ApplyDisplayMoney(GameManager gm, int lives = 0)
    {
        if (gm == null)
        {
            Debug.LogWarning("MazeHubState: ApplyDisplayMoney skipped — GameManager missing");
            return;
        }
        CrossLevelStatus.Ensure();
        ProgressStore.EnsureSingleWalletMigration();
        int show = DisplayMoney;
        gm.RestoreHubTotals(show, lives);
        Debug.Log("MazeHubState: hub MONEY=$" + show + " (wallet) (gm now $" + gm.PlayerMoney + ")");
    }

    public static void Clear()
    {
        HasSave = false;
        MazeBank = 0;
        Lives = 0;
        PlayerPos = Vector2.zero;
        PortalIgnoreUntil = 0f;
        EatenTreasureIds.Clear();
        ClearedLessons.Clear();
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    // --- prefs (also used by editor smoke test) -------------------------------------------------

    public static string SerializeForTest()
    {
        var sb = new StringBuilder();
        sb.Append(HasSave ? "1" : "0").Append('|');
        sb.Append(MazeBank).Append('|').Append(Lives).Append('|');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(PlayerPos.x.ToString("R", inv)).Append(',').Append(PlayerPos.y.ToString("R", inv)).Append('|');
        sb.Append(string.Join(",", EatenTreasureIds)).Append('|');
        sb.Append(string.Join(",", ClearedLessons));
        return sb.ToString();
    }

    public static bool TryParseForTest(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return false;
        string[] parts = raw.Split('|');
        if (parts.Length < 5) return false;
        if (parts[0] != "1") return false;
        if (!int.TryParse(parts[1], out MazeBank)) return false;
        if (!int.TryParse(parts[2], out Lives)) Lives = 0;
        string[] xy = parts[3].Split(',');
        if (xy.Length == 2
            && float.TryParse(xy[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float px)
            && float.TryParse(xy[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float py))
            PlayerPos = new Vector2(px, py);
        EatenTreasureIds.Clear();
        if (!string.IsNullOrEmpty(parts[4]))
            foreach (string id in parts[4].Split(','))
                if (!string.IsNullOrEmpty(id)) EatenTreasureIds.Add(id);
        ClearedLessons.Clear();
        if (parts.Length > 5 && !string.IsNullOrEmpty(parts[5]))
            foreach (string id in parts[5].Split(','))
                if (!string.IsNullOrEmpty(id)) ClearedLessons.Add(id);
        HasSave = true;
        return true;
    }

    public static void Persist()
    {
        HasSave = true;
        WritePrefs();
    }

    static void WritePrefs()
    {
        PlayerPrefs.SetString(PrefsKey, SerializeForTest());
        PlayerPrefs.Save();
    }

    public static void ReadPrefsIfNeeded()
    {
        if (HasSave) return;
        string raw = PlayerPrefs.GetString(PrefsKey, "");
        if (!TryParseForTest(raw)) return;
    }

    // Force-reload from prefs even if statics are warm (editor tests / reset flows).
    public static void ForceReloadFromPrefs()
    {
        HasSave = false;
        EatenTreasureIds.Clear();
        ClearedLessons.Clear();
        ReadPrefsIfNeeded();
    }
}
