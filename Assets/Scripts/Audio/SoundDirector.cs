using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Owner: Claude (sound director)
// The one persistent object that plays every sound in the game. It makes itself when the game
// starts (no scene or prefab edits) and survives scene loads.
//
//   Sfx.Play("hook_grab", position)   a sound effect (SoundCues lists them all)
//   Sfx.Ui("ui_click")                a menu sound
//
// Music: one track per place (Music), crossfaded on scene changes and looped with a crossfade so
// the songs' endings never cut. Ambience: the mine's room tone under every level.
// Volumes: three sliders (music, sound effects, menus) plus master and mute, saved in PlayerPrefs.
// The settings screen only needs SoundDirector.GetVolume / SetVolume / Muted (docs/audio.md).
//
// Like the art layer it never changes gameplay: it only listens (SoundWatcher) and plays, and it
// rolls its own dice (System.Random) so gameplay's UnityEngine.Random sequence is untouched.
public class SoundDirector : MonoBehaviour
{
    public enum Volume { Master, Music, Sfx, Ui }

    static SoundDirector instance;
    public static SoundDirector Instance => instance;

    // --- volume settings (0..1), saved -------------------------------------------------------------
    static readonly string[] PrefKeys = { "sound.master", "sound.music", "sound.sfx", "sound.ui" };
    static readonly float[] Defaults = { 1f, 0.7f, 1f, 1f };
    static readonly float[] volumes = { 1f, 0.7f, 1f, 1f };
    static bool muted;

    public static float GetVolume(Volume which) => volumes[(int)which];
    public static void SetVolume(Volume which, float value)
    {
        volumes[(int)which] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PrefKeys[(int)which], volumes[(int)which]);
    }
    public static bool Muted
    {
        get => muted;
        set { muted = value; PlayerPrefs.SetInt("sound.muted", value ? 1 : 0); }
    }

    static void LoadSettings()
    {
        for (int i = 0; i < volumes.Length; i++) volumes[i] = PlayerPrefs.GetFloat(PrefKeys[i], Defaults[i]);
        muted = PlayerPrefs.GetInt("sound.muted", 0) == 1;
    }

    float BusGain(SoundCues.Bus bus)
    {
        if (muted) return 0f;
        float master = volumes[(int)Volume.Master];
        switch (bus)
        {
            case SoundCues.Bus.Music: return master * volumes[(int)Volume.Music];
            case SoundCues.Bus.Ui: return master * volumes[(int)Volume.Ui];
            default: return master * volumes[(int)Volume.Sfx];
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        LoadSettings();
        var go = new GameObject("SoundDirector");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundDirector>();
        go.AddComponent<SoundWatcher>();
    }

    // --- sound effects -------------------------------------------------------------------------------
    const int PoolSize = 24;
    readonly List<AudioSource> pool = new List<AudioSource>(PoolSize);
    readonly Dictionary<AudioSource, string> playing = new Dictionary<AudioSource, string>();
    readonly Dictionary<AudioSource, float> baseGain = new Dictionary<AudioSource, float>();   // before the bus
    readonly Dictionary<AudioSource, SoundCues.Bus> busOf = new Dictionary<AudioSource, SoundCues.Bus>();
    readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
    readonly Dictionary<string, int> lastTake = new Dictionary<string, int>();
    readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
    readonly System.Random dice = new System.Random();
    float duckUntil, duckDepth;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // a 2D game: positions only pan a little left/right
            pool.Add(s);
        }
        musicA = MakeMusicSource();
        musicB = MakeMusicSource();
        ambience = gameObject.AddComponent<AudioSource>();
        ambience.playOnAwake = false;
        ambience.loop = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    AudioClip[] Clips(string cue)
    {
        if (clips.TryGetValue(cue, out AudioClip[] found)) return found;
        found = Resources.LoadAll<AudioClip>("Audio/Sfx/" + cue);
        System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
        clips[cue] = found;
        if (found.Length == 0) Debug.LogWarning("SoundDirector: no clips for cue '" + cue + "'");
        return found;
    }

    float Roll(float range) => range <= 0f ? 0f : ((float)dice.NextDouble() * 2f - 1f) * range;

    // Play a cue. `at` (world position) pans it a little toward its side of the screen.
    public void PlayCue(string id, Vector3? at = null, float volumeScale = 1f, float pitchScale = 1f)
    {
        if (string.IsNullOrEmpty(id) || !SoundCues.All.TryGetValue(id, out SoundCues.Cue cue)) return;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(id, out float last) && now - last < cue.gap) return;

        int sounding = 0;
        AudioSource oldest = null;
        foreach (var pair in playing)
            if (pair.Value == id && pair.Key.isPlaying)
            {
                sounding++;
                if (oldest == null || pair.Key.time > oldest.time) oldest = pair.Key;
            }
        if (sounding >= cue.voices && oldest != null) oldest.Stop();   // steal the oldest copy

        AudioClip[] takes = Clips(id);
        if (takes.Length == 0) return;
        int take = dice.Next(takes.Length);
        if (takes.Length > 1 && lastTake.TryGetValue(id, out int prev) && take == prev) take = (take + 1) % takes.Length;
        lastTake[id] = take;

        AudioSource source = FreeSource();
        source.clip = takes[take];
        baseGain[source] = cue.volume * volumeScale;
        busOf[source] = cue.bus;
        source.volume = baseGain[source] * BusGain(cue.bus);
        source.pitch = Mathf.Max(0.1f, (1f + Roll(cue.pitch)) * pitchScale);
        source.panStereo = at.HasValue ? Pan(at.Value) : 0f;
        source.ignoreListenerPause = cue.bus == SoundCues.Bus.Ui;
        source.Play();
        playing[source] = id;
        lastPlayed[id] = now;
        Played?.Invoke(id);

        if (cue.duck > 0f)
        {
            duckDepth = Mathf.Max(DuckingNow ? duckDepth : 0f, cue.duck);
            duckUntil = Mathf.Max(duckUntil, now + source.clip.length / source.pitch);
        }
    }

    bool DuckingNow => Time.unscaledTime < duckUntil;

    AudioSource FreeSource()
    {
        AudioSource best = pool[0];
        foreach (AudioSource s in pool)
        {
            if (!s.isPlaying) return s;
            if (s.time > best.time) best = s;   // all busy: reuse the one furthest along
        }
        best.Stop();
        return best;
    }

    static float Pan(Vector3 at)
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return 0f;
        float half = cam.orthographicSize * cam.aspect;
        if (half <= 0.01f) return 0f;
        return Mathf.Clamp((at.x - cam.transform.position.x) / half, -1f, 1f) * 0.35f;
    }

    // --- music ---------------------------------------------------------------------------------------
    // Track files in Resources/Audio/Music, already trimmed and levelled to the same loudness
    public const string TitleTrack = "title";       // 01 Under the Dirt Map: the main menu
    public const string ShopTrack = "shop";         // 01 jazz: store, character pick, card map
    public const string LairTrack = "lair";         // 03 jazz: the evil cat's world map (hub)
    public const string BattleTrack = "battle";     // 02 jazz: levels and tutorials
    public const string EndlessTrack = "endless";   // 02: survival and roam (endless waves)
    public const string FinaleTrack = "finale";     // 03: the last level

    const float MusicLevel = 0.8f;       // music under effects
    const float CrossfadeTime = 1.5f;
    const float LoopOverlap = 3f;        // seconds of crossfade when a song wraps around
    AudioSource musicA, musicB, ambience;
    AudioSource musicNow;
    string trackNow;
    float fadeStart = -1f;
    AudioSource fadingOut;
    float roundEndDip = 1f;              // the music sits lower on the result screen
    string ambienceNow;

    AudioSource MakeMusicSource()
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;   // looped by hand with a crossfade (Update)
        s.ignoreListenerPause = true;
        s.volume = 0f;
        return s;
    }

    public void PlayMusic(string track)
    {
        if (track == trackNow) return;
        trackNow = track;
        AudioClip clip = string.IsNullOrEmpty(track) ? null : Resources.Load<AudioClip>("Audio/Music/" + track);
        StartMusic(clip, 0f);
    }

    void StartMusic(AudioClip clip, float from)
    {
        AudioSource next = musicNow == musicA ? musicB : musicA;
        fadingOut = musicNow;
        musicNow = clip != null ? next : null;
        if (clip != null)
        {
            next.Stop();
            next.clip = clip;
            next.time = Mathf.Clamp(from, 0f, clip.length - 0.1f);
            next.volume = 0f;
            next.Play();
        }
        fadeStart = Time.unscaledTime;
    }

    public void SetRoundEndDip(float level) { roundEndDip = Mathf.Clamp01(level); }

    // For tests and debugging
    public static event System.Action<string> Played;   // every cue that actually started
    public bool CuePlaying(string id)
    {
        foreach (var pair in playing) if (pair.Value == id && pair.Key.isPlaying) return true;
        return false;
    }
    public string CurrentTrack => trackNow;
    public bool MusicPlaying => musicNow != null && musicNow.isPlaying;
    public string CurrentAmbience => ambienceNow;
    public int VoicesPlaying { get { int n = 0; foreach (AudioSource s in pool) if (s.isPlaying) n++; return n; } }

    void UpdateMusic()
    {
        float bus = BusGain(SoundCues.Bus.Music) * MusicLevel * roundEndDip;
        if (Time.timeScale == 0f) bus *= 0.5f;                   // paused: the music steps back
        if (DuckingNow) bus *= 1f - duckDepth;
        float t = fadeStart < 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - fadeStart) / CrossfadeTime);

        if (musicNow != null) musicNow.volume = bus * t;
        if (fadingOut != null)
        {
            fadingOut.volume = bus * (1f - t);
            if (t >= 1f) { fadingOut.Stop(); fadingOut = null; }
        }

        // Wrap a song round with an overlap, so its ending flows back into its start
        if (musicNow != null && musicNow.clip != null && fadingOut == null
            && musicNow.time >= musicNow.clip.length - LoopOverlap)
            StartMusic(musicNow.clip, 0f);
        // A hitch (or a background browser tab) can skip past that window and let the song end:
        // start it again rather than fall silent
        else if (musicNow != null && musicNow.clip != null && fadingOut == null && !musicNow.isPlaying
                 && musicNow.clip.loadState == AudioDataLoadState.Loaded)
            StartMusic(musicNow.clip, 0f);
    }

    // --- ambience: the mine under every level ------------------------------------------------------
    public void PlayAmbience(string cue, float level)
    {
        ambienceLevel = level;
        if (cue == ambienceNow) return;
        ambienceNow = cue;
        ambience.Stop();
        if (string.IsNullOrEmpty(cue)) return;
        AudioClip[] takes = Clips(cue);
        if (takes.Length == 0) return;
        ambience.clip = takes[0];
        ambience.time = (float)dice.NextDouble() * ambience.clip.length * 0.9f;
        ambience.Play();
    }
    float ambienceLevel;

    // --- every frame ---------------------------------------------------------------------------------
    void Update()
    {
        UpdateMusic();
        ambience.volume = BusGain(SoundCues.Bus.Sfx) * ambienceLevel;
        if (Time.timeScale == 0f) ambience.volume *= 0.5f;

        // Slider and mute changes apply to sounds that are already playing too
        foreach (AudioSource s in pool)
            if (s.isPlaying && baseGain.TryGetValue(s, out float gain))
                s.volume = gain * BusGain(busOf[s]);

        UiClicks();
    }

    // Every uGUI button in the game (HUD, store, map, dialogue choices) clicks, with no per-button set-up
    GameObject hovered;
    readonly List<RaycastResult> hits = new List<RaycastResult>();
    void UiClicks()
    {
        EventSystem events = EventSystem.current;
        if (events == null) { hovered = null; return; }
        var pointer = new PointerEventData(events) { position = Input.mousePosition };
        hits.Clear();
        events.RaycastAll(pointer, hits);
        Selectable target = null;
        foreach (RaycastResult hit in hits)
        {
            if (hit.gameObject == null) continue;
            target = hit.gameObject.GetComponentInParent<Selectable>();
            break;   // only the topmost thing under the pointer counts
        }
        bool live = target != null && target.IsInteractable() && target.isActiveAndEnabled;
        GameObject now = live ? target.gameObject : null;
        if (now != null && now != hovered) PlayCue("ui_hover");
        hovered = now;
        if (live && Input.GetMouseButtonDown(0)) PlayCue("ui_click");
    }

    // --- scenes -> music and ambience ---------------------------------------------------------------
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        roundEndDip = 1f;
        pendingPlace = 2;   // decide next frames, once MapHub / LevelLoader / GameManager are awake
    }
    int pendingPlace;

    void LateUpdate()
    {
        if (pendingPlace > 0 && --pendingPlace == 0) ChoosePlace();
    }

    void ChoosePlace()
    {
        string scene = SceneManager.GetActiveScene().name;
        string level = LevelLoader.Requested != null ? LevelLoader.Requested.name : scene;
        bool round = GameManager.Instance != null;

        if (scene == GameFlow.BootScene) return;
        if (scene == GameFlow.MenuScene) { PlayMusic(TitleTrack); PlayAmbience(null, 0f); return; }
        if (scene == GameFlow.StoreScene || scene == GameFlow.RolePickScene || scene == GameFlow.MapScene
            || scene == "RolePick")
        { PlayMusic(ShopTrack); PlayAmbience(null, 0f); return; }
        if (MapHub.Active != null || level.Contains("world_map") || level.Contains("maze"))
        { PlayMusic(LairTrack); PlayAmbience("amb_mine", 0.18f); return; }
        if (scene == GameFlow.SurvivalScene || level.Contains("survival") || level.Contains("roam"))
        { PlayMusic(EndlessTrack); PlayAmbience("amb_mine", 0.22f); return; }
        if (level.Contains("level_08"))
        { PlayMusic(FinaleTrack); PlayAmbience("amb_mine", 0.22f); return; }
        if (round || scene.StartsWith("level_") || scene == GameFlow.GameScene)
        { PlayMusic(BattleTrack); PlayAmbience("amb_mine", 0.25f); return; }
        PlayMusic(ShopTrack);
        PlayAmbience(null, 0f);
    }
}

// The short way to play a sound from anywhere. Safe to call in edit mode, tests, or before the
// director exists (it just does nothing).
public static class Sfx
{
    public static void Play(string cue) => Safe(cue, null, 1f, 1f);
    public static void Play(string cue, Vector3 at) => Safe(cue, at, 1f, 1f);
    public static void Play(string cue, Vector3 at, float volumeScale, float pitchScale = 1f) => Safe(cue, at, volumeScale, pitchScale);
    public static void Ui(string cue) => Safe(cue, null, 1f, 1f);

    // These calls sit inside gameplay code: a sound problem must never break the line it sits on
    static void Safe(string cue, Vector3? at, float volumeScale, float pitchScale)
    {
        if (SoundDirector.Instance == null) return;
        try { SoundDirector.Instance.PlayCue(cue, at, volumeScale, pitchScale); }
        catch (System.Exception e) { Debug.LogException(e); }
    }
}
