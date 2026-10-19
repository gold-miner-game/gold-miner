using System;
using System.Collections.Generic;
using UnityEngine;

// The one analytics metric the WebGL build records itself: at the end of a round,
// player money minus miner money. Press the Margins button to see the bar chart.
// The same JSON is what tools/analytics/plot_margin.py draws.
public class RoundMarginLog : MonoBehaviour
{
    const string PrefsKey = "gm.analytics.margins";
    const int MaxRounds = 48;

    static RoundMarginLog active;

    readonly List<RoundMargin> rounds = new List<RoundMargin>();
    bool showChart;
    string json = "";
    Texture2D pixel;
    GUIStyle label;
    Vector2 scroll;

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
        var saved = new Saved { rounds = rounds.ToArray() };
        json = JsonUtility.ToJson(saved);
        PlayerPrefs.SetString(PrefsKey, json);
        PlayerPrefs.Save();
    }

    void OnGUI()
    {
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            label.normal.textColor = Color.white;
            pixel = Texture2D.whiteTexture;
        }

        if (GUI.Button(new Rect(8f, 8f, 110f, 28f), showChart ? "Close" : "Margins"))
            showChart = !showChart;
        if (!showChart) return;

        Rect panel = new Rect(8f, 42f, 560f, 360f);
        GUI.Box(panel, "Money margin  (player − miner)");
        if (rounds.Count == 0)
        {
            GUI.Label(new Rect(24f, 80f, 520f, 40f), "No finished rounds yet. Play a level to the end.", label);
            return;
        }

        DrawBars(new Rect(24f, 70f, 520f, 200f));
        if (string.IsNullOrEmpty(json)) Save();
        scroll = GUI.BeginScrollView(new Rect(24f, 280f, 520f, 70f), scroll, new Rect(0f, 0f, 500f, 64f));
        GUI.TextArea(new Rect(0f, 0f, 500f, 64f), json);
        GUI.EndScrollView();
        GUI.Label(new Rect(24f, 352f, 520f, 20f), "Copy the JSON into tools/analytics/rounds.json, then run plot_margin.py", label);
    }

    void DrawBars(Rect area)
    {
        int n = rounds.Count;
        int peak = 1;
        for (int i = 0; i < n; i++)
            peak = Mathf.Max(peak, Mathf.Abs(rounds[i].margin));

        float zeroY = area.y + area.height * 0.5f;
        Fill(new Rect(area.x, zeroY, area.width, 1f), new Color(1f, 1f, 1f, 0.8f));

        float slot = area.width / n;
        float barW = Mathf.Max(4f, slot - 6f);
        for (int i = 0; i < n; i++)
        {
            RoundMargin row = rounds[i];
            float h = area.height * 0.5f * (Mathf.Abs(row.margin) / (float)peak);
            float x = area.x + i * slot + (slot - barW) * 0.5f;
            float y = row.margin >= 0 ? zeroY - h : zeroY;
            Color color = row.playerWon ? new Color(0.35f, 0.75f, 0.4f) : new Color(0.85f, 0.35f, 0.3f);
            Fill(new Rect(x, y, barW, Mathf.Max(1f, h)), color);
            GUI.Label(new Rect(x - 8f, area.yMax - 18f, barW + 16f, 18f), Short(row.level), label);
        }
    }

    void Fill(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = prev;
    }

    static string Short(string level)
    {
        if (string.IsNullOrEmpty(level)) return "?";
        return level.Length <= 8 ? level : level.Substring(level.Length - 8);
    }
}
