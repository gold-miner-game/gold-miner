using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
//                Lines go by number, so swapping a character's Prefab or Name changes who appears
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
        if (box == null) BuildBox();
        SpawnCharacters();
        box.SetActive(true);
        index = -1;
        Next();
    }


    void Update()
    {
        if (!IsRunning) return;
        Line line = script[index];
        float t = Time.unscaledTime - lineStart;

        int shown = lettersPerSecond > 0f ? Mathf.Min(line.text.Length, Mathf.FloorToInt(t * lettersPerSecond)) : line.text.Length;
        typing = shown < line.text.Length;
        bodyText.maxVisibleCharacters = shown;
        hintImage.enabled = !typing && line.autoNext <= 0f;

        bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (pressed && t > 0.15f)
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
        index++;
        if (index >= script.Count) { Finish(); return; }

        Line line = script[index];
        lineStart = Time.unscaledTime;
        bodyText.text = line.text;
        bodyText.maxVisibleCharacters = 0;

        Character who = FindCharacter(line.speaker);
        nameText.text = who != null ? who.name : int.TryParse(line.speaker, out _) ? "" : (line.speaker ?? "");
        Color color = ColorOf(who);
        nameText.color = color;
        if (border != null) { border.effectColor = color; border.enabled = who != null; }
        speaker = who != null && actors.TryGetValue(who.name, out Transform t) ? t : null;
        MoveLight();
        DimListeners();
        TutorialEvents.Fire(line.fireEvent);
    }

    void Finish()
    {
        IsRunning = false;
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
        foreach (string raw in scriptFile.text.Split('\n'))
        {
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith("#")) continue;   // blank lines and # comments
            string fire = TakeEvent(ref text);
            int before = script.Count;
            AddScriptLine(text);
            if (script.Count > before) script[before].fireEvent = fire;
        }
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

    // Beside Box: the others from the left edge inwards, the player from the right edge inwards,
    // standing on the box's bottom line; the box spans the gap. Across the whole screen when there
    // is no arena (and so no HUD sidebar), like the store
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
            if (c.isPlayer) { Place(portrait, right - w * 0.5f, BoxBottom); right -= w + gap; }
            else { Place(portrait, left + w * 0.5f, BoxBottom); left += w + gap; }
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
        box = new GameObject("DialogueBox", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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
