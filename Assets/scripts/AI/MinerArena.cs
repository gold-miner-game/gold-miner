using System.Collections.Generic;
using UnityEngine;

// Level state for the miner AI. Put it on any object in the scene (Set Up Scene puts it on the
// LevelManager object).
//
// Normal play: LevelManager runs the level as usual; this only reads its score and keeps a matching
//   timer, so MinerAgent can see them.
// Training: LevelManager is switched off (it freezes the game and reloads the scene at the end of a
//   level, which training can't use). This class runs the timer and score instead, and restarts the
//   level in place: a random level from LevelManager.levels, minerals moved a little from their scene
//   positions, player back at the start.
public class MinerArena : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public LevelManager levelManager;
    public AIMinerHook hook;
    public die playerTarget;

    [Header("Training")]
    [Tooltip("On each restart, move every mineral up to this far from where it was placed in the scene (0 = keep the scene layout).")]
    public float mineralJitter = 1.5f;
    [Tooltip("Minerals are kept inside this area when moved.")]
    public Vector2 fieldMin = new Vector2(-7f, -4.7f);
    public Vector2 fieldMax = new Vector2(7f, 2.5f);
    public bool showTrainingHud = true;

    // Training only: the level ended. True if the player was caught.
    public event System.Action<bool> LevelEnded;

    public bool TrainingMode { get; private set; }

    private float score;
    private float targetScore;
    private float timeLimit;
    private float timeLeft;
    private bool playing = true;

    // Minerals currently on the field, and inactive copies of the scene's minerals to respawn from
    private readonly List<GrabbableObject> minerals = new List<GrabbableObject>();
    private readonly List<GrabbableObject> prototypes = new List<GrabbableObject>();
    private readonly List<Vector3> startPositions = new List<Vector3>();
    private readonly List<Vector2> sizes = new List<Vector2>();
    private Rigidbody2D playerBody;
    private Vector3 playerStart;

    public float Score => TrainingMode ? score : levelManager.Score;
    public float TargetScore => TrainingMode ? targetScore : levelManager.TargetScore;
    public float TimeLimit => timeLimit;
    public float TimeLeft => timeLeft;
    public bool IsPlaying => playing;
    public IReadOnlyList<GrabbableObject> Minerals => minerals;

    void Awake()
    {
        if (levelManager == null) levelManager = FindFirstObjectByType<LevelManager>();
        if (hook == null) hook = FindFirstObjectByType<AIMinerHook>();
        if (playerTarget == null) playerTarget = FindFirstObjectByType<die>();

        playerBody = playerTarget.GetComponent<Rigidbody2D>();
        playerStart = playerTarget.transform.position;

        foreach (GrabbableObject mineral in FindObjectsByType<GrabbableObject>(FindObjectsSortMode.None))
        {
            minerals.Add(mineral);
            startPositions.Add(mineral.transform.position);
            sizes.Add(mineral.GetComponent<Collider2D>().bounds.size);
        }

        // LevelManager doesn't expose its time limit; find the level with the current target
        targetScore = levelManager.TargetScore;
        timeLimit = levelManager.levels[0].timeLimit;
        foreach (LevelManager.Level level in levelManager.levels)
        {
            if (Mathf.Approximately(level.targetScore, targetScore))
            {
                timeLimit = level.timeLimit;
                break;
            }
        }
        timeLeft = timeLimit;

        hook.ShotReturned += OnShotReturned;
        hook.PlayerCaught += () => EndLevel(true);
    }

    // Called by MinerAgent when it connects to the trainer
    public void StartTraining()
    {
        if (TrainingMode)
        {
            return;
        }
        TrainingMode = true;
        levelManager.enabled = false; // No freezing, no scene reloads, no HUD
        hook.trainingMode = true;

        // Keep a hidden copy of every mineral, to respawn fresh ones on each restart
        foreach (GrabbableObject mineral in minerals)
        {
            GameObject copy = Instantiate(mineral.gameObject);
            copy.SetActive(false);
            copy.name = mineral.name + " (prototype)";
            prototypes.Add(copy.GetComponent<GrabbableObject>());
        }
    }

    // Training: restart the level in place
    public void ResetLevel()
    {
        LevelManager.Level level = levelManager.levels[Random.Range(0, levelManager.levels.Length)];
        targetScore = level.targetScore;
        timeLimit = level.timeLimit;
        timeLeft = timeLimit;
        score = 0f;
        playing = true;

        // Player back to the start
        playerTarget.transform.position = playerStart;
        playerBody.position = playerStart;
        playerBody.linearVelocity = Vector2.zero;

        // Replace whatever minerals are left with fresh copies
        foreach (GrabbableObject old in minerals)
        {
            if (old != null)
            {
                old.gameObject.SetActive(false); // Gone right away; Destroy only happens at the end of the frame
                Destroy(old.gameObject);
            }
        }
        minerals.Clear();

        var placed = new List<Rect>();
        for (int i = 0; i < prototypes.Count; i++)
        {
            Vector2 position = PickPosition(startPositions[i], sizes[i], placed);
            placed.Add(new Rect(position - sizes[i] / 2f, sizes[i]));

            GameObject copy = Instantiate(prototypes[i].gameObject, new Vector3(position.x, position.y, startPositions[i].z), prototypes[i].transform.rotation);
            copy.name = prototypes[i].name.Replace(" (prototype)", "");
            copy.SetActive(true);
            minerals.Add(copy.GetComponent<GrabbableObject>());
        }
    }

    void FixedUpdate()
    {
        if (!playing)
        {
            return;
        }

        timeLeft -= Time.fixedDeltaTime;
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            EndLevel(false);
        }
        else if (!TrainingMode && playerTarget.IsDead)
        {
            playing = false; // Normal game: LevelManager shows GAME OVER
        }
    }

    void OnShotReturned(GrabbableObject returned)
    {
        if (TrainingMode && playing && returned != null)
        {
            score += returned.TotalValue;
        }
    }

    void EndLevel(bool playerCaught)
    {
        if (!playing)
        {
            return;
        }
        playing = false;
        LevelEnded?.Invoke(playerCaught);
    }

    // A spot near the original one that doesn't overlap minerals already placed or the player start
    Vector2 PickPosition(Vector2 start, Vector2 size, List<Rect> placed)
    {
        for (int attempt = 0; attempt < 20 && mineralJitter > 0f; attempt++)
        {
            Vector2 candidate = start + Random.insideUnitCircle * mineralJitter;
            candidate.x = Mathf.Clamp(candidate.x, fieldMin.x + size.x / 2f, fieldMax.x - size.x / 2f);
            candidate.y = Mathf.Clamp(candidate.y, fieldMin.y + size.y / 2f, fieldMax.y - size.y / 2f);

            Rect rect = new Rect(candidate - size / 2f - Vector2.one * 0.05f, size + Vector2.one * 0.1f);
            bool free = Vector2.Distance(candidate, playerStart) > 1f;
            foreach (Rect other in placed)
            {
                if (rect.Overlaps(other))
                {
                    free = false;
                    break;
                }
            }
            if (free)
            {
                return candidate;
            }
        }
        return start;
    }

    void OnGUI()
    {
        if (!TrainingMode || !showTrainingHud)
        {
            return;
        }
        GUIStyle hud = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
        hud.normal.textColor = Color.white;
        GUI.Label(new Rect(20, 20, 500, 30), "TRAINING", hud);
        GUI.Label(new Rect(20, 50, 500, 30), $"Score {score:0} / {targetScore:0}", hud);
        GUI.Label(new Rect(20, 80, 500, 30), $"Time {Mathf.CeilToInt(timeLeft)}", hud);
    }
}
