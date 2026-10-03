using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

// The miner AI (ML-Agents). Put it on the Miner next to AIMinerHook.
// It decides WHEN the hook fires; the aim swings, so the timing picks the direction.
// Goal: bring back as much value as possible, and catch the player before time runs out.
//
// - Training (mlagents-learn running): MinerArena restarts levels in place, PlayerBot plays the player.
// - With a trained model: the AI is the miner, a human plays the player.
// - No model: Heuristic fires at random moments (like the original game). Hold F to fire.
//
// Command-line arguments for the game build (see carc/evaluate.slurm and TRAINING.md):
//   -minerTrainingTest          run the training loop without the trainer, firing at random
//   -minerTimeScale <x>         game speed for -minerTrainingTest (default 20)
//   -minerEvalLog <folder>      write a CSV row per level and per shot (MinerEvalLog) for testing
//   -minerEvalLabel <name>      name used in those files (default: trained, or random without a trainer)
//   -minerEvalEpisodes <n>      quit after n levels
//
// Runs after the other scripts' Awake: LevelManager.Awake sets Time.timeScale to 1, and that must
// happen before ML-Agents connects and speeds the game up for training.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(AIMinerHook), typeof(BehaviorParameters), typeof(BufferSensorComponent))]
public class MinerAgent : Agent
{
    public const string BehaviorName = "Miner";
    public const int VectorObservationSize = 22;
    public const int MineralFeatures = 7;
    public const int MaxMinerals = 20;

    [Header("References (found automatically if empty)")]
    public MinerArena arena;
    public die playerTarget;
    public PlayerBot playerBot;

    [Header("Rewards")]
    [Tooltip("Reward per point of value the hook brings back (gold 100 -> +1).")]
    public float rewardPerValue = 0.01f;
    [Tooltip("Penalty when the hook comes back empty.")]
    public float emptyShotPenalty = 0.05f;
    [Tooltip("Reward for catching the player (this ends the level), scaled by how much of the level's target money " +
             "has been collected: catching before collecting anything is worth nothing, catching after reaching the " +
             "target is worth the full amount. So the AI learns to collect money first and catch the player later.")]
    public float catchPlayerReward = 3f;
    [Tooltip("Penalty when time runs out and the player was never caught.")]
    public float noCatchPenalty = 1f;

    [Header("Decisions")]
    [Tooltip("While the hook can fire, ask the policy every N physics steps.")]
    [Min(1)] public int decisionInterval = 2;
    [Tooltip("No model (heuristic): about this many shots per second of waiting, at random moments.")]
    public float heuristicFireRate = 1f;
    [Tooltip("Let PlayerBot move the player outside training too, to watch the AI play against the bot.")]
    public bool botOutsideTraining;

    const float ValueNorm = 200f;
    const float WeightNorm = 30f;
    const float SpeedNorm = 5f;

    private AIMinerHook hook;
    private bool training;
    private bool selfTest;
    private int shots;
    private int emptyShots;
    private float catchTimeFraction;
    private int stepsSinceDecision;
    private bool levelOver;
    private bool playerCaught;
    private float hookRadius;
    private Rigidbody2D playerBody;
    private BufferSensorComponent mineralSensor;
    private ContactFilter2D solidFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];

    // Test log (only with -minerEvalLog)
    private MinerEvalLog evalLog;
    private int evalEpisodeLimit;
    private int evalEpisodes;
    private bool shotPending;
    private MinerEvalLog.Shot pendingShot;
    private float readySince = -1f;

    private float LevelTime => arena.TimeLimit - arena.TimeLeft;

    public override void Initialize()
    {
        hook = GetComponent<AIMinerHook>();
        if (arena == null) arena = FindFirstObjectByType<MinerArena>();
        if (playerTarget == null) playerTarget = FindFirstObjectByType<die>();
        if (playerBot == null) playerBot = playerTarget.GetComponent<PlayerBot>();

        // -minerTrainingTest: run the training loop (bot, level restarts, rewards) without the
        // trainer, with heuristic actions. Used to check a build before uploading it.
        selfTest = HasArgument("-minerTrainingTest");
        training = Academy.Instance.IsCommunicatorOn || selfTest;
        if (selfTest)
        {
            Time.timeScale = float.TryParse(GetArgument("-minerTimeScale"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float scale) ? scale : 20f;
        }

        string evalFolder = GetArgument("-minerEvalLog");
        if (evalFolder != null)
        {
            BehaviorParameters behavior = GetComponent<BehaviorParameters>();
            bool usesPolicy = Academy.Instance.IsCommunicatorOn || behavior.Model != null;
            string label = GetArgument("-minerEvalLabel") ?? (usesPolicy ? "trained" : "random");
            evalLog = new MinerEvalLog(evalFolder, label);
            int.TryParse(GetArgument("-minerEvalEpisodes"), out evalEpisodeLimit);
            Debug.Log($"[MinerAgent] Writing test log '{label}' to {evalFolder}");
        }
        if (training)
        {
            arena.StartTraining();
        }
        if (playerBot != null)
        {
            playerBot.controlPlayer = training || botOutsideTraining;
        }

        hook.ShotReturned += OnShotReturned;
        arena.LevelEnded += caught =>
        {
            levelOver = true;
            playerCaught = caught;
            catchTimeFraction = 1f - arena.TimeLeft / arena.TimeLimit;
            EndShotLog(caught ? "Player" : "Unfinished", 0f, 0f);
        };

        playerBody = playerTarget.GetComponent<Rigidbody2D>();
        mineralSensor = GetComponent<BufferSensorComponent>();
        Collider2D headCollider = hook.hookHead.GetComponent<Collider2D>();
        hookRadius = headCollider != null ? Mathf.Max(headCollider.bounds.extents.x, 0.05f) : 0.1f;
        solidFilter = new ContactFilter2D { useTriggers = false };
    }

    public override void OnEpisodeBegin()
    {
        levelOver = false;
        playerCaught = false;
        stepsSinceDecision = 0;
        shots = 0;
        emptyShots = 0;
        shotPending = false;
        readySince = -1f;

        if (training)
        {
            arena.ResetLevel();
            hook.ResetHook();
            if (playerBot != null) playerBot.ResetBot();
        }
    }

    void OnShotReturned(GrabbableObject returned)
    {
        if (returned != null)
        {
            AddReward(returned.TotalValue * rewardPerValue);
            string kind = returned.resourceType != null ? returned.resourceType.name : returned.name;
            EndShotLog(kind, returned.TotalValue, returned.TotalWeight);
        }
        else
        {
            emptyShots++;
            AddReward(-emptyShotPenalty);
            EndShotLog("Empty", 0f, 0f);
        }
    }

    // Test log: remember what the AI saw when it fired...
    void BeginShotLog(float aimAngle, int predicted, float predictedValue, float predictedDistance)
    {
        if (evalLog == null)
        {
            return;
        }
        shotPending = true;
        pendingShot = new MinerEvalLog.Shot
        {
            episode = evalEpisodes + 1,
            index = shots,
            fireTime = LevelTime,
            fireFraction = LevelTime / arena.TimeLimit,
            aimAngle = aimAngle,
            waited = readySince >= 0f ? LevelTime - readySince : 0f,
            predicted = predicted == 1 ? "Mineral" : predicted == 2 ? "Player" : "Nothing",
            predictedValue = predictedValue,
            predictedDistance = predictedDistance,
        };
    }

    // ...and what came of it
    void EndShotLog(string outcome, float value, float weight)
    {
        if (evalLog == null || !shotPending)
        {
            return;
        }
        shotPending = false;
        pendingShot.outcome = outcome;
        pendingShot.value = value;
        pendingShot.weight = weight;
        pendingShot.retrieveTime = LevelTime - pendingShot.fireTime;
        pendingShot.moneyAfter = arena.Score;
        evalLog.WriteShot(pendingShot);
    }

    void FixedUpdate()
    {
        if (levelOver)
        {
            if (!training)
            {
                return; // Normal game: LevelManager shows the result and reloads the scene
            }
            // Catching is worth more the more money was collected first (full value once the target is reached)
            float moneyShare = Mathf.Clamp01(arena.Score / Mathf.Max(arena.TargetScore, 1f));
            AddReward(playerCaught ? catchPlayerReward * moneyShare : -noCatchPenalty);
            levelOver = false;
            RecordEpisodeStats();
            EndEpisode(); // Calls OnEpisodeBegin, which restarts the level

            if (evalLog != null && evalEpisodeLimit > 0 && evalEpisodes >= evalEpisodeLimit)
            {
                evalLog.Close();
                evalLog = null;
                Debug.Log($"[MinerAgent] Test finished after {evalEpisodes} levels");
                Application.Quit();
            }
            return;
        }

        // How long the hook has been ready to fire (test log: does the AI wait for a good moment?)
        if (hook.CanFire)
        {
            if (readySince < 0f) readySince = LevelTime;
        }
        else
        {
            readySince = -1f;
        }

        // Only ask the policy when there is a real choice to make
        if (arena.IsPlaying && hook.CanFire && ++stepsSinceDecision >= decisionInterval)
        {
            stepsSinceDecision = 0;
            RequestDecision();
        }
    }

    // Game-specific numbers for TensorBoard and the progress report (carc/report.py).
    // ML-Agents averages them over every summary period (summary_freq in config/miner.yaml).
    void RecordEpisodeStats()
    {
        float moneyLeft = 0f;
        foreach (GrabbableObject mineral in arena.Minerals)
        {
            if (mineral != null && mineral.gameObject.activeInHierarchy && !mineral.IsCaught)
            {
                moneyLeft += mineral.TotalValue;
            }
        }
        float money = arena.Score;
        float emptyRate = shots > 0 ? (float)emptyShots / shots : 0f;

        StatsRecorder stats = Academy.Instance.StatsRecorder;
        stats.Add("Miner/Money Collected", money);
        stats.Add("Miner/Share Of Money Collected", money + moneyLeft > 0f ? money / (money + moneyLeft) : 0f);
        stats.Add("Miner/Target Reached Rate", money >= arena.TargetScore ? 1f : 0f);
        stats.Add("Miner/Player Caught Rate", playerCaught ? 1f : 0f);
        if (playerCaught)
        {
            stats.Add("Miner/Catch Time (fraction of level)", catchTimeFraction);
        }
        stats.Add("Miner/Shots Per Level", shots);
        stats.Add("Miner/Empty Shot Rate", emptyRate);

        if (evalLog != null)
        {
            evalEpisodes++;
            evalLog.WriteEpisode(evalEpisodes, arena.TargetScore, arena.TimeLimit, money, playerCaught,
                catchTimeFraction * arena.TimeLimit, shots, emptyShots, moneyLeft, GetCumulativeReward());
        }

        // Shows up in the game's log (results/<run-id>/run_logs/Player-*.log when training)
        Debug.Log($"[MinerAgent] Episode {CompletedEpisodes + 1} ended: score {money:0}/{arena.TargetScore:0}, " +
                  $"player caught {playerCaught}, catch time {(playerCaught ? catchTimeFraction : -1f):0.00}, " +
                  $"shots {shots}, empty {emptyShots}, money left {moneyLeft:0}, reward {GetCumulativeReward():0.00}, steps {StepCount}");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector2 origin = hook.transform.position;
        float range = hook.maxDistance;

        // Hook (6)
        sensor.AddObservation(AngleToObservation(hook.AimAngle));
        sensor.AddObservation(hook.SwingDirection);
        sensor.AddObservation(hook.CanFire ? 1f : 0f);
        sensor.AddOneHotObservation((int)hook.State, 3);

        // Level (3)
        sensor.AddObservation(arena.TimeLeft / arena.TimeLimit);
        sensor.AddObservation(Mathf.Clamp(arena.Score / arena.TargetScore, 0f, 2f));
        sensor.AddObservation(Mathf.Clamp(arena.TimeLeft / 60f, 0f, 2f));

        // Player (7)
        Vector2 toPlayer = (Vector2)playerTarget.transform.position - origin;
        sensor.AddObservation(toPlayer / range);
        sensor.AddObservation(playerBody.linearVelocity / SpeedNorm);
        sensor.AddObservation(Mathf.DeltaAngle(hook.AimAngle, DirectionToAngle(toPlayer)) / 180f);
        sensor.AddObservation(toPlayer.magnitude <= range ? 1f : 0f);
        sensor.AddObservation(playerTarget.IsDead ? 1f : 0f);

        // What the hook would hit first if it fired right now (6)
        AddAimObservations(sensor, origin, range);

        // Every mineral still on the field (variable count, handled by the buffer sensor)
        int count = 0;
        foreach (GrabbableObject mineral in arena.Minerals)
        {
            if (count >= MaxMinerals) break;
            if (mineral == null || !mineral.gameObject.activeInHierarchy || mineral.IsCaught) continue;

            Vector2 toMineral = (Vector2)mineral.transform.position - origin;
            float angle = DirectionToAngle(toMineral);
            Vector3 scale = mineral.transform.lossyScale;
            mineralSensor.AppendObservation(new[]
            {
                Mathf.DeltaAngle(hook.AimAngle, angle) / 180f, // how far the swing is from it
                AngleToObservation(angle),
                toMineral.magnitude / range,
                mineral.TotalValue / ValueNorm,
                mineral.TotalWeight / WeightNorm,
                Mathf.Abs(scale.x * scale.y),
                toMineral.magnitude <= range ? 1f : 0f,
            });
            count++;
        }
    }

    void AddAimObservations(VectorSensor sensor, Vector2 origin, float range)
    {
        int hitType = ProbeAim(out float value, out float weight, out float distance);
        sensor.AddOneHotObservation(hitType, 3);
        sensor.AddObservation(value / ValueNorm);
        sensor.AddObservation(weight / WeightNorm);
        sensor.AddObservation(distance / range);
    }

    // What the hook would hit first if it fired now: 0 nothing, 1 mineral, 2 player.
    // value/weight of that mineral, distance in world units (maxDistance if nothing).
    public int ProbeAim(out float value, out float weight, out float distance)
    {
        Vector2 origin = hook.transform.position;
        float range = hook.maxDistance;
        int hitType = 0;
        value = 0f;
        weight = 0f;
        distance = range;

        // Nearest mineral or player along the aim; the hook passes through anything else
        float nearest = float.PositiveInfinity;
        int hits = Physics2D.CircleCast(origin, hookRadius, hook.AimDirection, solidFilter, castHits, range);
        for (int i = 0; i < hits; i++)
        {
            if (castHits[i].distance >= nearest)
            {
                continue;
            }

            Collider2D hitCollider = castHits[i].collider;
            if (hitCollider.TryGetComponent(out GrabbableObject mineral) && !mineral.IsCaught)
            {
                hitType = 1;
                value = mineral.TotalValue;
                weight = mineral.TotalWeight;
            }
            else if (hitCollider.TryGetComponent(out die _))
            {
                hitType = 2;
                value = 0f;
                weight = 0f;
            }
            else
            {
                continue;
            }
            nearest = castHits[i].distance;
            distance = nearest;
        }
        return hitType;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (actions.DiscreteActions[0] != 1)
        {
            return;
        }
        float aimAngle = hook.AimAngle;
        int predicted = 0;
        float predictedValue = 0f, predictedDistance = 0f;
        if (evalLog != null)
        {
            predicted = ProbeAim(out predictedValue, out _, out predictedDistance);
        }
        if (hook.Fire())
        {
            shots++;
            BeginShotLog(aimAngle, predicted, predictedValue, predictedDistance);
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!hook.CanFire)
        {
            actionMask.SetActionEnabled(0, 1, false);
        }
    }

    // Used when there is no model: F fires, otherwise fire at random moments
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> actions = actionsOut.DiscreteActions;
        Keyboard keyboard = Keyboard.current;
        bool fire = keyboard != null && keyboard.fKey.isPressed;
        if (!fire)
        {
            float chancePerDecision = heuristicFireRate * Time.fixedDeltaTime * decisionInterval;
            fire = Random.value < chancePerDecision;
        }
        actions[0] = fire ? 1 : 0;
    }

    void OnApplicationQuit()
    {
        evalLog?.Close();
    }

    static bool HasArgument(string name)
    {
        return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;
    }

    // The value after a command-line argument, or null
    static string GetArgument(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Aim angles are 180 (left) .. 270 (down) .. 360 (right); map that range to -1 .. 1
    static float AngleToObservation(float degrees)
    {
        return Mathf.DeltaAngle(270f, degrees) / 90f;
    }

    static float DirectionToAngle(Vector2 direction)
    {
        float degrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    void OnValidate()
    {
        ConfigureComponents();
    }

    // Keep the ML-Agents components matching this script, so nothing has to be typed in by hand
    public void ConfigureComponents()
    {
        if (TryGetComponent(out BehaviorParameters behavior))
        {
            behavior.BehaviorName = BehaviorName;
            behavior.BrainParameters.VectorObservationSize = VectorObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(2); // 0 = wait, 1 = fire
        }
        if (TryGetComponent(out BufferSensorComponent buffer))
        {
            buffer.SensorName = "Minerals";
            buffer.ObservableSize = MineralFeatures;
            buffer.MaxNumObservables = MaxMinerals;
        }
    }
}
