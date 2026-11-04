using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// A conversation, played by the TutorialStep it sits on: the step's trigger starts it (e.g. Event
// "round_won"), and the step ends when the last line is read. Make one with Tools > Gold Miner >
// Tutorial Story.
//   Characters   who talks: a prefab that appears in the arena (e.g. EvilMeowmeow) at Spawn At, or
//                the player. The speaker gets the spotlight (Speaker Light) and a little bounce
//   Lines        the script: Speaker (a character's number in Characters, 0 = the first, or its
//                name; empty = narrator) and Text. Or write it
//                in a .txt file, one "0. text" (or "Name: text") per line, and put it in Script File.
//                Lines go by number, so swapping a character's Prefab or Name changes who appears.
//                Script extras: "… #event", "#choice id:Label|id:Label", "#if id" / "#endif",
//                and fireEvent "storm_off" (crush + slide the first non-player portrait right).
// Lines type out in a box over the bottom of the screen; a key or click shows the rest, then moves
// on. While it plays the round result waits (no "YOU WIN", no leaving for the map), and with
// Leave When Done the level ends as soon as the last line is read.
public class TutorialDialogue : MonoBehaviour
{
    [System.Serializable]
    public class Character
    {
        public string name = "Evil Meowmeow";
        [Tooltip("Appears in the arena while the conversation plays, e.g. the EvilMeowmeow prefab")]
        public GameObject prefab;
        [Tooltip("Where the prefab appears (a child of the step works well). Empty = the step's position")]
        public Transform spawnAt;
        [Tooltip("This character is the player (no prefab is spawned)")]
        public bool isPlayer;
        [Tooltip("Whose side they are on: the colour of their name and of the box's border while they talk (the colours are under Look)")]
        public Side side = Side.Enemy;
    }

    public enum Side { Enemy, Friend, Unknown }

    [System.Serializable]
    public class Line
    {
        [Tooltip("The character's number in Characters (0 = the first) or its name. Empty = narrator")]
        public string speaker;
        [TextArea(2, 4)] public string text;
        [Tooltip("Seconds before moving on by itself. 0 = wait for a key or click")]
        public float autoNext;
        [Tooltip("Fired (TutorialEvents) when this line shows, so other steps can react")]
        [TutorialEvent(true)] public string fireEvent;
        // Script-file only: show this line only after that choice id was picked.
        [System.NonSerialized] public string onlyIf;
        // Script-file only: a choice prompt (Buy anyway / Not buying, …).
        [System.NonSerialized] public string[] choiceIds;
        [System.NonSerialized] public string[] choiceLabels;
        public bool IsChoice => choiceIds != null && choiceIds.Length > 0;
    }

    public Character[] characters = new Character[0];
    public Line[] lines = new Line[0];
    [Tooltip("Optional: a .txt script, one \"0. text\" per line (0 = the first character). Used instead of Lines")]
    public TextAsset scriptFile;
    [Tooltip("Play the Lines above, even with a Script File set")]
    public bool useLines = false;

    [Header("Look")]
    [Tooltip("Letters per second as a line types out (0 = all at once)")]
    public float lettersPerSecond = 45f;
    [Tooltip("A sprite mask that moves onto the speaker, so they show through the step's dark")]
    public SpriteMask speakerLight;
    [Tooltip("Sorting order the spawned characters draw at, as one piece: above the arena (items, characters, the squeeze's presses at 20) and under the tutorial dark (100), so the speaker light still picks them out")]
    public int characterOrder = 50;
    [Tooltip("Show the characters as pictures standing on the box, over everything (for a scene with no arena, like the store). Off: they appear in the arena at Spawn At. The player needs a Prefab here too")]
    public bool onScreen = false;
    [Tooltip("Pictures beside the box instead of on it (turns On Screen on): the others stand on its left, the player on its right, and the box fills the space between. Fits a full screen like the store's")]
    public bool besideBox = false;
    [Tooltip("On Screen: how tall a character's picture is (the box is 230 tall)")]
    public float portraitHeight = 230f;
    [Tooltip("How big the box, its text and the pictures are: 1 = the HUD's scale, 1.25 = a quarter bigger")]
    [Range(0.5f, 2f)] public float boxScale = 1.25f;
    [Tooltip("Where the box sits: Top keeps the bottom of the screen clear, e.g. for something a step points at there. On Screen pictures hang under a top box")]
    public BoxPlace boxPosition = BoxPlace.Bottom;
    public enum BoxPlace { Bottom, Top }

    bool AtTop => boxPosition == BoxPlace.Top;

    // The box canvas's layout: 1920x1080 like the HUD, made smaller to draw everything boxScale bigger
    float LayoutWidth => 1920f / Mathf.Max(0.5f, boxScale);
    float LayoutHeight => 1080f / Mathf.Max(0.5f, boxScale);
    [Tooltip("Name and border colour of each side; the player has their own")]
    public Color enemyColor = new Color(1f, 0.35f, 0.35f);
    public Color friendColor = new Color(0.4f, 0.7f, 1f);
    public Color unknownColor = new Color(0.65f, 0.65f, 0.65f);
    public Color playerColor = new Color(1f, 0.72f, 0.82f);

    [Header("Round")]
    [Tooltip("No \"YOU WIN\" / \"YOU LOSE\" panel while it plays (and after, with Leave When Done)")]
    public bool hideResult = true;
    [Tooltip("When the last line is read after the round is over, end the level: back to the map (cleared if the round was won). A story told while the round is still being played just ends")]
    public bool leaveWhenDone = true;
    [Tooltip("Remove the spawned characters when it ends")]
    public bool removeCharacters = true;

    // True while a conversation is playing, or the round just ended and one is about to start
    // because of it: GameFlow and LevelLoader wait before leaving the level
    public static bool Playing
    {
        get
        {
            foreach (TutorialDialogue d in all) if (d.IsRunning || d.AboutToPlay()) return true;
            return false;
        }
    }

    // True while the round result ("YOU WIN") should stay hidden: the same, for conversations with
    // Hide Result, and after one that leaves the level
    public static bool HidesResult
    {
        get
        {
            foreach (TutorialDialogue d in all)
                if (d.hideResult && (d.IsRunning || d.leaving || d.AboutToPlay())) return true;
            return false;
        }
    }

    static readonly List<TutorialDialogue> all = new List<TutorialDialogue>();

    public bool IsRunning { get; private set; }
    bool leaving;   // finished, and the level is ending: the result stays hidden

    const int SortingOrder = 520;   // above the HUD, the tutorial's UI dark and UI callouts
    static readonly Color Panel = new Color(0.118f, 0.165f, 0.247f, 0.95f);   // the HUD's panel colour

    TutorialStep step;
    readonly List<Line> script = new List<Line>();
    readonly Dictionary<string, Transform> actors = new Dictionary<string, Transform>();
    readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();
    readonly List<GameObject> spawned = new List<GameObject>();
    int index;
    float lineStart;
    bool typing;
    Transform speaker;
    string chosenId;          // last #choice pick (buy / skip / …)
    bool awaitingChoice;
    GameObject choiceRow;
    bool stormingOff;

    GameObject box;
    RectTransform panel;
    TMP_Text nameText;
    Outline border;   // round the box, in the speaker's side colour
    TMP_Text bodyText;
    Image hintImage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // Play without a domain reload keeps statics
    {
        all.Clear();
    }

    void Awake() { step = GetComponent<TutorialStep>(); }
    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { all.Remove(this); }

    // The round is over and this conversation's step is set to start on that result
    // ("round_won", "round_lost" or "round_end") but hasn't yet: the frame or two in between
    bool AboutToPlay()
    {
        if (step == null || step.TimesPlayed > 0 || step.startOn != TutorialStep.StartTrigger.Event) return false;
        // Used up (Only First Time and already seen, or played its Max Times): it never starts, so
        // nothing should wait for it (the level would never move on)
        if (step.Current == TutorialStep.State.Done || !step.isActiveAndEnabled) return false;
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.IsPlaying) return false;
        string e = step.startEvent;
        return e == "round_end" || (e == "round_won" && gm.PlayerWon) || (e == "round_lost" && !gm.PlayerWon);
    }

    // Called by the step when it starts
    public void Play(TutorialStep owner)
    {
        if (IsRunning) return;
        step = owner;
        BuildScript();
        if (script.Count == 0) { step.End(); return; }

        IsRunning = true;
        chosenId = null;
        awaitingChoice = false;
        stormingOff = false;
        if (box == null) BuildBox();
        SpawnCharacters();
        box.SetActive(true);
        index = -1;
        Next();
    }


    void Update()
    {
        if (!IsRunning) return;
        if (awaitingChoice)
        {
            // Fallback if EventSystem / GraphicRaycaster miss the buttons: 1/2 or left/right click halves.
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame)
                    PickChoiceAt(0);
                else if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
                    PickChoiceAt(1);
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && index >= 0 && index < script.Count && script[index].IsChoice)
            {
                float x = Mouse.current.position.ReadValue().x;
                PickChoiceAt(x < Screen.width * 0.5f ? 0 : 1);
            }
            return;
        }
        if (index < 0 || index >= script.Count) return;
        Line line = script[index];
        if (line.IsChoice) return;
        float t = Time.unscaledTime - lineStart;

        int shown = lettersPerSecond > 0f ? Mathf.Min(line.text.Length, Mathf.FloorToInt(t * lettersPerSecond)) : line.text.Length;
        typing = shown < line.text.Length;
        bodyText.maxVisibleCharacters = shown;
        hintImage.enabled = !typing && line.autoNext <= 0f && !stormingOff;

        bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (pressed && t > 0.15f && !stormingOff)
        {
            if (typing) lineStart = Time.unscaledTime - line.text.Length / Mathf.Max(1f, lettersPerSecond);   // show it all
            else Next();
        }
        else if (!typing && line.autoNext > 0f && t >= line.autoNext + line.text.Length / Mathf.Max(1f, lettersPerSecond))
        {
            Next();
        }

        Bounce(t);
    }

    // The speaker's side colour; white for the narrator
    Color ColorOf(Character who)
    {
        if (who == null) return Color.white;
        if (who.isPlayer) return playerColor;
        switch (who.side)
        {
            case Side.Friend: return friendColor;
            case Side.Unknown: return unknownColor;
            default: return enemyColor;
        }
    }

    // Fired as each line goes (before the next line's own event): a Tutorial Pointer ends on it
    public const string LineEvent = "story_line";

    void Next()
    {
        if (index >= 0) TutorialEvents.Fire(LineEvent);
        HideChoices();
        index++;
        while (index < script.Count)
        {
            Line skip = script[index];
            if (string.IsNullOrEmpty(skip.onlyIf) || skip.onlyIf == chosenId) break;
            index++;
        }
        if (index >= script.Count) { Finish(); return; }

        Line line = script[index];
        lineStart = Time.unscaledTime;

        if (line.IsChoice)
        {
            ShowChoices(line);
            return;
        }

        bodyText.text = line.text;
        bodyText.maxVisibleCharacters = 0;
        bodyText.color = Color.white;

        Character who = FindCharacter(line.speaker);
        nameText.text = who != null ? who.name : int.TryParse(line.speaker, out _) ? "" : (line.speaker ?? "");
        Color color = ColorOf(who);
        nameText.color = color;
        if (border != null) { border.effectColor = color; border.enabled = who != null; }
        speaker = who != null && actors.TryGetValue(who.name, out Transform t) ? t : null;
        MoveLight();
        DimListeners();
        TutorialEvents.Fire(line.fireEvent);
        if (line.fireEvent == "storm_off")
        {
            // Show the swear in full, then slide JK off and continue.
            bodyText.maxVisibleCharacters = int.MaxValue;
            typing = false;
            hintImage.enabled = false;
            StartCoroutine(StormOffThenNext());
        }
    }

    void ShowChoices(Line line)
    {
        awaitingChoice = true;
        typing = false;
        nameText.text = "Your move";
        nameText.color = new Color(1f, 0.85f, 0.35f);
        if (border != null)
        {
            border.enabled = true;
            border.effectColor = new Color(1f, 0.75f, 0.2f);
        }
        bodyText.text = "";
        bodyText.maxVisibleCharacters = int.MaxValue;
        hintImage.enabled = false;
        speaker = null;
        DimListeners();

        if (choiceRow != null) Destroy(choiceRow);
        choiceRow = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
        RectTransform row = (RectTransform)choiceRow.transform;
        row.SetParent(panel, false);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 1f);
        row.offsetMin = new Vector2(28f, 20f);
        row.offsetMax = new Vector2(-28f, -58f);
        var layout = choiceRow.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 8, 0);

        var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        RectTransform btnRow = (RectTransform)buttons.transform;
        btnRow.SetParent(row, false);
        var h = buttons.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 20f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;

        for (int i = 0; i < line.choiceIds.Length; i++)
        {
            string id = line.choiceIds[i];
            string label = i < line.choiceLabels.Length ? line.choiceLabels[i] : id;
            MakeChoiceButton(btnRow, id, label);
        }
        row.SetAsLastSibling();
        EnsureUiInput();
    }

    // Dialogue box needs a GraphicRaycaster; the Store scene needs an Input System UI module.
    void EnsureUiInput()
    {
        if (box != null && box.GetComponent<GraphicRaycaster>() == null)
            box.AddComponent<GraphicRaycaster>();
        EventSystem events = EventSystem.current != null
            ? EventSystem.current
            : FindFirstObjectByType<EventSystem>();
        if (events == null)
        {
            var go = new GameObject("DialogueEventSystem");
            events = go.AddComponent<EventSystem>();
        }
        var legacy = events.GetComponent<StandaloneInputModule>();
        var modern = events.GetComponent<InputSystemUIInputModule>();
        if (modern == null)
        {
            if (legacy != null) Destroy(legacy);
            modern = events.gameObject.AddComponent<InputSystemUIInputModule>();
            modern.AssignDefaultActions();
        }
        else if (legacy != null)
        {
            legacy.enabled = false;
            modern.enabled = true;
        }
    }

    void MakeChoiceButton(RectTransform parent, string id, string label)
    {
        var go = new GameObject("Choice_" + id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(0f, 88f);
        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.95f, 0.72f, 0.18f, 1f);
        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 0.95f, 0.55f, 1f);
        outline.effectDistance = new Vector2(4f, -4f);
        Button btn = go.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.92f, 0.55f, 1f);
        colors.pressedColor = new Color(0.85f, 0.55f, 0.1f, 1f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;

        TMP_Text text = Text(rect, "Label", Vector2.zero, Vector2.zero, 32f, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;
        text.color = new Color(0.12f, 0.08f, 0.02f);
        RectTransform tr = text.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 8f);
        tr.offsetMax = new Vector2(-8f, -8f);

        go.AddComponent<ChoiceButtonPulse>();

        string pick = id;
        btn.onClick.AddListener(() => PickChoice(pick));
    }

    // Soft scale pulse so choice buttons read as clickable.
    class ChoiceButtonPulse : MonoBehaviour
    {
        RectTransform rect;
        void Awake() { rect = transform as RectTransform; }
        void Update()
        {
            if (rect == null) return;
            float s = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f);
            rect.localScale = new Vector3(s, s, 1f);
        }
    }

    void PickChoice(string id)
    {
        if (!awaitingChoice) return;
        chosenId = id;
        awaitingChoice = false;
        HideChoices();
        Next();
    }

    void PickChoiceAt(int i)
    {
        if (!awaitingChoice || index < 0 || index >= script.Count) return;
        Line line = script[index];
        if (!line.IsChoice || i < 0 || i >= line.choiceIds.Length) return;
        PickChoice(line.choiceIds[i]);
    }

    void HideChoices()
    {
        awaitingChoice = false;
        if (choiceRow != null) { Destroy(choiceRow); choiceRow = null; }
    }

    IEnumerator StormOffThenNext()
    {
        yield return StormOff();
        if (IsRunning) Next();
    }

    // Crush JK's colours and slide the portrait off to the right (store DEN beat).
    IEnumerator StormOff()
    {
        stormingOff = true;
        Transform jk = null;
        foreach (Character c in characters)
        {
            if (c == null || c.isPlayer || string.IsNullOrEmpty(c.name)) continue;
            if (actors.TryGetValue(c.name, out Transform portrait)) { jk = portrait; break; }
        }
        if (jk == null) { stormingOff = false; yield break; }

        var crush = new List<Image>();
        foreach (var part in portraitColors)
            if (part.Key != null && part.Key.transform.IsChildOf(jk)) crush.Add(part.Key);
        foreach (Image img in crush)
        {
            Color baseColor = portraitColors[img];
            Color sad = new Color(baseColor.r * 0.45f, baseColor.g * 0.45f, baseColor.b * 0.55f, baseColor.a);
            img.color = sad;
            portraitColors[img] = sad;
        }

        RectTransform rect = jk as RectTransform;
        if (rect == null) { stormingOff = false; yield break; }
        SpawnAngerFlames(rect);

        // Flame holds ~2s above JK's head, then he bolts off quickly.
        float holdUntil = Time.unscaledTime + 2f;
        while (Time.unscaledTime < holdUntil) yield return null;

        Vector2 from = rect.anchoredPosition;
        Vector2 to = from + new Vector2(LayoutWidth * 0.85f, -30f);
        float dur = 0.28f;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(elapsed / dur);
            float ease = u * u;   // accelerate out
            rect.anchoredPosition = Vector2.Lerp(from, to, ease);
            yield return null;
        }
        jk.gameObject.SetActive(false);
        if (speaker == jk) speaker = null;
        stormingOff = false;
    }

    // Cartoon fire plume over the speaker's head while they storm off.
    void SpawnAngerFlames(RectTransform who)
    {
        var root = new GameObject("AngerFlames", typeof(RectTransform));
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.SetParent(who, false);
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 18f);
        rootRect.sizeDelta = new Vector2(140f, 110f);
        root.AddComponent<AngerFlameBurst>();
    }

    class AngerFlameBurst : MonoBehaviour
    {
        struct Tongue
        {
            public RectTransform rect;
            public float baseX, baseRot, phase, wiggle;
        }

        readonly List<Tongue> tongues = new List<Tongue>();
        readonly List<RectTransform> embers = new List<RectTransform>();
        float born;
        static Sprite flameSprite;
        static Sprite emberSprite;

        void Awake()
        {
            born = Time.unscaledTime;
            if (flameSprite == null) flameSprite = BuildFlameSprite();
            if (emberSprite == null) emberSprite = BuildEmberSprite();

            // Outer red, mid orange, inner yellow — stacked so it reads as one fire.
            AddTongue(0f, 72f, 100f, 0f, 0f, 1f);
            AddTongue(-22f, 48f, 72f, -18f, 0.7f, 0.85f);
            AddTongue(24f, 44f, 68f, 16f, 1.4f, 0.8f);
            AddTongue(-8f, 36f, 56f, -8f, 2.1f, 0.7f);
            AddTongue(10f, 32f, 50f, 10f, 2.8f, 0.65f);

            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Ember", typeof(RectTransform), typeof(Image));
                RectTransform r = (RectTransform)go.transform;
                r.SetParent(transform, false);
                float s = 5f + (i % 3) * 2f;
                r.sizeDelta = new Vector2(s, s);
                Image img = go.GetComponent<Image>();
                img.sprite = emberSprite;
                img.color = i % 2 == 0
                    ? new Color(1f, 0.85f, 0.25f, 1f)
                    : new Color(1f, 0.45f, 0.1f, 1f);
                img.raycastTarget = false;
                embers.Add(r);
            }
        }

        void AddTongue(float x, float w, float h, float rot, float phase, float wiggle)
        {
            var go = new GameObject("Flame", typeof(RectTransform), typeof(Image));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(transform, false);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, 4f);
            r.localRotation = Quaternion.Euler(0f, 0f, rot);
            Image img = go.GetComponent<Image>();
            img.sprite = flameSprite;
            img.color = Color.white;
            img.raycastTarget = false;
            tongues.Add(new Tongue
            {
                rect = r, baseX = x, baseRot = rot, phase = phase, wiggle = wiggle
            });
        }

        // Classic fire: wide red base → orange body → bright yellow tip, wavy silhouette.
        static Sprite BuildFlameSprite()
        {
            const int w = 64, h = 96;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1); // 0 base → 1 tip
                // Asymmetric S-curve edges (cartoon flame).
                float left = -0.42f + 0.12f * Mathf.Sin(v * Mathf.PI * 2.2f)
                             + 0.08f * Mathf.Sin(v * Mathf.PI * 4.1f);
                float right = 0.42f - 0.1f * Mathf.Sin(v * Mathf.PI * 2.6f + 1.2f)
                              - 0.07f * Mathf.Sin(v * Mathf.PI * 3.7f);
                // Taper hard near the tip.
                float taper = Mathf.Lerp(1f, 0.08f, v * v);
                left *= taper;
                right *= taper;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f;
                    if (u < left || u > right) { tex.SetPixel(x, y, Color.clear); continue; }
                    float across = Mathf.InverseLerp(left, right, u);
                    float edge = Mathf.Min(across, 1f - across) * 2f; // 0 rim → 1 center
                    float a = Mathf.Clamp01(0.55f + 0.55f * edge);
                    if (v < 0.08f) a *= v / 0.08f; // soft base

                    // Colour: red rim / base → orange mid → yellow core & tip.
                    Color rim = new Color(0.95f, 0.12f, 0.02f, a);
                    Color mid = new Color(1f, 0.45f, 0.05f, a);
                    Color core = new Color(1f, 0.92f, 0.35f, a);
                    Color c = Color.Lerp(rim, mid, Mathf.Clamp01(edge * 1.2f));
                    c = Color.Lerp(c, core, Mathf.Clamp01((edge - 0.35f) * 1.8f) * Mathf.Lerp(0.35f, 1f, v));
                    // Tip goes yellower overall.
                    c = Color.Lerp(c, core, v * v * 0.65f);
                    c.a = a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 48f);
        }

        static Sprite BuildEmberSprite()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float mid = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(mid, mid)) / mid;
                    float a = Mathf.Clamp01(1.1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 16f);
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            for (int i = 0; i < tongues.Count; i++)
            {
                Tongue f = tongues[i];
                if (f.rect == null) continue;
                float sway = Mathf.Sin(t * 10f + f.phase) * 6f * f.wiggle;
                float stretch = 1f + 0.12f * Mathf.Sin(t * 14f + f.phase * 1.3f);
                float squash = 1f + 0.08f * Mathf.Sin(t * 17f + f.phase * 0.7f);
                f.rect.anchoredPosition = new Vector2(f.baseX + sway, 4f + Mathf.Abs(Mathf.Sin(t * 9f + f.phase)) * 3f);
                f.rect.localRotation = Quaternion.Euler(0f, 0f, f.baseRot + sway * 0.6f);
                f.rect.localScale = new Vector3(squash, stretch, 1f);
            }
            for (int i = 0; i < embers.Count; i++)
            {
                RectTransform r = embers[i];
                if (r == null) continue;
                float life = (t * 0.55f + i * 0.17f) % 1.2f;
                float u = life / 1.2f;
                float x = Mathf.Sin(i * 1.7f + t * 3f) * (18f + i * 2f);
                float y = 20f + u * 70f;
                r.anchoredPosition = new Vector2(x, y);
                float s = (1f - u) * (0.7f + (i % 3) * 0.2f);
                r.localScale = new Vector3(s, s, 1f);
                Image img = r.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = Mathf.Clamp01(1.2f - u * 1.4f);
                    img.color = c;
                }
            }
        }
    }

    void Finish()
    {
        IsRunning = false;
        HideChoices();
        StopAllCoroutines();
        stormingOff = false;
        if (box != null) box.SetActive(false);
        if (removeCharacters)
            foreach (GameObject go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
        step.End();
        if (leaveWhenDone) Leave();
    }

    // The level is over: what the result screen would have done after its countdown, now
    void Leave()
    {
        leaving = true;
        GameManager gm = GameManager.Instance;
        // Only once the round is over: a story told while the level is still being played (an
        // introduction at the start) just ends, and the level goes on
        if (gm != null && gm.IsPlaying) { leaving = false; return; }
        bool won = gm != null && !gm.IsPlaying && gm.PlayerWon;
        GameFlow flow = FindFirstObjectByType<GameFlow>();
        // The character pick leaves by itself once a character is picked, never without one
        if (gm == null && FindFirstObjectByType<ChampionSelect>() != null) { leaving = false; return; }
        if (flow != null && flow.CurrentNode != null)
        {
            if (won) flow.CompleteCurrentNode();
            else flow.ReturnToMap();
            return;
        }
        if (won) LevelLoader.AdvanceNow = true;   // no map (editor): the next level
        else leaving = false;                      // lost: show the result, R retries
    }

    // --- script and characters ------------------------------------------------------------------

    void BuildScript()
    {
        script.Clear();
        if (scriptFile == null || useLines)
        {
            foreach (Line line in lines) if (line != null && !string.IsNullOrEmpty(line.text)) script.Add(line);
            return;
        }
        string onlyIf = "";
        foreach (string raw in scriptFile.text.Split('\n'))
        {
            string text = raw.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("#choice "))
            {
                Line choice = ParseChoice(text.Substring(8).Trim());
                if (choice != null)
                {
                    choice.onlyIf = onlyIf;
                    script.Add(choice);
                }
                continue;
            }
            if (text.StartsWith("#if "))
            {
                onlyIf = text.Substring(4).Trim();
                continue;
            }
            if (text == "#endif")
            {
                onlyIf = "";
                continue;
            }
            if (text.StartsWith("#")) continue;   // blank-line comments
            string fire = TakeEvent(ref text);
            int before = script.Count;
            AddScriptLine(text);
            if (script.Count > before)
            {
                script[before].fireEvent = fire;
                script[before].onlyIf = onlyIf;
            }
        }
    }

    // "buy:Buy anyway|skip:Not buying" → a choice Line
    static Line ParseChoice(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string[] parts = raw.Split('|');
        var ids = new List<string>();
        var labels = new List<string>();
        foreach (string part in parts)
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            int colon = p.IndexOf(':');
            if (colon <= 0) continue;
            ids.Add(p.Substring(0, colon).Trim());
            labels.Add(p.Substring(colon + 1).Trim());
        }
        if (ids.Count == 0) return null;
        return new Line
        {
            speaker = "",
            text = "",
            choiceIds = ids.ToArray(),
            choiceLabels = labels.ToArray(),
        };
    }

    // "... text #show_shelf": the line fires TutorialEvents "show_shelf" when it shows (a step can
    // light the shelf on it). The # must follow a space and the name has no spaces
    static string TakeEvent(ref string text)
    {
        int hash = text.LastIndexOf(" #");
        if (hash < 0) return "";
        string name = text.Substring(hash + 2).Trim();
        if (name.Length == 0 || name.IndexOf(' ') >= 0) return "";
        text = text.Substring(0, hash).TrimEnd();
        return name;
    }

    void AddScriptLine(string text)
    {
        // "0. text": the first character says it, "1. text" the second, ...
        int digits = 0;
        while (digits < text.Length && char.IsDigit(text[digits])) digits++;
        if (digits > 0 && digits < text.Length && (text[digits] == '.' || text[digits] == ':' || text[digits] == ')'))
        {
            string number = text.Substring(0, digits);
            if (FindCharacter(number) == null)
                Debug.LogWarning("TutorialDialogue: " + scriptFile.name + " has a line for character " + number +
                                 ", but Characters has only " + characters.Length + " (they count from 0)", this);
            script.Add(new Line { speaker = number, text = text.Substring(digits + 1).Trim() });
            return;
        }

        // "Name: text"; anything else is narration
        int colon = text.IndexOf(':');
        bool named = colon > 0 && FindCharacter(text.Substring(0, colon).Trim()) != null;
        script.Add(new Line
        {
            speaker = named ? text.Substring(0, colon).Trim() : "",
            text = named ? text.Substring(colon + 1).Trim() : text,
        });
    }

    // A speaker is a character's name, or its number in Characters (0 = the first)
    Character FindCharacter(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (int.TryParse(name, out int number))
            return number >= 0 && number < characters.Length ? characters[number] : null;
        foreach (Character c in characters)
            if (c != null && string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    void SpawnCharacters()
    {
        actors.Clear();
        baseScales.Clear();
        portraitColors.Clear();
        if (Portraits) { SpawnPortraits(); return; }
        foreach (Character c in characters)
        {
            if (c == null || string.IsNullOrEmpty(c.name)) continue;
            if (c.isPlayer)
            {
                PlayerCharacter player = FindFirstObjectByType<PlayerCharacter>();
                if (player != null) actors[c.name] = player.transform;
                continue;
            }
            if (c.prefab == null) continue;
            Transform at = c.spawnAt != null ? c.spawnAt : transform;
            GameObject go = SpawnLook(c.prefab, at.position);
            go.name = c.name;
            // One sorting group: the prefab's own layering stays, the whole character draws on top
            SortingGroup group = go.GetComponent<SortingGroup>();
            if (group == null) group = go.AddComponent<SortingGroup>();
            group.sortingOrder = characterOrder;
            spawned.Add(go);
            actors[c.name] = go.transform;
        }
    }

    // --- On Screen: the characters as pictures standing on the box ------------------------------

    // Where each character stands, as shares of the arena's width (bottom centre): the first
    // on the right, the second on the left, then nearer the middle
    static readonly float[] PortraitSlots = { 0.82f, 0.18f, 0.62f, 0.38f };
    readonly Dictionary<Image, Color> portraitColors = new Dictionary<Image, Color>();

    bool Portraits => onScreen || besideBox;

    void SpawnPortraits()
    {
        float arenaWidth = LayoutWidth * (1f - HUDLayout.SidebarWidth);
        if (besideBox) { SpawnBesideBox(); return; }
        LayOutPanel(60f, arenaWidth - 60f);
        int slot = 0;
        foreach (Character c in characters)
        {
            if (c == null || string.IsNullOrEmpty(c.name) || c.prefab == null) continue;   // the player too needs a Prefab here
            float x = arenaWidth * PortraitSlots[Mathf.Min(slot, PortraitSlots.Length - 1)];
            slot++;
            AddPortrait(c, new Vector2(x, BoxTop + 12f));
        }
    }

    // Beside Box (store): player on the left, others (J.K. Rolling) on the right; the box spans
    // the gap. Across the whole screen when there is no arena (and so no HUD sidebar).
    void SpawnBesideBox()
    {
        float width = FindFirstObjectByType<LevelLayout>() != null ? LayoutWidth * (1f - HUDLayout.SidebarWidth) : LayoutWidth;
        const float edge = 40f, gap = 24f;
        float left = edge, right = width - edge;
        foreach (Character c in characters)
        {
            if (c == null || string.IsNullOrEmpty(c.name)) continue;
            if (c.prefab == null)
            {
                Debug.LogWarning("Story " + name + ": " + c.name + " has no Prefab, so no picture beside the box", this);
                continue;
            }
            RectTransform portrait = AddPortrait(c, new Vector2(0f, BoxBottom));
            float w = portrait.sizeDelta.x;
            if (c.isPlayer) { Place(portrait, left + w * 0.5f, BoxBottom); left += w + gap; }
            else { Place(portrait, right - w * 0.5f, BoxBottom); right -= w + gap; }
        }
        LayOutPanel(left, right);
    }

    RectTransform AddPortrait(Character c, Vector2 standAt)
    {
        RectTransform portrait = Portrait(c.prefab, standAt);
        portrait.name = c.name;
        spawned.Add(portrait.gameObject);
        actors[c.name] = portrait;
        return portrait;
    }

    // The box spanning from `left` to `right`, on its bottom margin (or its top one, Box Position Top)
    void LayOutPanel(float left, float right)
    {
        panel.anchorMin = panel.anchorMax = new Vector2(0f, AtTop ? 1f : 0f);
        panel.pivot = new Vector2(0.5f, AtTop ? 1f : 0f);
        panel.sizeDelta = new Vector2(Mathf.Max(300f, right - left), BoxHeight);
        panel.anchoredPosition = new Vector2((left + right) * 0.5f, AtTop ? -BoxBottom : BoxBottom);
    }

    // A picture at `x`, `y` up from the bottom edge to its feet; with the box at the top, the
    // same seen from the top edge: `y` down to its head, hanging below
    void Place(RectTransform portrait, float x, float y)
    {
        portrait.anchorMin = portrait.anchorMax = new Vector2(0f, AtTop ? 1f : 0f);
        portrait.anchoredPosition = new Vector2(x, AtTop ? -(y + portrait.sizeDelta.y) : y);
    }

    const float BoxBottom = 30f, BoxHeight = 230f;
    const float BoxTop = BoxBottom + BoxHeight;

    // The prefab's sprites copied into UI images (UISpriteCopy), portraitHeight tall, standing with
    // its bottom centre on `standAt`, under the box panel
    RectTransform Portrait(GameObject prefab, Vector2 standAt)
    {
        RectTransform rect = UISpriteCopy.Build(prefab, box.transform, portraitHeight, portraitColors);
        rect.SetAsFirstSibling();
        Place(rect, standAt.x, standAt.y);
        return rect;
    }

    // The speaker in full colour, the others dimmed
    void DimListeners()
    {
        if (!Portraits) return;
        foreach (KeyValuePair<Image, Color> part in portraitColors)
        {
            if (part.Key == null) continue;
            bool talking = speaker != null && part.Key.transform.IsChildOf(speaker);
            Color c = part.Value;
            part.Key.color = talking ? c : new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, c.a);
        }
    }

    // A copy of the prefab that only looks the part. A playable character's prefab (the Cat, JK.
    // Rolling) would otherwise be a second player: hooks, steps and the camera look for the
    // PlayerCharacter. It is made under an inactive holder, so its scripts never wake up, and its
    // scripts, physics and colliders are taken off before it appears (scripts first: some need the body)
    static GameObject SpawnLook(GameObject prefab, Vector3 at)
    {
        if (prefab.GetComponentInChildren<PlayerCharacter>(true) == null)
            return Instantiate(prefab, at, prefab.transform.rotation);

        var holder = new GameObject("SpawnHolder");
        holder.SetActive(false);
        GameObject go = Instantiate(prefab, at, prefab.transform.rotation, holder.transform);
        foreach (MonoBehaviour script in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
        foreach (Collider2D col in go.GetComponentsInChildren<Collider2D>(true)) DestroyImmediate(col);
        foreach (Rigidbody2D body in go.GetComponentsInChildren<Rigidbody2D>(true)) DestroyImmediate(body);
        go.transform.SetParent(null, true);
        Destroy(holder);
        return go;
    }

    // The speaker hops a little while their line types out
    void Bounce(float t)
    {
        foreach (Transform actor in actors.Values)
        {
            if (actor == null || actor.GetComponent<PlayerCharacter>() != null) continue;   // the player's body is physics driven
            float hop = actor == speaker && typing ? Mathf.Abs(Mathf.Sin(t * 12f)) * 0.06f : 0f;
            if (!baseScales.TryGetValue(actor, out Vector3 scale)) baseScales[actor] = scale = actor.localScale;
            actor.localScale = scale * (1f + hop);
        }
        MoveLight();
    }

    void MoveLight()
    {
        if (speakerLight == null) return;
        speakerLight.enabled = speaker != null;
        if (speaker == null) return;
        Bounds bounds = new Bounds(speaker.position, Vector3.zero);
        foreach (Renderer r in speaker.GetComponentsInChildren<Renderer>())
            if (r.enabled && !(r is LineRenderer)) bounds.Encapsulate(r.bounds);
        speakerLight.transform.position = new Vector3(bounds.center.x, bounds.center.y, speakerLight.transform.position.z);
        float size = Mathf.Max(bounds.size.x, bounds.size.y) * 1.3f + 0.4f;
        speakerLight.transform.localScale = new Vector3(size, size, 1f);   // the circle sprite is 1 unit across
    }

    // --- the box --------------------------------------------------------------------------------

    // Built when first needed: a screen canvas above everything, laid out like the HUD (1920x1080,
    // times boxScale), with a panel across the bottom of the arena
    void BuildBox()
    {
        box = new GameObject("DialogueBox", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                             typeof(GraphicRaycaster));
        Canvas canvas = box.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        CanvasScaler scaler = box.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(LayoutWidth, LayoutHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        float arenaWidth = LayoutWidth * (1f - HUDLayout.SidebarWidth);
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        panel.SetParent(box.transform, false);
        LayOutPanel(60f, arenaWidth - 60f);
        Image image = panel.GetComponent<Image>();
        Texture2D rounded = WorldMapController.MakeRounded(16);
        image.sprite = Sprite.Create(rounded, new Rect(0f, 0f, rounded.width, rounded.height), new Vector2(0.5f, 0.5f),
                                     100f, 0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
        image.type = Image.Type.Sliced;
        image.color = Panel;
        image.raycastTarget = false;
        border = panel.gameObject.AddComponent<Outline>();   // the speaker's side colour (Next)
        border.effectDistance = new Vector2(4f, -4f);
        border.useGraphicAlpha = false;
        border.enabled = false;

        nameText = Text(panel, "Name", new Vector2(36f, -18f), new Vector2(-72f, 50f), 34f, FontStyles.Bold);
        bodyText = Text(panel, "Text", new Vector2(36f, -70f), new Vector2(-72f, 140f), 32f, FontStyles.Normal);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        // The "next" arrow: a drawn triangle (the font has no ▶)
        var hintObject = new GameObject("Hint", typeof(RectTransform), typeof(Image));
        RectTransform hint = (RectTransform)hintObject.transform;
        hint.SetParent(panel, false);
        hint.anchorMin = hint.anchorMax = hint.pivot = new Vector2(1f, 0f);
        hint.anchoredPosition = new Vector2(-36f, 24f);
        hint.sizeDelta = new Vector2(26f, 30f);
        hintImage = hintObject.GetComponent<Image>();
        hintImage.sprite = RightTriangle();
        hintImage.color = new Color(1f, 1f, 1f, 0.7f);
        hintImage.raycastTarget = false;
    }

    // A triangle pointing right, filling its square texture
    static Sprite RightTriangle()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float halfHeight = 0.5f * (1f - u);   // the point is on the right edge
                float inside = halfHeight - Mathf.Abs(v - 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(inside * size));
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // A text stretched across the panel's width, pinned to its top
    static TMP_Text Text(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
}
