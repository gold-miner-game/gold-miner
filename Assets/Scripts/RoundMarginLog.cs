using System;
using System.Collections.Generic;
using UnityEngine;

// Records the money margin when a round ends and forwards it to the sheet.
// Charts are drawn from that sheet by tools/analytics/plot_metrics.py.
public class RoundMarginLog : MonoBehaviour
{
    const string PrefsKey = "gm.analytics.margins";
    const int MaxRounds = 48;

    static RoundMarginLog active;

    readonly List<RoundMargin> rounds = new List<RoundMargin>();

    [Serializable]
    public class RoundMargin
    {
        public string level;
        public int playerMoney;
        public int minerMoney;
        public int margin;
        public bool playerWon;
        public string endReason;
        public bool tutorial;
    }

    [Serializable]
    class Saved
    {
        public RoundMargin[] rounds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Ensure();
    }

    public static void Record(GameManager gm)
    {
        if (gm == null) return;
        Ensure().Add(gm);
    }

    static RoundMarginLog Ensure()
    {
        if (active != null) return active;
        var go = new GameObject("RoundMarginLog");
        DontDestroyOnLoad(go);
        return go.AddComponent<RoundMarginLog>();
    }

    void Awake()
    {
        if (active != null && active != this)
        {
            Destroy(gameObject);
            return;
        }
        active = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    void OnDestroy()
    {
        if (active == this) active = null;
    }

    void Add(GameManager gm)
    {
        LevelLoader loader = FindFirstObjectByType<LevelLoader>();
        var row = new RoundMargin
        {
            level = loader != null ? loader.LevelName() : "level",
            playerMoney = gm.PlayerMoney,
            minerMoney = gm.MinerMoney,
            margin = gm.PlayerMoney - gm.MinerMoney,
            playerWon = gm.PlayerWon,
            endReason = gm.EndReason,
            tutorial = LevelLoader.Tutorial,
        };
        rounds.Add(row);
        if (rounds.Count > MaxRounds) rounds.RemoveAt(0);
        Save();
        PlayAnalytics.RoundEnd(row.level, row.playerMoney, row.minerMoney, row.margin, row.playerWon, row.endReason, row.tutorial);
    }

    void Load()
    {
        rounds.Clear();
        string raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        Saved saved = JsonUtility.FromJson<Saved>(raw);
        if (saved == null || saved.rounds == null) return;
        rounds.AddRange(saved.rounds);
    }

    void Save()
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Saved { rounds = rounds.ToArray() }));
        PlayerPrefs.Save();
    }
}
