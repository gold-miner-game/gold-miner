using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Owner: Penny (Level map & tutorial)
// One beat of a wordless tutorial: when its start trigger happens it shows its children (spotlight
// Holes, Callouts = arrow + short label, anything else), optionally darkens the arena around the
// holes and freezes the game, then hides them again when its end trigger happens. Steps sit under a
// "Tutorial" object in the level scene; make them with Tools > Gold Miner > Tutorial Step.
//
// Triggers are public, so they can be changed in the Inspector or from code:
//   startOn / endOn    which trigger (see StartTrigger / EndTrigger)
//   Play() / End()     start or end the step now (the Manual trigger, or from a UnityEvent)
//   TutorialEvents.Fire("name")   the Event trigger; every step fires "<name>.start" / "<name>.end"
//   onStart / onEnd    UnityEvents, to start something else when this step starts or ends
// The freeze is a time scale of 0, like the HUD's pause. It ends as the step starts fading out.
public class TutorialStep : MonoBehaviour
{
    public enum StartTrigger
    {
        LevelStart,         // as soon as the level is playing
        AfterStep,          // when another step ends (afterStep; empty = the step above this one)
        Event,              // TutorialEvents.Fire(startEvent)
        PlayerEntersArea,   // the player walks into the area (again, after leaving, for repeats)
        ItemGone,           // the item is eaten, grabbed or destroyed
        Manual,             // only Play(), from code or a UnityEvent
    }

    public enum EndTrigger
    {
        Seconds,            // after `seconds`
        AnyKey,             // a key press or click (works while frozen)
        PlayerMoves,        // the player moves (never frozen)
        PlayerEntersArea,   // the player walks into the area (never frozen)
        ItemGone,           // the item is eaten, grabbed or destroyed
        Event,              // TutorialEvents.Fire(endEvent)
        Manual,             // only End(), from code or a UnityEvent
    }

    public enum State { Waiting, Delaying, Showing, Done }

    [Header("Start")]
    public StartTrigger startOn = StartTrigger.LevelStart;
    [Tooltip("Seconds between the trigger and the step showing")]
    public float delay = 0f;
    [Tooltip("AfterStep: the step to follow. Empty = the step above this one in the Hierarchy")]
    public TutorialStep afterStep;
    [Tooltip("Event: the TutorialEvents name that starts this step")]
    public string startEvent;

    [Header("End")]
    public EndTrigger endOn = EndTrigger.Seconds;
    [Tooltip("Seconds: how long the step shows")]
    public float seconds = 3f;
    [Tooltip("Event: the TutorialEvents name that ends this step")]
    public string endEvent;

    [Header("Used by the Area / Item triggers")]
    [Tooltip("PlayerEntersArea: the area's centre (a child or any object; empty = this step's position)")]
    public Transform area;
    public Vector2 areaSize = new Vector2(2f, 2f);
    [Tooltip("ItemGone: the item (gold, diamond, ...) to watch")]
    public Transform item;

    [Header("While showing")]
    [Tooltip("Nothing moves while the step shows (the clock, the hooks, the player)")]
    public bool freezeGame = true;
    [Tooltip("Darken the arena outside the step's Holes (sprite masks)")]
    public bool darken = true;
    [Range(0f, 1f)] public float darkness = 0.8f;
    [Tooltip("Seconds the fade in / fade out takes")]
    public float fadeTime = 0.4f;
    [Tooltip("How many times the step can play. 0 = every time its trigger happens")]
    public int maxTimes = 1;

    [Header("Hooks for other things")]
    public UnityEvent onStart;
    public UnityEvent onEnd;

    public State Current { get; private set; } = State.Waiting;
    public int TimesPlayed { get; private set; }
    public int TimesFinished { get; private set; }
    // 0..1: how much of the step is showing (its fade)
    public float Visibility { get; private set; }

    const int DarkOrder = 100;   // above the arena; callouts draw at 110

    // Shared by all steps: the one dark overlay, the steps on screen, and how many hold the freeze
    static SpriteRenderer dark;
    static readonly List<TutorialStep> showing = new List<TutorialStep>();
    static readonly List<TutorialUIHole> uiHoleBuffer = new List<TutorialUIHole>();
    static int freezes;

    readonly List<GameObject> props = new List<GameObject>();
    readonly List<CanvasGroup> overlays = new List<CanvasGroup>();   // child canvases, drawn above the HUD
    SpriteRenderer[] sprites;
    float[] spriteAlphas;
    TMP_Text[] texts;
    TutorialUIHole[] uiHoles;
    TutorialDialogue dialogue;   // optional, on this object: a conversation that plays while it shows
    float[] textAlphas;

    PlayerCharacter player;
    float delayUntil;
    float shownAt;
    float endingAt = -1f;     // when the fade out began; -1 = not ending
    bool startRequested;
    bool endRequested;
    bool levelStarted;
    bool wasInArea;
    bool wasItemGone;
    bool itemSeen;           // the level's items start hidden, so "gone" only counts once it was there
    int afterSeen;            // afterStep.TimesFinished already acted on
    Vector3 playerAtStart;
    bool holding;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // Play without a domain reload keeps statics
    {
        dark = null;
        showing.Clear();
        freezes = 0;
    }

    void Awake()
    {
        foreach (Transform child in transform)
        {
            // A UI overlay (UI callouts) stays active but undrawn, so ScreenFrame fits it to the
            // 16:9 frame when the level loads, like the HUD; its CanvasGroup does the fade
            Canvas canvas = child.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = false;
                CanvasGroup group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                overlays.Add(group);
                continue;
            }
            props.Add(child.gameObject);
            child.gameObject.SetActive(false);
        }
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        spriteAlphas = new float[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) spriteAlphas[i] = sprites[i].color.a;
        uiHoles = GetComponentsInChildren<TutorialUIHole>(true);
        dialogue = GetComponent<TutorialDialogue>();
        texts = System.Array.FindAll(GetComponentsInChildren<TMP_Text>(true), t => t.canvas == null);   // UI text fades with its overlay
        textAlphas = new float[texts.Length];
        for (int i = 0; i < texts.Length; i++) textAlphas[i] = texts[i].alpha;

        if (afterStep == null && startOn == StartTrigger.AfterStep) afterStep = StepAbove();
    }

    void OnEnable() { TutorialEvents.Fired += OnEvent; }

    void OnDisable()
    {
        TutorialEvents.Fired -= OnEvent;
        Hold(false);
        if (showing.Remove(this) && showing.Count == 0) SetDark(0f, null);
    }

    // Start the step now (the Manual trigger). Ignored while it is already showing or used up
    public void Play()
    {
        if (Current == State.Waiting || Current == State.Delaying) Begin();
    }

    // End the step now (the Manual trigger): it fades out
    public void End()
    {
        if (Current == State.Showing && endingAt < 0f) BeginEnding();
    }

    void OnEvent(string name)
    {
        if (startOn == StartTrigger.Event && name == startEvent) startRequested = true;
        if (endOn == EndTrigger.Event && name == endEvent) endRequested = true;
    }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
        if (player == null) return;   // the Game scene isn't in yet
        float now = Time.unscaledTime;

        // Starts happen at the moment (walking in, the item going); ends also when it's already so
        bool inArea = InArea();
        bool areaEntered = inArea && !wasInArea;
        wasInArea = inArea;
        bool itemGone = ItemGone();
        bool itemJustGone = itemGone && !wasItemGone;
        wasItemGone = itemGone;

        switch (Current)
        {
            case State.Waiting:
                if (Started(areaEntered, itemJustGone))
                {
                    Current = State.Delaying;
                    delayUntil = now + delay;
                }
                break;
            case State.Delaying:
                if (now >= delayUntil) Begin();
                break;
            case State.Showing:
                if (holding) Time.timeScale = 0f;   // closing the HUD's pause popup doesn't end the freeze
                if (endingAt < 0f && Ended(now, inArea, itemGone)) BeginEnding();
                UpdateFade(now);
                break;
        }
        startRequested = false;
        endRequested = false;
    }

    bool Started(bool areaEntered, bool itemGone)
    {
        switch (startOn)
        {
            case StartTrigger.LevelStart:
                if (levelStarted) return false;
                levelStarted = true;
                return true;
            case StartTrigger.AfterStep:
                if (afterStep == null || afterStep.TimesFinished <= afterSeen) return false;
                afterSeen = afterStep.TimesFinished;
                return true;
            case StartTrigger.Event: return startRequested;
            case StartTrigger.PlayerEntersArea: return areaEntered;
            case StartTrigger.ItemGone: return itemGone;
            default: return false;   // Manual
        }
    }

    bool Ended(float now, bool inArea, bool itemGone)
    {
        if (dialogue != null && dialogue.IsRunning) return false;   // the conversation decides
        bool settled = now - shownAt >= fadeTime;   // a key held from before can't skip it at once
        switch (endOn)
        {
            case EndTrigger.Seconds: return now - shownAt >= seconds + fadeTime;
            case EndTrigger.AnyKey:
                if (!settled) return false;
                return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            case EndTrigger.PlayerMoves:
                return settled && Vector2.Distance(player.transform.position, playerAtStart) > 0.3f;
            case EndTrigger.PlayerEntersArea: return inArea;
            case EndTrigger.ItemGone: return itemGone;
            case EndTrigger.Event: return endRequested;
            default: return false;   // Manual
        }
    }

    void Begin()
    {
        Current = State.Showing;
        TimesPlayed++;
        shownAt = Time.unscaledTime;
        endingAt = -1f;
        playerAtStart = player != null ? player.transform.position : Vector3.zero;
        foreach (GameObject prop in props) if (prop != null) prop.SetActive(true);
        foreach (CanvasGroup overlay in overlays) if (overlay != null) ShowOverlay(overlay, true);
        if (!showing.Contains(this)) showing.Add(this);
        // A step that waits for the player to move can't stop them from moving
        Hold(freezeGame && endOn != EndTrigger.PlayerMoves && endOn != EndTrigger.PlayerEntersArea);
        UpdateFade(shownAt);
        onStart.Invoke();
        TutorialEvents.Fire(name + ".start");
        if (dialogue != null) dialogue.Play(this);   // it ends the step after its last line
    }

    static void ShowOverlay(CanvasGroup overlay, bool on)
    {
        Canvas canvas = overlay.GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = on;
        overlay.alpha = on ? overlay.alpha : 0f;
    }

    void BeginEnding()
    {
        endingAt = Time.unscaledTime;
        Hold(false);   // the player can go while it fades
    }

    void Finish()
    {
        Visibility = 0f;
        foreach (GameObject prop in props) if (prop != null) prop.SetActive(false);
        foreach (CanvasGroup overlay in overlays) if (overlay != null) ShowOverlay(overlay, false);
        showing.Remove(this);
        if (showing.Count == 0) SetDark(0f, null);
        TimesFinished++;
        Current = maxTimes <= 0 || TimesPlayed < maxTimes ? State.Waiting : State.Done;
        onEnd.Invoke();
        TutorialEvents.Fire(name + ".end");
    }

    void UpdateFade(float now)
    {
        float fadeIn = fadeTime > 0f ? Mathf.Clamp01((now - shownAt) / fadeTime) : 1f;
        float fadeOut = endingAt < 0f ? 1f : fadeTime > 0f ? 1f - Mathf.Clamp01((now - endingAt) / fadeTime) : 0f;
        Visibility = Mathf.Min(fadeIn, fadeOut);

        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null) continue;
            Color c = sprites[i].color;
            c.a = spriteAlphas[i] * Visibility;
            sprites[i].color = c;
        }
        for (int i = 0; i < texts.Length; i++)
            if (texts[i] != null) texts[i].alpha = textAlphas[i] * Visibility;
        foreach (CanvasGroup overlay in overlays) if (overlay != null) overlay.alpha = Visibility;

        if (endingAt >= 0f && Visibility <= 0f) Finish();
    }

    void LateUpdate()
    {
        // The first step on screen sets the shared dark for all of them: the darkest one wins, and
        // the UI holes of every dark step are cut out of it
        if (showing.Count == 0 || showing[0] != this) return;
        float alpha = 0f;
        uiHoleBuffer.Clear();
        foreach (TutorialStep step in showing)
            if (step.darken)
            {
                alpha = Mathf.Max(alpha, step.darkness * step.Visibility);
                uiHoleBuffer.AddRange(step.uiHoles);
            }
        SetDark(alpha, uiHoleBuffer);
    }

    // The arena's dark (a sprite the game camera draws) and the UI's (TutorialUIDark)
    static void SetDark(float alpha, List<TutorialUIHole> uiHoles)
    {
        TutorialUIDark.Show(alpha, uiHoles);
        if (dark == null)
        {
            if (alpha <= 0f) return;
            // Covers far past the arena; it only draws outside the sprite masks (the Holes)
            var go = new GameObject("TutorialDark");
            go.transform.localScale = new Vector3(60f, 40f, 1f);
            dark = go.AddComponent<SpriteRenderer>();
            Texture2D white = Texture2D.whiteTexture;
            dark.sprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            dark.sortingOrder = DarkOrder;
            dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        }
        dark.color = new Color(0f, 0f, 0f, alpha);
    }

    // Steps share one freeze: the game runs again when the last of them lets go
    void Hold(bool on)
    {
        if (on == holding) return;
        holding = on;
        freezes = Mathf.Max(0, freezes + (on ? 1 : -1));
        if (freezes > 0) { Time.timeScale = 0f; return; }
        HUDController hud = FindFirstObjectByType<HUDController>();
        if (hud == null || !hud.IsPaused) Time.timeScale = 1f;   // a pause opened meanwhile stays paused
    }

    bool InArea()
    {
        if (startOn != StartTrigger.PlayerEntersArea && endOn != EndTrigger.PlayerEntersArea) return false;
        Vector2 centre = area != null ? (Vector2)area.position : (Vector2)transform.position;
        return new Rect(centre - areaSize * 0.5f, areaSize).Contains(player.transform.position);
    }

    bool ItemGone()
    {
        if (startOn != StartTrigger.ItemGone && endOn != EndTrigger.ItemGone) return false;
        if (item != null && item.gameObject.activeInHierarchy)
        {
            // Grabbed by a hook counts as gone too (it rides the hook, still active)
            Treasure treasure = item.GetComponent<Treasure>();
            if (treasure == null || !treasure.IsTaken) { itemSeen = true; return false; }
        }
        return itemSeen;
    }

    TutorialStep StepAbove()
    {
        Transform parent = transform.parent;
        if (parent == null) return null;
        for (int i = transform.GetSiblingIndex() - 1; i >= 0; i--)
        {
            TutorialStep step = parent.GetChild(i).GetComponent<TutorialStep>();
            if (step != null) return step;
        }
        return null;
    }

    void OnDrawGizmosSelected()
    {
        if (startOn != StartTrigger.PlayerEntersArea && endOn != EndTrigger.PlayerEntersArea) return;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
        Vector3 centre = area != null ? area.position : transform.position;
        Gizmos.DrawWireCube(centre, areaSize);
    }
}
