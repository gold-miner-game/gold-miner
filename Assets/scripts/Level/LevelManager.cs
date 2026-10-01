using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Put this on an empty object in the scene. Tracks the miner's score and the level timer.
// The player wins a level if, when time runs out, the miner has reached the target score
// and the player was never caught. Reaching the target early does not stop the clock.
public class LevelManager : MonoBehaviour
{
    [System.Serializable]
    public struct Level
    {
        public float targetScore;
        public float timeLimit; // Seconds
    }

    [Header("Levels")]
    public Level[] levels =
    {
        new Level { targetScore = 200f, timeLimit = 60f },
        new Level { targetScore = 250f, timeLimit = 50f },
        new Level { targetScore = 280f, timeLimit = 45f },
    };

    public static LevelManager Instance { get; private set; }

    private enum State { Playing, Won, Lost }
    private State state = State.Playing;

    // Survives scene reloads so "next level" can reload the same scene with new settings
    private static int levelIndex;

    private float score;
    private float timeLeft;
    private string loseReason;

    public float Score => score;
    public float TargetScore => CurrentLevel.targetScore;
    private Level CurrentLevel => levels[Mathf.Clamp(levelIndex, 0, levels.Length - 1)];
    private bool IsLastLevel => levelIndex >= levels.Length - 1;

    // Reset statics when entering Play Mode (in case domain reload is turned off)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        levelIndex = 0;
        Instance = null;
    }

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f; // A previous game over or level end froze the game
        timeLeft = CurrentLevel.timeLimit;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (state == State.Playing)
        {
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f)
            {
                timeLeft = 0f;
                if (score >= TargetScore)
                {
                    EndLevel(State.Won);
                }
                else
                {
                    loseReason = "TIME UP";
                    EndLevel(State.Lost);
                }
            }
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (state == State.Won && keyboard.enterKey.wasPressedThisFrame)
        {
            // After the last level, start over from the first one
            levelIndex = IsLastLevel ? 0 : levelIndex + 1;
            ReloadScene();
        }
        else if (state == State.Lost && keyboard.rKey.wasPressedThisFrame)
        {
            ReloadScene();
        }
    }

    // Called when the miner's hook brings an object back
    public void AddScore(float value)
    {
        if (state != State.Playing)
        {
            return;
        }
        score += value;
    }

    // Called by die when the hook catches the player
    public void PlayerCaught()
    {
        if (state != State.Playing)
        {
            return;
        }
        loseReason = "GAME OVER";
        EndLevel(State.Lost);
    }

    void EndLevel(State result)
    {
        state = result;
        Time.timeScale = 0f;
    }

    void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        GUIStyle hud = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
        hud.normal.textColor = Color.white;

        string check = score >= TargetScore ? "  OK" : "";
        GUI.Label(new Rect(20, 20, 400, 30), $"Level {levelIndex + 1}", hud);
        GUI.Label(new Rect(20, 50, 400, 30), $"Score {score:0} / {TargetScore:0}{check}", hud);
        GUI.Label(new Rect(20, 80, 400, 30), $"Time {Mathf.CeilToInt(timeLeft)}", hud);

        if (state == State.Playing)
        {
            return;
        }

        string message;
        if (state == State.Won)
        {
            message = IsLastLevel
                ? $"ALL LEVELS CLEAR\nScore {score:0}\nPress Enter to play again"
                : $"LEVEL COMPLETE\nScore {score:0}\nPress Enter for next level";
        }
        else
        {
            message = $"{loseReason}\nScore {score:0} / {TargetScore:0}\nPress R to retry";
        }

        GUIStyle banner = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        banner.normal.textColor = state == State.Won ? Color.yellow : Color.red;
        GUI.Label(new Rect(0, 0, Screen.width, Screen.height), message, banner);
    }
}
