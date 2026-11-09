using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The end of the dodge tutorial (Tools > Gold Miner > Tutorial Squeeze): when startEvent fires
// (the EvilMeowmeow's story ending), two huge presses slide in from the sides and close in, bit by
// bit. Their points meet in the middle and they keep going, overlapping, so the space runs out.
// New hook holes open one by one at the Hook Spots and every hook gets faster.
// The only way to win is the exit: once the player is down to rescueAtLives (or the presses have
// nearly met) it fires squeeze_rescue, where a story starts (Tools > Gold Miner > Tutorial Rescue).
// When exitEvent fires (that story ending) the left press slides away and the Exit appears where
// it was; reaching it wins the level (LevelExit). The right press keeps coming. Running out of
// Lives, or being crushed by the presses, is a loss (R retries).
//   Left Press / Right Press   triangles, placed where they stop entering. Solid: they push the
//                              player along
//   Hook Spots                 one child per new hook hole, opened in the Hierarchy's order
// Everything is in the level scene, so it can be moved and resized in the Scene view.
public class SqueezePhase : MonoBehaviour
{
    [Header("Start")]
    [Tooltip("The TutorialEvents name that starts it (a step fires \"<step name>.end\" when it ends)")]
    [TutorialEvent] public string startEvent = "SqueezeStory.end";

    [Header("Presses")]
    public Transform leftPress;
    public Transform rightPress;
    [Tooltip("Seconds the presses take to slide in from off screen")]
    public float enterTime = 1.2f;
    [Tooltip("How far off screen they start, outwards from where they are placed")]
    public float enterDistance = 5f;
    [Tooltip("Seconds from entering until their points meet in the middle")]
    public float closeTime = 30f;
    [Tooltip("How the closing speeds up: 1 = steady, above 1 = slow at first and faster at the end")]
    [Range(1f, 3f)] public float closeEasing = 1.5f;
    [Tooltip("After the points meet the presses keep going, overlapping, at the speed they met at, until each point is this many times as far from home as the middle (2 = it reaches the other press's base)")]
    [Range(1f, 2.5f)] public float overlapTo = 2f;
    [Tooltip("Seconds the player can be pinched between the presses (or pushed inside one) before being crushed")]
    public float crushTime = 0.3f;

    [Header("Hooks")]
    [Tooltip("Where the new hook holes open; one per child, in order")]
    public Transform hookSpots;
    [Tooltip("Seconds into the squeeze the first new hole opens")]
    public float firstHookAt = 2f;
    [Tooltip("Seconds between new holes")]
    public float hookEvery = 4f;
    [Tooltip("Hook speed when the presses meet, times the level's speed (1 = no faster)")]
    public float endSpeed = 1.8f;
    [Tooltip("Shot rhythm when the presses meet: the aim time and the pause between shots, times the level's")]
    [Range(0.2f, 1f)] public float endRhythm = 0.6f;

    [Header("Rescue")]
    [Tooltip("Fire squeeze_rescue (a story starts on it) once the player is down to this many Lives. 0 = never")]
    public int rescueAtLives = 1;
    [Tooltip("...or once the presses have closed this far (0..1), so a player who never gets hit is rescued too. 1 = never")]
    [Range(0f, 1f)] public float rescueAtClosed = 0.85f;
    [Tooltip("The TutorialEvents name that opens the exit: the left press slides away and the Exit appears")]
    [TutorialEvent] public string exitEvent = "NiceStory.end";
    [Tooltip("The exit (Prefabs/Object/Exit). Reaching it wins the level")]
    public GameObject exitPrefab;
    [Tooltip("Where the exit appears. Empty = where the left press was")]
    public Transform exitSpot;
    public float exitRadius = 0.8f;

    // TutorialEvents it fires, for steps that react to it
    public const string StartedEvent = "squeeze_start";
    public const string HookAddedEvent = "squeeze_hook";
    public const string ClosedEvent = "squeeze_closed";   // the presses' points met (they keep going)
    public const string RescueEvent = "squeeze_rescue";   // down to rescueAtLives, or nearly closed
    public const string ExitOpenEvent = "exit_open";
    public const string CrushedEvent = "squeeze_crushed";

    bool running;
    float elapsed;   // since the squeeze began
    bool rescueFired, exitOpen, metFired;
    float pinched;   // seconds the player has been caught by the presses
    Collider2D leftCollider, rightCollider, playerCollider;
    float leftLeaving = -1f;   // seconds the left press has been sliding away; -1 = it isn't
    int spotsOpened;
    Vector2 leftHome, rightHome;
    float leftTravel, rightTravel;   // from home until the point reaches the middle
    Rigidbody2D leftBody, rightBody;
    LevelLoader loader;

    // Each hook's speed and rhythm before the squeeze, so the speed-up scales from them
    struct Pace { public float extend, retract, aimMin, aimMax, cooldown; }
    readonly Dictionary<SurvivalHookAI, Pace> baseline = new Dictionary<SurvivalHookAI, Pace>();

    public bool Running => running;
    // 0 when the presses are in place, 1 when their points meet (it stays 1 while they overlap)
    public float Progress => closeTime > 0f ? Mathf.Clamp01((elapsed - enterTime) / closeTime) : 1f;

    // How far the points have gone, as a share of the way to the middle: eased up to 1 when they
    // meet, then on at the speed they met at, up to overlapTo
    float Closed
    {
        get
        {
            if (closeTime <= 0f) return overlapTo;
            float t = (elapsed - enterTime) / closeTime;
            if (t <= 1f) return Mathf.Pow(Mathf.Max(0f, t), closeEasing);
            return Mathf.Min(overlapTo, 1f + closeEasing * (t - 1f));
        }
    }

    void Awake()
    {
        // The middle between the two presses, and how far each point is from it
        float middle = 0f;
        if (leftPress != null && rightPress != null) middle = (leftPress.position.x + rightPress.position.x) * 0.5f;
        leftTravel = Mathf.Max(0f, middle - Edge(leftPress, true));
        rightTravel = Mathf.Max(0f, Edge(rightPress, false) - middle);

        leftBody = SetUpPress(leftPress, out leftHome);
        rightBody = SetUpPress(rightPress, out rightHome);
        leftCollider = leftPress != null ? leftPress.GetComponent<Collider2D>() : null;
        rightCollider = rightPress != null ? rightPress.GetComponent<Collider2D>() : null;
        if (hookSpots != null)
            foreach (Transform spot in hookSpots) spot.gameObject.SetActive(false);   // markers only
    }

#if UNITY_EDITOR
    // An empty Exit Prefab picks up Prefabs/Object/Exit, so the exit is never invisible
    void OnValidate()
    {
        if (exitPrefab != null) return;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("Exit t:Prefab"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != "Exit") continue;
            exitPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            break;
        }
    }
#endif

    // The press's point: the inner edge of what it draws
    static float Edge(Transform press, bool left)
    {
        if (press == null) return 0f;
        Renderer r = press.GetComponent<Renderer>();
        if (r == null) return press.position.x;
        return left ? r.bounds.max.x : r.bounds.min.x;
    }

    void OnEnable() { TutorialEvents.Fired += OnEvent; }
    void OnDisable() { TutorialEvents.Fired -= OnEvent; }

    void OnEvent(string name)
    {
        if (name == startEvent) Begin();
        if (name == exitEvent || (exitEventMissing && IsStoryEnd(name))) OpenExit();
    }

    // Exit Event names a step that isn't in the scene (renamed, or the story made by hand), so it
    // would never fire: then the exit opens when any story ends during the squeeze instead
    bool exitEventMissing;

    void CheckExitEvent()
    {
        string step = StepOf(exitEvent);
        exitEventMissing = step != null && FindStep(step) == null;
        if (exitEventMissing)
            Debug.LogWarning("Squeeze: Exit Event is \"" + exitEvent + "\" but there is no step called \"" + step
                + "\". The exit opens when the next story ends instead; set Exit Event to \"<your story step>.end\".", this);
    }

    static string StepOf(string e)
    {
        if (string.IsNullOrEmpty(e) || !e.EndsWith(".end")) return null;
        return e.Substring(0, e.Length - ".end".Length);
    }

    static TutorialStep FindStep(string name)
    {
        foreach (TutorialStep s in FindObjectsByType<TutorialStep>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (s.name == name) return s;
        return null;
    }

    // "<step>.end" of a step with a conversation, other than the one that started the squeeze
    bool IsStoryEnd(string e)
    {
        if (!running || e == startEvent) return false;
        string step = StepOf(e);
        TutorialStep s = step != null ? FindStep(step) : null;
        return s != null && s.GetComponent<TutorialDialogue>() != null;
    }

    // The way out: the left press slides off, the presses stop closing, the exit appears
    public void OpenExit()
    {
        if (!running || exitOpen) return;
        exitOpen = true;
        leftLeaving = 0f;

        Vector3 at = exitSpot != null ? exitSpot.position : new Vector3(leftHome.x, leftHome.y, 0f);
        GameObject exit = exitPrefab != null ? Instantiate(exitPrefab, at, Quaternion.identity) : new GameObject("Exit");
        exit.name = "Exit";
        exit.transform.position = at;
        LevelExit reach = exit.GetComponent<LevelExit>();
        if (reach == null) reach = exit.AddComponent<LevelExit>();
        reach.radius = exitRadius;
        if (exitPrefab == null) Debug.LogWarning("SqueezePhase: no Exit Prefab assigned; the exit is invisible");
        TutorialEvents.Fire(ExitOpenEvent);
    }

    // Start now, whatever the event (for a UnityEvent or another script)
    public void Begin()
    {
        if (running) return;
        running = true;
        elapsed = 0f;
        loader = FindFirstObjectByType<LevelLoader>();
        CheckExitEvent();
        if (leftPress != null) leftPress.gameObject.SetActive(true);
        if (rightPress != null) rightPress.gameObject.SetActive(true);
        TutorialEvents.Fire(StartedEvent);
    }

    // Solid and hidden until the squeeze: a kinematic body moved by physics pushes the player
    Rigidbody2D SetUpPress(Transform press, out Vector2 home)
    {
        home = press != null ? (Vector2)press.position : Vector2.zero;
        if (press == null) return null;
        Rigidbody2D body = press.GetComponent<Rigidbody2D>();
        if (body == null) body = press.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (press.GetComponent<Collider2D>() == null) FitTriangleCollider(press);
        press.position = PressAt(home, 0f, 0f);
        press.gameObject.SetActive(false);
        return body;
    }

    // A collider on the triangle sprite's own outline (apex up, base down, in its local space)
    static void FitTriangleCollider(Transform press)
    {
        SpriteRenderer sr = press.GetComponent<SpriteRenderer>();
        PolygonCollider2D poly = press.gameObject.AddComponent<PolygonCollider2D>();
        if (sr == null || sr.sprite == null) return;
        Bounds b = sr.sprite.bounds;
        poly.points = new[]
        {
            new Vector2(b.center.x, b.max.y),
            new Vector2(b.min.x, b.min.y),
            new Vector2(b.max.x, b.min.y),
        };
    }

    // Where a press is: off screen at t = 0, home after enterTime, then closing in (`travel` is
    // how far its point is from the middle)
    Vector2 PressAt(Vector2 home, float travel, float t)
    {
        Vector2 inward = home.x < 0f ? Vector2.right : Vector2.left;
        if (t < enterTime)
        {
            float k = enterTime > 0f ? t / enterTime : 1f;
            k = 1f - (1f - k) * (1f - k);   // eases into place
            return home - inward * enterDistance * (1f - k);
        }
        return home + inward * travel * Closed;
    }

    // Time.deltaTime: a frozen game (tutorial steps, the pause popup) stops the squeeze too
    void Update()
    {
        if (!running) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        elapsed += Time.deltaTime;
        if (leftLeaving >= 0f) leftLeaving += Time.deltaTime;
        OpenHooks();
        SpeedUp();

        if (!rescueFired && elapsed >= enterTime
            && ((rescueAtLives > 0 && gm.Lives <= rescueAtLives) || (rescueAtClosed < 1f && Progress >= rescueAtClosed)))
        {
            rescueFired = true;
            TutorialEvents.Fire(RescueEvent);
        }

        // The points meet; they don't stop there
        if (!metFired && Progress >= 1f)
        {
            metFired = true;
            TutorialEvents.Fire(ClosedEvent);
        }

        if (Pinched()) pinched += Time.deltaTime;
        else pinched = 0f;
        if (pinched >= crushTime)
        {
            running = false;
            TutorialEvents.Fire(CrushedEvent);
            gm.Crush("Squeezed!");
        }
    }

    // Caught by the presses: pressed between the two of them, or pushed so far that its middle is
    // inside one (against the other press, or the arena's edge)
    bool Pinched()
    {
        if (playerCollider == null)
        {
            PlayerCharacter player = FindFirstObjectByType<PlayerCharacter>();
            playerCollider = player != null ? player.GetComponent<Collider2D>() : null;
            if (playerCollider == null) return false;
        }
        bool onLeft = Solid(leftCollider), onRight = Solid(rightCollider);
        if (onLeft && onRight && playerCollider.IsTouching(leftCollider) && playerCollider.IsTouching(rightCollider)) return true;
        Vector2 middle = playerCollider.bounds.center;
        return (onLeft && leftCollider.OverlapPoint(middle)) || (onRight && rightCollider.OverlapPoint(middle));
    }

    static bool Solid(Collider2D press) => press != null && press.enabled && press.gameObject.activeInHierarchy;

    void FixedUpdate()
    {
        if (!running) return;
        if (leftBody != null && leftBody.gameObject.activeSelf)
        {
            Vector2 at = PressAt(leftHome, leftTravel, elapsed);
            if (leftLeaving >= 0f)
            {
                // Back out the way it came, then gone
                float k = enterTime > 0f ? Mathf.Clamp01(leftLeaving / enterTime) : 1f;
                at += Vector2.left * enterDistance * k * k;
                if (k >= 1f) leftBody.gameObject.SetActive(false);
            }
            if (leftBody.gameObject.activeSelf) leftBody.MovePosition(at);
        }
        if (rightBody != null) rightBody.MovePosition(PressAt(rightHome, rightTravel, elapsed));
    }

    void OpenHooks()
    {
        if (hookSpots == null || loader == null || spotsOpened >= hookSpots.childCount) return;
        if (elapsed < firstHookAt + spotsOpened * hookEvery) return;
        Transform spot = hookSpots.GetChild(spotsOpened++);
        if (loader.AddHole(spot.position, 0.8f) != null) TutorialEvents.Fire(HookAddedEvent);
    }

    // From the level's speed when the squeeze starts to endSpeed times it when the presses meet
    void SpeedUp()
    {
        if (loader == null) return;
        float k = Progress;
        float speed = Mathf.Lerp(1f, endSpeed, k);
        float rhythm = Mathf.Lerp(1f, endRhythm, k);
        foreach (SurvivalHookAI ai in loader.Holes)
        {
            if (ai == null || ai.hook == null) continue;
            if (!baseline.TryGetValue(ai, out Pace b))
            {
                b = new Pace
                {
                    extend = ai.hook.extendSpeed, retract = ai.hook.retractSpeed,
                    aimMin = ai.telegraphMin, aimMax = ai.telegraphMax, cooldown = ai.cooldown,
                };
                baseline[ai] = b;
            }
            ai.hook.extendSpeed = b.extend * speed;
            ai.hook.retractSpeed = b.retract * speed;
            ai.telegraphMin = b.aimMin * rhythm;
            ai.telegraphMax = b.aimMax * rhythm;
            ai.cooldown = b.cooldown * rhythm;
        }
    }

    // The presses' path to the middle and the new hook spots, in the Scene view
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        float middle = leftPress != null && rightPress != null ? (leftPress.position.x + rightPress.position.x) * 0.5f : 0f;
        Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.8f);
        if (leftPress != null)
        {
            float edge = Edge(leftPress, true);
            Gizmos.DrawLine(new Vector3(edge, leftPress.position.y), new Vector3(middle, leftPress.position.y));
        }
        if (rightPress != null)
        {
            float edge = Edge(rightPress, false);
            Gizmos.DrawLine(new Vector3(edge, rightPress.position.y), new Vector3(middle, rightPress.position.y));
        }
        Gizmos.DrawWireSphere(new Vector3(middle, leftPress != null ? leftPress.position.y : 0f), 0.25f);

        if (hookSpots == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.4f);
        foreach (Transform spot in hookSpots)
        {
            Gizmos.DrawWireSphere(spot.position, 0.35f);
            Gizmos.DrawLine(spot.position, spot.position + Vector3.down * 0.8f);
        }
    }
}
