using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Available in every scene and player build without adding anything to saved scenes.
[DefaultExecutionOrder(-10000)]
public class DeveloperConsole : MonoBehaviour
{
    static DeveloperConsole instance;
    static int consumedFrame = -1;
    public static bool IsOpen => instance != null && instance.open;
    public static bool CapturesInput => IsOpen || (instance != null && instance.checkPaused) || consumedFrame == Time.frameCount;

    readonly List<EventSystem> suspended = new List<EventSystem>();
    Keyboard keyboard;
    bool open;
    bool checkMode = false;
    bool checkPaused;
    float scaleBeforeCheckPause;
    float previousTimeScale;
    string command = "";
    const string Commands = "Available commands: checkmode <True|False>, stopmotion <on|off>, money <amount>, pass, good <item_name>, unlock. Tab completes a unique name.";
    string feedback = Commands;
    string CheckModeStatus => "checkmode: " + (checkMode ? "TRUE" : "FALSE");
    string StopMotionStatus => StopMotion.Enabled ? "stopmotion: ON (12 poses/s)" : "stopmotion: OFF (smooth, gait off)";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; consumedFrame = -1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance == null) new GameObject("Developer Console").AddComponent<DeveloperConsole>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += SceneLoaded;
    }

    void Update()
    {
        if (keyboard != Keyboard.current)
        {
            if (keyboard != null) keyboard.onTextInput -= ReceiveText;
            keyboard = Keyboard.current;
            if (keyboard != null) keyboard.onTextInput += ReceiveText;
        }
        if (keyboard == null) return;
        if (keyboard.backquoteKey.wasPressedThisFrame)
        {
            SetOpen(!open);
            return;
        }
        if (!open)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) ToggleCheckPause();
            return;
        }
        if (keyboard.escapeKey.wasPressedThisFrame) { SetOpen(false); return; }
        if (keyboard.backspaceKey.wasPressedThisFrame && command.Length > 0)
            command = command.Substring(0, command.Length - 1);
        if (keyboard.tabKey.wasPressedThisFrame) CompleteCommand();
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            ExecuteCommand();
    }

    void ReceiveText(char character)
    {
        if (!open || char.IsControl(character) || character == '`' || character == '~') return;
        if (command.Length < 120) command += character;
    }

    void SetOpen(bool value)
    {
        consumedFrame = Time.frameCount;
        open = value;
        if (value)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            SuspendUI();
        }
        else
        {
            foreach (EventSystem system in suspended)
                if (system != null) system.enabled = true;
            suspended.Clear();
            Time.timeScale = previousTimeScale;
        }
    }

    void SuspendUI()
    {
        foreach (EventSystem system in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            if (system.enabled) { suspended.Add(system); system.enabled = false; }
    }

    void LateUpdate()
    {
        if (checkPaused) Time.timeScale = 0f;
        if (!open) return;
        Time.timeScale = 0f;
        SuspendUI();
    }

    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (open) SetOpen(false);
        if (checkPaused) { checkPaused = false; Time.timeScale = 1f; }
    }

    void ToggleCheckPause()
    {
        GameManager round = GameManager.Instance;
        if (!checkMode || open || round == null || !round.IsPlaying) return;
        HUDController hud = FindFirstObjectByType<HUDController>();
        if (!checkPaused && hud != null && hud.IsPaused) return;
        consumedFrame = Time.frameCount;
        if (checkPaused) ResumeCheckPause();
        else
        {
            scaleBeforeCheckPause = Time.timeScale;
            checkPaused = true;
            Time.timeScale = 0f;
        }
    }

    void ResumeCheckPause()
    {
        if (!checkPaused) return;
        checkPaused = false;
        // Closing the console must restore the resumed clock, rather than reapply the pause.
        if (open) previousTimeScale = scaleBeforeCheckPause;
        else Time.timeScale = scaleBeforeCheckPause;
    }

    void ExecuteCommand()
    {
        string entered = command.Trim();
        command = "";
        if (entered.Length == 0) return;
        int separator = entered.IndexOfAny(new[] { ' ', '\t' });
        string verb = separator < 0 ? entered : entered.Substring(0, separator);
        string argument = separator < 0 ? "" : entered.Substring(separator).Trim();
        if (string.Equals(verb, "stopmotion", StringComparison.OrdinalIgnoreCase))
        {
            bool turnOn = string.Equals(argument, "on", StringComparison.OrdinalIgnoreCase);
            if (!turnOn && !string.Equals(argument, "off", StringComparison.OrdinalIgnoreCase))
            {
                feedback = "Usage: stopmotion on or stopmotion off. " + StopMotionStatus;
                return;
            }
            StopMotion.Enabled = turnOn;
            feedback = StopMotionStatus + ".";
            return;
        }
        if (string.Equals(verb, "checkmode", StringComparison.OrdinalIgnoreCase))
        {
            if (!bool.TryParse(argument, out bool enabled))
            {
                feedback = "Usage: checkmode True or checkmode False. " + CheckModeStatus;
                return;
            }
            checkMode = enabled;
            if (!checkMode) ResumeCheckPause();
            feedback = CheckModeStatus + (checkMode
                ? ". Close the console; Space pauses/continues gameplay."
                : ". Space no longer pauses/continues gameplay.");
            return;
        }
        if (string.Equals(verb, "money", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                feedback = "Usage: money <amount> (a whole number, e.g. money 1000).";
                return;
            }
            CrossLevelStatus player = CrossLevelStatus.Ensure();
            long balance = (long)player.getWallet() + amount;
            if (balance < int.MinValue || balance > int.MaxValue)
            {
                feedback = "That amount would exceed the wallet's supported range. Balance unchanged.";
                return;
            }
            player.addWallet(amount);
            StoreView.RefreshWallet();
            feedback = "Wallet: " + player.WalletLabel() + ".";
            return;
        }
        if (string.Equals(entered, "pass", StringComparison.OrdinalIgnoreCase))
        {
            LevelLoader loader = FindFirstObjectByType<LevelLoader>();
            GameManager round = GameManager.Instance;
            if (loader == null || loader.IsMazeLevel || MapHub.Active != null || round == null || !round.IsPlaying)
            {
                feedback = "pass requires an active level or tutorial.";
                return;
            }
            feedback = "Level passed. All remaining treasure collected and banked.";
            SetOpen(false);
            loader.PassCurrentLevel();
            return;
        }
        if (string.Equals(verb, "good", StringComparison.OrdinalIgnoreCase))
        {
            feedback = StoreView.GrantConsoleItem(argument);
            return;
        }
        if (!string.Equals(entered, "unlock", StringComparison.OrdinalIgnoreCase))
        {
            feedback = "Unknown command: " + entered + "\n" + Commands;
            return;
        }
        GameUnlocks.UnlockEverything();
        feedback = "Available content unlocked. Hidden characters stay locked. Saved. Opening the map.";
        SetOpen(false);
        GameFlow.Instance.OpenMap();
        // Old scene teardown may release a tutorial freeze; leave the new map running.
        Time.timeScale = 1f;
    }

    void CompleteCommand()
    {
        string entered = command.TrimStart();
        var candidates = new List<string> { "checkmode True", "checkmode False", "stopmotion on", "stopmotion off", "money ", "pass" };
        foreach (string name in StoreView.ConsoleItemNames()) candidates.Add("good " + name);
        candidates.Add("unlock");
        var matches = candidates.FindAll(candidate => candidate.StartsWith(entered, StringComparison.OrdinalIgnoreCase));
        if (matches.Count == 1)
        {
            command = matches[0];
            feedback = "Press Enter to run: " + command;
        }
        else if (matches.Count == 0) feedback = "No completion found. " + Commands;
        else
        {
            string common = matches[0];
            foreach (string match in matches)
                while (!match.StartsWith(common, StringComparison.OrdinalIgnoreCase))
                    common = common.Substring(0, common.Length - 1);
            if (common.Length > entered.Length) command = common;
            feedback = "Matches: " + string.Join(", ", matches);
        }
    }

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -10000;
        ScreenFrame.BeginGUI();
        GUI.ModalWindow(GetInstanceID(), new Rect(0, 0, ScreenFrame.GuiWidth, ScreenFrame.GuiHeight),
            DrawConsole, "Developer Console");
    }

    void DrawConsole(int id)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, richText = false };
        GUI.Label(new Rect(28, 45, 1224, 80), feedback, style);
        GUI.Box(new Rect(24, 136, 1232, 48), GUIContent.none);
        GUI.Label(new Rect(36, 142, 1200, 40), "> " + command + "_", style);
        GUI.Label(new Rect(28, 205, 1200, 210),
            "checkmode <True|False> — enable/disable Space pause/continue (default: FALSE).\n" +
            "stopmotion <on|off> — held art poses and walk gait (default: on); off runs smooth.\n" +
            "money <amount> — add to the wallet (e.g. money 1000).\n" +
            "pass — win this level/tutorial and bank all remaining treasure.\n" +
            "good <item_name> — grant a free effect (e.g. good Time Machine). Tab completes a unique name.\n" +
            "unlock — unlock available content and open the map; hidden characters stay locked (saved).", style);
        var statusStyle = new GUIStyle(style);
        statusStyle.normal.textColor = new Color(0.25f, 0.6f, 1f);
        GUI.Label(new Rect(28, 430, 300, 40), CheckModeStatus, statusStyle);
        GUI.Label(new Rect(340, 430, 880, 40), StopMotionStatus, statusStyle);
    }

    void OnDestroy()
    {
        if (instance != this) return;
        ResumeCheckPause();
        if (open) SetOpen(false);
        if (keyboard != null) keyboard.onTextInput -= ReceiveText;
        SceneManager.sceneLoaded -= SceneLoaded;
        instance = null;
    }
}
