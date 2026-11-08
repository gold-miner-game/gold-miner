using System;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

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
    // Optional columns: seq, t, attempt, build. Add these four short-answer questions to the form and
    // paste their entry ids here. Until then the rows still post without them.
    const string ESeq = "";
    const string ET = "";
    const string EAttempt = "";
    const string EBuild = "";

    const string Build = "beta-1110";
    const float HeartbeatSeconds = 30f;
    const float StuckSeconds = 15f;
    const float UnderstoodSeconds = 8f;   // a tutorial action done slower than this was not understood

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

    // Traceability: rows in order, seconds since the session began, and which try at this level
    int rowCount;
    float sessionStart;
    float nextHeartbeat;
    readonly Dictionary<string, int> attempts = new Dictionary<string, int>();

    // Stuck: no eat and no block for StuckSeconds while a round plays
    PlayerCharacter watched;
    int progressMark = -1;
    float progressSince;
    Vector2 windowStart;

    // This round so far: every hook's outcome ("yellow:banked"), skill presses, the store items in play,
    // and treasure the player knocked off a hook (instance id -> when)
    readonly SortedDictionary<string, int> hookResults = new SortedDictionary<string, int>();
    readonly Dictionary<int, float> freedAt = new Dictionary<int, float>();
    readonly List<string> freedEaten = new List<string>();
    PlayerCharacter roundPlayer;
    int skillUses;
    bool skillWasOn;
    string itemsInPlay;
    int shieldHits;
    bool braceSpent;

    // Store: what happened since the last battle, and how the last lost round was lost
    string storeChance;
    string lastLoss = "none";
    int storeAffordable;
    readonly List<string> storeBought = new List<string>();
    readonly HashSet<string> storeHovered = new HashSet<string>();

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
        public int seq;
        public float t;
        public int attempt;
        public string build;
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
        log.watched = null;
        log.hookResults.Clear();
        log.freedAt.Clear();
        log.freedEaten.Clear();
        log.roundPlayer = null;
        log.skillUses = 0;
        log.skillWasOn = false;
        log.itemsInPlay = null;
        log.shieldHits = 0;
        log.braceSpent = false;
        string level = LevelName();
        log.attempts[level] = log.attempts.TryGetValue(level, out int n) ? n + 1 : 1;
        log.Send("level_start", 0f, 0, 0, "", "");
    }

    // Tutorial: the time from a prompt appearing to the player doing it. value = seconds,
    // won = 1 when done within UnderstoodSeconds, end_reason = the step
    public static void TutorialAction(string step, float seconds, bool done)
    {
        string understood = done && seconds <= UnderstoodSeconds ? "1" : "0";
        Ensure().Send("tutorial_action", seconds, 0, 0, understood, step, null, true);
    }

    // Every hook, yellow (aimed at treasure) or red (aimed at the player). Counted through the round
    // and sent as hook_result rows when it ends. Outcomes: hit, blocked, cut, banked, empty, dodge
    public static void HookShot(bool red, string outcome)
    {
        if (!Counting()) return;
        string key = (red ? "red:" : "yellow:") + outcome;
        PlayAnalytics log = Ensure();
        log.hookResults[key] = log.hookResults.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    // The player knocked this treasure off a hook (cut the rope or blocked the claw).
    // value = seconds the hook had held it, end_reason = its type
    public static void ItemCut(Treasure item, float heldSeconds)
    {
        if (item == null || !Counting()) return;
        PlayAnalytics log = Ensure();
        log.freedAt[item.GetInstanceID()] = Time.time;
        log.Send("item_cut", heldSeconds, 0, 0, "", item.type.ToString());
    }

    // The player ate a treasure. won = freed (it came off a hook first) or ground.
    // value = seconds from the cut to the eat, 0 for ground
    public static void ItemEaten(Treasure item)
    {
        if (item == null || !Counting()) return;
        PlayAnalytics log = Ensure();
        bool freed = log.freedAt.TryGetValue(item.GetInstanceID(), out float cutAt);
        if (freed) log.freedEaten.Add(item.type.ToString());
        log.Send("item_eat", freed ? Time.time - cutAt : 0f, 0, 0, freed ? "freed" : "ground", item.type.ToString());
    }

    // A miner's hook brought a diamond or a large gold home
    public static void ItemBanked(Treasure item)
    {
        if (item == null || !Counting()) return;
        if (item.type != TreasureType.Diamond && item.type != TreasureType.LargeGold) return;
        Ensure().Send("item_banked", item.value, 0, 0, "", item.type.ToString());
    }

    // Rounds count; the scripted tutorial does not
    static bool Counting()
    {
        return GameManager.Instance != null && GameManager.Instance.IsPlaying && !Level1Tutorial.Running;
    }

    // A map portal was entered (Level 1, Store, Pick Character, ...)
    public static void NodeEnter(string nodeId)
    {
        Ensure().Send("node_enter", 1f, 0, 0, "", nodeId);
    }

    // The player walked into a locked portal. value = best margins counted toward its gold gate
    public static void LockedPortal(string nodeId, int goldToward)
    {
        Ensure().Send("portal_locked", goldToward, 0, 0, "", nodeId);
    }

    // Does the player understand the store? Four questions:
    //   1. Do they go when they can pay?  store_chance, sent as each battle starts:
    //      end_reason = bought / looked_only / skipped (could pay, never went) / cant_afford
    //   2. Do they buy what fixes their last loss?  store_visit, sent on leaving:
    //      end_reason = "<item> <fit|miss|no_loss> <read|unread>" per item ("none" if nothing),
    //      read: they hovered it and saw what it does before buying,
    //      won = last loss (killed / outearned / none), value = items they could pay for
    //      fit: killed -> shield or brace; outearned -> swift or ration
    //   3. Does the item do anything?  item_effect, per item in play when a round ends:
    //      value = shield: hits it soaked; brace: 1 if its extra life was spent; swift/ration: treasure eaten
    //   4. Does it help them win?  round_items (above): margin and won with vs without items
    public static void StoreOpen(int affordable)
    {
        PlayAnalytics log = Ensure();
        log.storeAffordable = affordable;
        log.storeBought.Clear();
        log.storeHovered.Clear();
        if (log.storeChance != "bought") log.storeChance = "looked_only";
    }

    public static void StoreHover(string itemId)
    {
        Ensure().storeHovered.Add(itemId);
    }

    public static void Purchase(string itemId)
    {
        PlayAnalytics log = Ensure();
        log.storeBought.Add(itemId ?? "");
        log.storeChance = "bought";
    }

    public static void StoreLeave()
    {
        PlayAnalytics log = Ensure();
        var parts = new List<string>();
        foreach (string item in log.storeBought)
            parts.Add(item + " " + Fit(item, log.lastLoss) + (log.storeHovered.Contains(item) ? " read" : " unread"));
        log.Send("store_visit", log.storeAffordable, 0, 0, log.lastLoss,
            parts.Count > 0 ? string.Join(",", parts) : "none", "store", false);
    }

    // A battle is starting. affordable = shelf items the wallet pays for right now
    public static void StoreChance(string nodeId, int affordable)
    {
        PlayAnalytics log = Ensure();
        string outcome = log.storeChance ?? (affordable > 0 ? "skipped" : "cant_afford");
        log.Send("store_chance", affordable, 0, 0, log.lastLoss, outcome, nodeId, false);
        log.storeChance = null;
    }

    static string Fit(string item, string loss)
    {
        if (loss == "killed") return item == "shield" || item == "brace" ? "fit" : "miss";
        if (loss == "outearned") return item == "swift" || item == "ration" ? "fit" : "miss";
        return "no_loss";
    }

    public static void ShieldSoaked() { if (Counting()) Ensure().shieldHits++; }
    public static void BraceSpent() { if (Counting()) Ensure().braceSpent = true; }

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

    // value = seconds spent on the pick screen before choosing
    public static void CharacterPicked(int index, float seconds = 0f)
    {
        Ensure().Send("character_pick", seconds, 0, 0, "", "", "select", false, index.ToString());
    }

    // round_end, then this round's summaries, all with the round's won flag so they split by outcome:
    //   round_items  value = margin, end_reason = store items in play ("swift,brace" or "none")
    //   skill_use    value = skill presses, player_money = hooks blocked + treasure knocked off,
    //                miner_money = treasure eaten
    //   level_plan   value = treasure eaten after cutting it off a hook, end_reason = which ones
    //   hook_result  one row per hook color and outcome, value = count, end_reason = "red:hit"
    public static void RoundEnd(string level, int playerMoney, int minerMoney, int margin, bool won, string reason, bool tutorial)
    {
        PlayAnalytics log = Ensure();
        string w = won ? "1" : "0";
        log.Send("round_end", margin, playerMoney, minerMoney, w, reason ?? "", level, tutorial);
        if (tutorial) return;

        log.lastLoss = won ? "none"
            : GameManager.Instance != null && GameManager.Instance.Lives <= 0 ? "killed" : "outearned";
        string items = log.itemsInPlay ?? "none";
        log.Send("round_items", margin, playerMoney, minerMoney, w, items, level, false);
        if (items != "none")
            foreach (string item in items.Split(','))
            {
                float effect = item == "shield" ? log.shieldHits
                    : item == "brace" ? (log.braceSpent ? 1f : 0f)
                    : log.roundPlayer != null ? log.roundPlayer.Eaten : 0f;
                log.Send("item_effect", effect, playerMoney, minerMoney, w, item, level, false);
            }
        PlayerCharacter player = log.roundPlayer;
        if (player != null)
            log.Send("skill_use", log.skillUses, player.HooksBlocked + player.TreasureStolen, player.Eaten, w,
                player.GetType().Name, level, false);
        log.Send("level_plan", log.freedEaten.Count, playerMoney, minerMoney, w,
            log.freedEaten.Count > 0 ? string.Join(",", log.freedEaten) : "none", level, false);
        foreach (KeyValuePair<string, int> result in log.hookResults)
            log.Send("hook_result", result.Value, 0, 0, w, result.Key, level, false);
        log.hookResults.Clear();
        log.freedEaten.Clear();
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
        sessionStart = Time.unscaledTime;
        nextHeartbeat = sessionStart + HeartbeatSeconds;
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

        // The last heartbeat a session sends is where that player left
        if (Time.unscaledTime >= nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;
            Send("heartbeat", Time.unscaledTime - sessionStart, 0, 0, "", SceneManager.GetActiveScene().name);
        }
        WatchForStuck();
    }

    // Wandering or stuck: a round is playing, but nothing has been eaten or blocked for a while.
    // value = how far the character moved in that window (low = standing still, high = walking around)
    void WatchForStuck()
    {
        GameManager gm = GameManager.Instance;
        bool round = gm != null && gm.IsPlaying && !Level1Tutorial.Running
                     && Time.timeScale > 0f && !TutorialDialogue.Playing;
        PlayerCharacter player = !round ? null : watched != null ? watched : FindFirstObjectByType<PlayerCharacter>();
        bool live = round && player != null;
        if (!live)
        {
            watched = null;
            progressMark = -1;
            return;
        }
        if (roundPlayer != player)
        {
            roundPlayer = player;
            skillWasOn = false;
        }
        if (itemsInPlay == null) itemsInPlay = ItemsInPlay();
        bool skillOn = player.SkillActive;
        if (skillOn && !skillWasOn) skillUses++;
        skillWasOn = skillOn;

        int mark = player.Eaten + player.HooksBlocked + player.TreasureStolen;
        if (player != watched || mark != progressMark)
        {
            watched = player;
            progressMark = mark;
            progressSince = Time.unscaledTime;
            windowStart = player.transform.position;
            return;
        }
        if (Time.unscaledTime - progressSince < StuckSeconds) return;
        float moved = Vector2.Distance(windowStart, player.transform.position);
        Send("stuck", moved, gm.PlayerMoney, gm.MinerMoney, "", "");
        progressSince = Time.unscaledTime;
        windowStart = player.transform.position;
    }

    // Store items working in this round, read on its first playing frame
    static string ItemsInPlay()
    {
        CrossLevelStatus status = CrossLevelStatus.Active;
        if (status == null) return "none";
        var ids = new List<string>();
        foreach (PlayerSkill skill in status.getSkills())
            if (CrossLevelStatus.IsStoreEffect(skill.id) && (skill.active || skill.secondsLeft > 0f))
                ids.Add(skill.id);
        return ids.Count > 0 ? string.Join(",", ids) : "none";
    }

    void Send(string eventName, float value, int playerMoney, int minerMoney, string won, string endReason)
    {
        Send(eventName, value, playerMoney, minerMoney, won, endReason, null, null);
    }

    void Send(string eventName, float value, int playerMoney, int minerMoney, string won, string endReason, string level, bool? tutorial, string characterOverride = null)
    {
        bool isTutorial = tutorial ?? LevelLoader.Tutorial;
        string levelName = string.IsNullOrEmpty(level) ? LevelName() : level;
        var row = new EventRow
        {
            player = playerId,
            session = sessionId,
            level = levelName,
            character = string.IsNullOrEmpty(characterOverride) ? CharacterIndex() : characterOverride,
            eventName = eventName,
            value = value,
            playerMoney = playerMoney,
            minerMoney = minerMoney,
            won = won ?? "",
            endReason = endReason ?? "",
            tutorial = isTutorial ? "1" : "0",
            source = Source(),
            seq = ++rowCount,
            t = Time.unscaledTime - sessionStart,
            attempt = attempts.TryGetValue(levelName, out int tries) ? tries : 0,
            build = Build,
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
        foreach (var (entry, value) in Optional(row))
            body += "&" + Field(entry, value);
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
        foreach (var (entry, value) in Optional(row))
            form.AddField(entry, value);
        UnityWebRequest request = UnityWebRequest.Post(FormUrl, form);
        request.SendWebRequest();
        pending.Add(request);
#endif
    }

    // The columns whose entry id is filled in
    static IEnumerable<(string entry, string value)> Optional(EventRow row)
    {
        if (ESeq.Length > 0) yield return (ESeq, row.seq.ToString());
        if (ET.Length > 0) yield return (ET, row.t.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (EAttempt.Length > 0) yield return (EAttempt, row.attempt.ToString());
        if (EBuild.Length > 0) yield return (EBuild, row.build ?? "");
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static string Field(string entry, string value)
    {
        return UnityWebRequest.EscapeURL(entry) + "=" + UnityWebRequest.EscapeURL(value ?? "");
    }
#endif
}
