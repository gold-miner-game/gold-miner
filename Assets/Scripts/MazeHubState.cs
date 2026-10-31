using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Snapshot of the maze world-map hub so leaving for a battle and returning does not reset
// maze-collected money, pig position, eaten treasure, or cleared tutorial lessons.
//
// World-map MONEY shown to the player =
//   MazeBank  (gold/diamonds eaten on the maze before battles)
// + ProgressStore.MapMoney  (sum of each battle's best player−miner margin)
//
// Stored in PlayerPrefs as well as statics — Apply must run AFTER GameManager.Awake (new scene).
public static class MazeHubState
{
    const string PrefsKey = "gm.mazeHub.v1";

    public static bool HasSave { get; private set; }
    public static Vector2 PlayerPos;
    // Maze-only bank (not battle scores). Prefs still serialize this as the money field.
    public static int MazeBank;
    public static int Lives;
    // After returning from a battle, ignore portal triggers until this time (avoids instant re-entry).
    public static float PortalIgnoreUntil;

    public static readonly HashSet<string> EatenTreasureIds = new HashSet<string>();
    public static readonly HashSet<string> ClearedLessons = new HashSet<string>();

    // What the HUD / gate should show on the world map.
    public static int DisplayMoney => Mathf.Max(0, MazeBank) + Mathf.Max(0, ProgressStore.MapMoney);

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
        SnapshotBankFromGame();
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
        SnapshotBankFromGame();
        HasSave = true;
        WritePrefs();
        Debug.Log("MazeHubState: marked eaten batch (total " + EatenTreasureIds.Count + ")");
    }

    // Block-lesson miss: diamond is refunded / respawned, so it must leave the eaten set.
    public static void UnmarkTreasureEaten(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!EatenTreasureIds.Remove(id)) return;
        SnapshotBankFromGame();
        if (HasSave) WritePrefs();
        Debug.Log("MazeHubState: unmarked eaten " + id + " (total " + EatenTreasureIds.Count + ")");
    }

    // GM on the hub holds DisplayMoney; strip battle margins to keep MazeBank pure.
    static void SnapshotBankFromGame()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        MazeBank = Mathf.Max(0, gm.PlayerMoney - ProgressStore.MapMoney);
        Lives = gm.Lives;
    }

    public static void CaptureFromScene()
    {
        PlayerCharacter pig = Object.FindFirstObjectByType<PlayerCharacter>();
        GameManager gm = GameManager.Instance;
        if (pig != null) PlayerPos = pig.transform.position;
        if (gm != null)
        {
            MazeBank = Mathf.Max(0, gm.PlayerMoney - ProgressStore.MapMoney);
            Lives = gm.Lives;
        }

        // Never Clear() the eaten set here — merge any treasures already gone from the live maze.
        MazeRuntime maze = MazeRuntime.Active;
        if (maze != null) maze.SyncMissingTreasureAsEaten();

        // Stand slightly south of the portal so restore does not re-fire LevelPortal.
        PlayerPos += Vector2.down * 0.55f;
        PortalIgnoreUntil = Time.unscaledTime + 2.5f;

        HasSave = true;
        WritePrefs();
        Debug.Log("MazeHubState: captured bank=$" + MazeBank + " display=$" + DisplayMoney
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

        if (gm != null)
        {
            int show = DisplayMoney;
            gm.RestoreHubTotals(show, Lives);
            Debug.Log("MazeHubState: restored display=$" + show
                      + " (bank=$" + MazeBank + " + margins=$" + ProgressStore.MapMoney + ")"
                      + " lives=" + Lives + " eaten=" + EatenTreasureIds.Count
                      + " (gm now $" + gm.PlayerMoney + ")");
        }
        else
        {
            Debug.LogWarning("MazeHubState: Apply skipped money — GameManager missing");
        }

        if (maze == null) return;
        maze.RemoveEatenTreasures();
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
