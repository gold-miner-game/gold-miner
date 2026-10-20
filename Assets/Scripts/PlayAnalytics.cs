using System;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using UnityEngine.Networking;

// Posts one row per analytics event to a Google Form, which lands in the form's
// response sheet. The same rows are kept on this machine so a chart can be drawn
// before the form link is pasted in.
//
// Create one Google Form with these short-answer questions, in any order:
//   player, session, level, character, event, value,
//   player_money, miner_money, won, end_reason, tutorial, source
// Send a prefilled link, copy the formResponse URL (replace viewform with
// formResponse) into FormUrl, and copy each entry.123 id into the matching field.
// Leave FormUrl empty until then: rows stay on this machine only.
public class PlayAnalytics : MonoBehaviour
{
    const string FormUrl = "https://docs.google.com/forms/d/e/1FAIpQLScni4MVpfEp4GmBCakOVkCE1INzRET47V7q5TsRkPgEQ7wsnw/formResponse";
    const string EPlayer = "entry.1577535456";
    const string ESession = "entry.1364306836";
    const string ELevel = "entry.1230225568";
    const string ECharacter = "entry.1310891982";
    const string EEvent = "entry.444828278";
    const string EValue = "entry.922649508";
    const string EPlayerMoney = "entry.1150469092";
    const string EMinerMoney = "entry.1893654756";
    const string EWon = "entry.410627730";
    const string EEndReason = "entry.1647763650";
    const string ETutorial = "entry.2094911153";
    const string ESource = "entry.1166496251";

    const string PlayerKey = "gm.analytics.player";
    const string PrefsKey = "gm.analytics.events";
    const int MaxEvents = 200;

    static PlayAnalytics active;

    readonly List<EventRow> events = new List<EventRow>();
    readonly HashSet<int> touching = new HashSet<int>();
    readonly List<UnityWebRequest> pending = new List<UnityWebRequest>();
    string playerId = "";
    string sessionId = "";
    float levelStart;
    bool sawGold;

    [Serializable]
    public class EventRow
    {
        public string player;
        public string session;
        public string level;
        public string character;
        public string eventName;
        public float value;
        public int playerMoney;
        public int minerMoney;
        public string won;
        public string endReason;
        public string tutorial;
        public string source;
    }

    [Serializable]
    class Saved
    {
        public EventRow[] events;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void AnalyticsPostForm(string url, string body);
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Ensure();
    }

    public static void BeginLevel()
    {
        PlayAnalytics log = Ensure();
        log.sawGold = false;
        log.touching.Clear();
        log.levelStart = Time.time;
        log.Send("level_start", 0f, 0, 0, "", "");
    }

    public static void FirstGold(TreasureType type)
    {
        if (type == TreasureType.Diamond) return;
        PlayAnalytics log = Ensure();
        if (log.sawGold) return;
        log.sawGold = true;
        log.Send("first_gold", Mathf.Max(0f, Time.time - log.levelStart), 0, 0, "", "");
    }

    // One count per contact while the character cannot eat. Repeat calls for the
    // same treasure are ignored until it leaves, or until the digest ends.
    public static void DigestContact(UnityEngine.Object key, PlayerCharacter player)
    {
        if (key == null || player == null) return;
        PlayAnalytics log = Ensure();
        int id = key.GetInstanceID();
        if (player.CanEat)
        {
            log.touching.Remove(id);
            return;
        }
        if (!log.touching.Add(id)) return;
        log.Send("digest_touch", 1f, 0, 0, "", "");
    }

    public static void DigestRelease(UnityEngine.Object key)
    {
        if (key == null || active == null) return;
        active.touching.Remove(key.GetInstanceID());
    }

    public static void Steal()
    {
        Ensure().Send("steal", 1f, 0, 0, "", "");
    }

    // A hook hit that costs a Life, including the hit that ends the round.
    public static void Death()
    {
        Ensure().Send("death", 1f, 0, 0, "", "");
    }

    public static void CharacterPicked(int index)
    {
        Ensure().Send("character_pick", 1f, 0, 0, "", "", "select", false, index.ToString());
    }

    public static void Purchase(string itemId)
    {
        Ensure().Send("purchase", 1f, 0, 0, "", itemId ?? "", "store", false, null);
    }

    public static void RoundEnd(string level, int playerMoney, int minerMoney, int margin, bool won, string reason, bool tutorial)
    {
        PlayAnalytics log = Ensure();
        log.Send("round_end", margin, playerMoney, minerMoney, won ? "1" : "0", reason ?? "", level, tutorial);
    }

    static PlayAnalytics Ensure()
    {
        if (active != null) return active;
        var go = new GameObject("PlayAnalytics");
        DontDestroyOnLoad(go);
        return go.AddComponent<PlayAnalytics>();
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
        playerId = PlayerPrefs.GetString(PlayerKey, "");
        if (string.IsNullOrEmpty(playerId))
        {
            playerId = Guid.NewGuid().ToString("N").Substring(0, 12);
            PlayerPrefs.SetString(PlayerKey, playerId);
            PlayerPrefs.Save();
        }
        sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
        Load();
    }

    void OnDestroy()
    {
        if (active == this) active = null;
    }

    void Update()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (!pending[i].isDone) continue;
            pending[i].Dispose();
            pending.RemoveAt(i);
        }
    }

    void Send(string eventName, float value, int playerMoney, int minerMoney, string won, string endReason)
    {
        Send(eventName, value, playerMoney, minerMoney, won, endReason, null, null);
    }

    void Send(string eventName, float value, int playerMoney, int minerMoney, string won, string endReason, string level, bool? tutorial, string characterOverride = null)
    {
        bool isTutorial = tutorial ?? LevelLoader.Tutorial;
        var row = new EventRow
        {
            player = playerId,
            session = sessionId,
            level = string.IsNullOrEmpty(level) ? LevelName() : level,
            character = string.IsNullOrEmpty(characterOverride) ? CharacterIndex() : characterOverride,
            eventName = eventName,
            value = value,
            playerMoney = playerMoney,
            minerMoney = minerMoney,
            won = won ?? "",
            endReason = endReason ?? "",
            tutorial = isTutorial ? "1" : "0",
            source = Source(),
        };
        events.Add(row);
        if (events.Count > MaxEvents) events.RemoveAt(0);
        Save();
        Post(row);
    }

    static string LevelName()
    {
        LevelLoader loader = FindFirstObjectByType<LevelLoader>();
        return loader != null ? loader.LevelName() : "level";
    }

    static string CharacterIndex()
    {
        if (LevelLoader.Tutorial) return "0";
        int index = ChampionSpawner.Chosen;
        if (index < 0 && ProgressStore.HasPickedCharacter
            && int.TryParse(ProgressStore.SelectedCharacter, out int picked))
            index = picked;
        return index < 0 ? "0" : index.ToString();
    }

    static string Source()
    {
#if UNITY_EDITOR
        return "editor";
#elif UNITY_WEBGL
        return "webgl";
#else
        return "player";
#endif
    }

    void Load()
    {
        events.Clear();
        string raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        Saved saved = JsonUtility.FromJson<Saved>(raw);
        if (saved == null || saved.events == null) return;
        events.AddRange(saved.events);
    }

    void Save()
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Saved { events = events.ToArray() }));
        PlayerPrefs.Save();
    }

    static bool FormReady()
    {
        return FormUrl.Length > 0
            && EPlayer.Length > 0 && ESession.Length > 0 && ELevel.Length > 0
            && ECharacter.Length > 0 && EEvent.Length > 0 && EValue.Length > 0
            && EPlayerMoney.Length > 0 && EMinerMoney.Length > 0 && EWon.Length > 0
            && EEndReason.Length > 0 && ETutorial.Length > 0 && ESource.Length > 0;
    }

    void Post(EventRow row)
    {
        if (!FormReady()) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        string body =
            Field(EPlayer, row.player)
            + "&" + Field(ESession, row.session)
            + "&" + Field(ELevel, row.level)
            + "&" + Field(ECharacter, row.character)
            + "&" + Field(EEvent, row.eventName)
            + "&" + Field(EValue, row.value.ToString("0.###"))
            + "&" + Field(EPlayerMoney, row.playerMoney.ToString())
            + "&" + Field(EMinerMoney, row.minerMoney.ToString())
            + "&" + Field(EWon, row.won)
            + "&" + Field(EEndReason, row.endReason)
            + "&" + Field(ETutorial, row.tutorial)
            + "&" + Field(ESource, row.source);
        AnalyticsPostForm(FormUrl, body);
#else
        var form = new WWWForm();
        form.AddField(EPlayer, row.player);
        form.AddField(ESession, row.session);
        form.AddField(ELevel, row.level);
        form.AddField(ECharacter, row.character);
        form.AddField(EEvent, row.eventName);
        form.AddField(EValue, row.value.ToString("0.###"));
        form.AddField(EPlayerMoney, row.playerMoney.ToString());
        form.AddField(EMinerMoney, row.minerMoney.ToString());
        form.AddField(EWon, row.won);
        form.AddField(EEndReason, row.endReason);
        form.AddField(ETutorial, row.tutorial);
        form.AddField(ESource, row.source);
        UnityWebRequest request = UnityWebRequest.Post(FormUrl, form);
        request.SendWebRequest();
        pending.Add(request);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static string Field(string entry, string value)
    {
        return UnityWebRequest.EscapeURL(entry) + "=" + UnityWebRequest.EscapeURL(value ?? "");
    }
#endif
}
