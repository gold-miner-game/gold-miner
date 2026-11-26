// Copied only into the disposable art-capture project by capture.ps1; never part of the game.
// Started with "-artcapture <folder>", it opens each scene named by "-artscenes A,B,C", waits
// "-artwait" seconds of real time, saves a full-screen PNG (sprites, uGUI and IMGUI alike) and quits.
// "-artframes N" also saves N more frames, one per stop-motion pose (StopMotion.Pose), per scene (NAME_000.png...) to check motion.
// "-artpeel" presses the menu's main button (MainMenu.StartGame) before the Menu frames.
// A scene entry "NAME:action" also triggers a moment a plain load never shows, through the game's own
// methods, then waits "-arttriggerwait" seconds (default 2.5) and saves NAME-action.png:
//   win / fail / crush   GameManager.WinRound / EndRound(false) / Crush: result panels, CAUGHT
//   reward               SkillUnlockSummary.PlayEveryTime, then a win: the skill reward demo
//   pause                HUDController.OpenPause: the Pause card ("Quit Game?" on the world map)
//   confirm              MainMenu page = ConfirmNew: the New Game confirmation
//   tutorial             LevelLoader.ScriptedTutorial = true before the load, as GameFlow does
//   focus                MapHub.FocusPortal on the first portal: the reveal spotlight
//   target / hurt        HUDController.CelebrateTarget / FlashDamage: the Target reached banner, the red edge flash
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ArtCaptureRunner : MonoBehaviour
{
    string outDir;
    string[] scenes;
    float wait = 3f;
    int frames;
    bool peel;
    int videoFps;
    float triggerWait = 2.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string dir = null, list = null;
        float seconds = 3f;
        int count = 0;
        bool pressPeel = false;
        float triggerSeconds = 2.5f;
        int videoFps = 0;
        for (int i = 0; i < args.Length; i++)
        {
            bool hasValue = i < args.Length - 1;
            if (args[i] == "-artcapture" && hasValue) dir = args[i + 1];
            else if (args[i] == "-artscenes" && hasValue) list = args[i + 1];
            else if (args[i] == "-artwait" && hasValue) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            else if (args[i] == "-artframes" && hasValue) int.TryParse(args[i + 1], out count);
            else if (args[i] == "-artpeel") pressPeel = true;
            else if (args[i] == "-arttriggerwait" && hasValue) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out triggerSeconds);
            else if (args[i] == "-artvideo" && hasValue) int.TryParse(args[i + 1], out videoFps);
        }
        if (string.IsNullOrEmpty(dir)) return;
        var go = new GameObject("Art Capture");
        DontDestroyOnLoad(go);
        var runner = go.AddComponent<ArtCaptureRunner>();
        runner.outDir = dir;
        runner.scenes = (list ?? "Menu").Split(',');
        runner.wait = seconds;
        runner.frames = count;
        runner.peel = pressPeel;
        runner.videoFps = videoFps;
        // "-artvideo FPS": the game clock advances exactly 1/FPS per rendered frame and every frame is saved,
        // so saving screenshots can never slow the game: the frames play back at FPS as the game really looks.
        if (videoFps > 0) Time.captureFramerate = videoFps;
        runner.triggerWait = triggerSeconds;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(outDir);
        Application.runInBackground = true;
        // Boot's own Start sends the game to the Menu in this first frame; let that load go first
        yield return null;
        yield return null;
        foreach (string raw in scenes)
        {
            string entry = raw.Trim();
            if (entry.Length == 0) continue;
            string[] parts = entry.Split(':');
            string scene = parts[0], action = parts.Length > 1 ? parts[1] : null;
            if (action == "reward") SkillUnlockSummary.PlayEveryTime = true;
            LevelLoader.ScriptedTutorial = action == "tutorial" ? true : (bool?)null;
            if (scene == "camp")
            {
                // the character camp (CampHub) is a level in the Game scene, opened like GameFlow's Pick Character node
                LevelLoader.ScriptedTutorial = true;
                LevelLoader.Requested = Resources.Load<TextAsset>("Hub/camp");
                LevelLoader.WaitToAdvance = true;
                LevelLoader.AdvanceNow = false;
                SceneManager.LoadScene("Game");
            }
            else SceneManager.LoadScene(scene);
            yield return Hold(wait);
            if (action != null)
            {
                Trigger(action);
                yield return Hold(triggerWait);
                scene = scene + "-" + action;
            }
            yield return Shot(scene);
            if (frames <= 0) continue;
            if (peel && scene == "Menu")
            {
                var menu = FindFirstObjectByType<MainMenu>();
                MethodInfo start = typeof(MainMenu).GetMethod("StartGame", BindingFlags.Instance | BindingFlags.NonPublic);
                if (menu != null && start != null) start.Invoke(menu, null);
            }
            string loaded = parts[0] == "camp" ? "Game" : parts[0];   // the camp is a level in the Game scene
            // "NAME:walk": hold the real right-arrow key (Input System) while saving frames at 30 a second, to
            // see whether moving pieces step on the stop-motion beat or glide.
            bool walk = action == "walk";
            if (walk && UnityEngine.InputSystem.Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            if (walk && UnityEngine.InputSystem.Keyboard.current != null)
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.RightArrow));
            for (int f = 0; f < frames; f++)
            {
                float next = Time.realtimeSinceStartup + (walk ? 1f / 30f : 1f / StopMotion.Pose);
                yield return Shot(scene + "_" + f.ToString("000"));
                if (videoFps <= 0) while (Time.realtimeSinceStartup < next) yield return null;
                if (SceneManager.GetActiveScene().name != loaded && !(peel && scene == "Menu")) break;
            }
            if (walk && UnityEngine.InputSystem.Keyboard.current != null)
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }
        Debug.Log("ART_CAPTURE_DONE");
        Application.Quit();
    }

    // Real seconds normally; in video mode the same span of GAME time, counted in locked frames.
    IEnumerator Hold(float seconds)
    {
        if (videoFps > 0)
        {
            int n = Mathf.RoundToInt(seconds * videoFps);
            for (int i = 0; i < n; i++) yield return null;
            yield break;
        }
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    static void Trigger(string action)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager gm = GameManager.Instance;
        switch (action)
        {
            case "win":
            case "reward":
                if (gm != null) gm.WinRound("art capture");
                break;
            case "fail":
                if (gm != null) typeof(GameManager).GetMethod("EndRound", Private)?.Invoke(gm, new object[] { false, "art capture" });
                break;
            case "crush":
                if (gm != null) gm.Crush("art capture");
                break;
            case "pause":
                var hud = FindFirstObjectByType<HUDController>();
                if (hud != null) typeof(HUDController).GetMethod("OpenPause", Private)?.Invoke(hud, null);
                break;
            case "target":   // the Target reached banner and flag swell (HUDController display methods)
            case "hurt":     // the lost-life edge flash
                var cue = FindFirstObjectByType<HUDController>();
                if (cue != null) typeof(HUDController).GetMethod(action == "target" ? "CelebrateTarget" : "FlashDamage", Private)?.Invoke(cue, null);
                break;
            case "focus":
                var hub = FindFirstObjectByType<MapHub>();
                var portal = FindFirstObjectByType<HubPortal>();
                if (hub != null && portal != null) hub.FocusPortal(portal);
                break;
            case "walk": break;   // handled while saving frames
            case "radio":       // back to the radio, skip a song, open the Sound card (back / next enabled, a fresh tag)
                MusicLibrary.Select(null);
                SoundDirector.NextSong();
                SoundPanel.Show();
                break;
            case "levelsong":   // mid-level: back to the radio and skip a song, so the now-playing tag shows over the level HUD
                MusicLibrary.Select(null);
                SoundDirector.NextSong();
                break;
            case "mute":        // the Sound card muted; sound comes back on after the shot, so later runs aren't silent
                SoundDirector.Muted = true;
                SoundPanel.Show();
                new GameObject("Capture unmute").AddComponent<CaptureUnmute>();
                break;
            case "sound":       // the papercraft Sound card (SoundPanel)
                SoundPanel.Show();
                break;
            case "pausesound":  // the pause popup's Sound button: the card over the hidden pause card
                var hudForSound = FindFirstObjectByType<HUDController>();
                if (hudForSound != null)
                {
                    typeof(HUDController).GetMethod("OpenPause", Private)?.Invoke(hudForSound, null);
                    typeof(HUDController).GetMethod("OpenSoundFromPause", Private)?.Invoke(hudForSound, null);
                }
                break;
            case "musicpick":   // take and pick a free song first, so the store shows its Playing / picked stamps
                MusicLibrary.Buy("hog_wild_swing");
                MusicLibrary.Select("hog_wild_swing");
                MusicStorePanel.Show(null, null);
                break;
            case "musicstore":  // the music store panel (the radio picked, nothing bought)
                MusicStorePanel.Show(null, null);
                break;
            case "needle":   // the sprint-stab cat's look (no gameplay yet): its review cycle beside the player (film it on world_map: no hooks there)
                var who = FindFirstObjectByType<PlayerCharacter>();
                if (who != null)
                {
                    var cat = new GameObject("NeedleMeowmeow (capture)");
                    cat.transform.position = who.transform.position + new Vector3(-2.2f, 1.6f, 0f);
                    var needle = cat.AddComponent<NeedleMeowmeowArt>();
                    needle.telegraphLength = 4.4f;
                    needle.previewCycle = true;
                }
                break;
            case "round":   // roam: put the player a few steps from the nearest walking hook hole (Red Round / Gold Round) and wake it
                var walkers = FindObjectsByType<RedEnemy>(FindObjectsSortMode.None);
                var hero = FindFirstObjectByType<PlayerCharacter>();
                RedEnemy closest = null;
                foreach (RedEnemy w in walkers)
                    if (!(w is RedSquare) && hero != null && (closest == null || (w.transform.position - hero.transform.position).sqrMagnitude < (closest.transform.position - hero.transform.position).sqrMagnitude)) closest = w;
                if (closest != null)
                {
                    Vector2 spot = (Vector2)closest.transform.position + Vector2.left * 3.5f;
                    var rb2 = hero.GetComponent<Rigidbody2D>();
                    if (rb2 != null) rb2.position = spot;
                    hero.transform.position = spot;
                    closest.WakeUp();
                    // no level places a Gold Round yet: add one through the loader's own spawner, to review its look
                    var loader = FindFirstObjectByType<LevelLoader>();
                    MethodInfo addGold = typeof(LevelLoader).GetMethod("AddGoldRound", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (loader != null && addGold != null) addGold.Invoke(loader, new object[] { spot + new Vector2(2.5f, -2.2f) });
                }
                break;
            case "brawlerfreeze":   // the brawler set-up, then real frost breathed on that square (FrostField2D.Expose, as Elsa's breath does)
                Trigger("brawler");
                new GameObject("Capture frost").AddComponent<CaptureFrost>();
                break;
            case "gate":    // camp: step away from the hatch (arming it), then onto it: back to the map
                new GameObject("Capture gate walk").AddComponent<CaptureGateWalk>();
                break;
            case "switch":  // camp: walk up to JK. Rolling and press Enter (play as them: the camp re-opens)
            case "talk":    // camp: walk up to JK. Rolling and press E (talk)
            case "hire":    // camp: walk up to Hulk (for hire), card only
            case "elsa":    // camp: walk up to Elsa at her igloo, card only
            case "scrat":   // camp: walk up to Scrat at his stump, card only
            case "soon":    // camp: walk up to Rero Rero (not released) and press E
                // whichever of the two starters is a friend here (the other is the one you play)
                GameObject starter = GameObject.Find("Camp JK. Rolling");
                if (starter == null) starter = GameObject.Find("Camp Mr. Hyde");
                var npc = action == "talk" || action == "switch" ? starter
                        : GameObject.Find(action == "hire" ? "Camp Hulk" : action == "elsa" ? "Camp Elsa" : action == "scrat" ? "Camp Scrat" : "Camp Rero Rero");
                var me = FindFirstObjectByType<PlayerCharacter>();
                if (npc != null && me != null)
                {
                    Vector2 by = (Vector2)npc.transform.position + new Vector2(-1.1f, 0f);
                    var meBody = me.GetComponent<Rigidbody2D>();
                    if (meBody != null) meBody.position = by;
                    me.transform.position = by;
                    if (action == "talk" || action == "switch" || action == "soon") new GameObject("Capture key").AddComponent<CaptureKey>().key =
                        action == "switch" ? UnityEngine.InputSystem.Key.Enter : UnityEngine.InputSystem.Key.E;
                }
                break;
            case "brawler":   // roam: put the player a few steps from the nearest Red Square and wake it, to see it act
                var squares = FindObjectsByType<RedSquare>(FindObjectsSortMode.None);
                var pc = FindFirstObjectByType<PlayerCharacter>();
                if (squares.Length > 0 && pc != null)
                {
                    RedSquare near = squares[0];
                    foreach (RedSquare s in squares)
                        if ((s.transform.position - pc.transform.position).sqrMagnitude < (near.transform.position - pc.transform.position).sqrMagnitude) near = s;
                    Vector2 spot = (Vector2)near.transform.position + Vector2.left * 3f;
                    var rb = pc.GetComponent<Rigidbody2D>();
                    if (rb != null) rb.position = spot;
                    pc.transform.position = spot;
                    near.WakeUp();
                }
                break;
            case "time": Debug.Log("ART_CAPTURE_TIME " + StoreView.GrantConsoleItem("Time Machine")); break;   // the console's good Time Machine
            case "shield": Debug.Log("ART_CAPTURE_SHIELD " + StoreView.GrantConsoleItem("Shield")); break;   // the console's good Shield
            case "confirm":
                var menu = FindFirstObjectByType<MainMenu>();
                FieldInfo page = typeof(MainMenu).GetField("page", Private);
                if (menu != null && page != null) page.SetValue(menu, Enum.Parse(page.FieldType, "ConfirmNew"));
                break;
        }
        Debug.Log("ART_CAPTURE_TRIGGER " + action);
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), shot.EncodeToPNG());
        Destroy(shot);
        Debug.Log("ART_CAPTURE_SAVED " + name);
    }
}

// Capture only: turns mute back off a moment after the "mute" shot
public class CaptureUnmute : MonoBehaviour
{
    float start;
    void Start() { start = Time.unscaledTime; DontDestroyOnLoad(gameObject); }   // survives the next capture scene
    void Update() { if (Time.unscaledTime - start > 2f) { SoundDirector.Muted = false; PlayerPrefs.Save(); Destroy(gameObject); } }
}

// Capture only: after a moment, breathes frost from the player onto the nearest Red Square for a
// second (the same FrostField2D.Expose call Elsa's breath makes), to film a frozen enemy.
public class CaptureFrost : MonoBehaviour
{
    float start;
    void Start() { start = Time.time; }
    void Update()
    {
        float t = Time.time - start;
        if (t < 1.0f) return;
        if (t > 2.0f) { Destroy(gameObject); return; }
        var pc = FindFirstObjectByType<PlayerCharacter>();
        if (pc == null) return;
        RedSquare near = null;
        foreach (RedSquare s in FindObjectsByType<RedSquare>(FindObjectsSortMode.None))
            if (near == null || (s.transform.position - pc.transform.position).sqrMagnitude < (near.transform.position - pc.transform.position).sqrMagnitude) near = s;
        if (near == null) return;
        Vector2 from = pc.transform.position;
        Vector2 to = near.transform.position;
        FrostField2D.Expose(pc.transform, from, to - from, Vector2.Distance(from, to) + 1.5f, 30f);
// Capture only: taps E a moment later (the camp's talk key), through the Input System like a player would
public class CaptureKey : MonoBehaviour
{
    public UnityEngine.InputSystem.Key key = UnityEngine.InputSystem.Key.E;
    float start;
    int stage;
    void Start() { start = Time.unscaledTime; }
    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        float t = Time.unscaledTime - start;
        if (stage == 0 && t > 0.4f) { UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key)); stage = 1; }
        else if (stage == 1 && t > 0.5f) { UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState()); Destroy(gameObject); }
    }
}

// Capture only: in the camp, stands the player in the middle (arming the gate), then on the hatch
public class CaptureGateWalk : MonoBehaviour
{
    float start;
    int stage;
    void Start() { start = Time.unscaledTime; }
    void Update()
    {
        var me = FindFirstObjectByType<PlayerCharacter>();
        var gate = GameObject.Find("Camp Gate");
        if (me == null || gate == null) return;
        float t = Time.unscaledTime - start;
        Vector2 to = stage == 0 ? (Vector2)gate.transform.position + new Vector2(3f, 0f) : (Vector2)gate.transform.position;
        if (stage == 0 && t > 0.3f) stage = 1;
        var body = me.GetComponent<Rigidbody2D>();
        if (body != null) body.position = to;
        me.transform.position = to;
        if (stage == 1 && t > 0.6f) Destroy(gameObject);
    }
}
