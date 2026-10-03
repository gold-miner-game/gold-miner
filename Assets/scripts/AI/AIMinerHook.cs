using UnityEngine;

// AI version of MinerHook. Use it on the Miner in place of MinerHook (Tools > Miner AI > Set Up Scene
// does this and copies the settings over). It keeps all of MinerHook's settings but replaces how it
// shoots: the aim swings back and forth like classic Gold Miner, and the hook only fires when
// Fire() is called (by MinerAgent). So *when* it fires decides *where* it goes.
//
// Unity only calls the most-derived Start/Update/LateUpdate, so MinerHook's own versions (random
// shooting every second) never run on this component.
public class AIMinerHook : MinerHook
{
    public enum HookState { Waiting, Shooting, Retracting }

    [Header("Aiming")]
    [Tooltip("Angles in degrees: 180 = left, 270 = straight down, 360 = right.")]
    public float minAngle = 190f;
    public float maxAngle = 350f;
    [Tooltip("Degrees per second.")]
    public float swingSpeed = 80f;
    [Tooltip("How far the hook head hangs out from the miner while aiming.")]
    public float restLength = 0.5f;

    // Set by MinerArena while training: collected minerals are just removed (MinerArena keeps the
    // score), and catching the player is reported instead of calling die.Die (which freezes the game).
    [HideInInspector] public bool trainingMode;

    // The hook got back to the miner. The object it brought back, or null if it came back empty.
    public event System.Action<GrabbableObject> ShotReturned;
    // Training only: the hook touched the player.
    public event System.Action PlayerCaught;

    private HookState state = HookState.Waiting;
    private Vector2 shotDirection;
    private float cooldown;
    private float aimAngle = 270f;
    private int swingDirection = 1;
    private GrabbableObject carried;

    public HookState State => state;
    public bool CanFire => state == HookState.Waiting && cooldown <= 0f;
    public float AimAngle => aimAngle;
    public int SwingDirection => swingDirection;
    public Vector2 AimDirection => AngleToDirection(aimAngle);
    public Vector2 ShotDirection => shotDirection;

    void Start()
    {
        linkLine.positionCount = 2;
        cooldown = timeBetweenShots;
        hookHead.position = RestPosition();

        // Our own catcher on the hook head (MinerHook's HookCatcher talks to MinerHook's private state)
        AIHookCatcher catcher = hookHead.GetComponent<AIHookCatcher>();
        if (catcher == null)
        {
            catcher = hookHead.gameObject.AddComponent<AIHookCatcher>();
        }
        catcher.owner = this;
    }

    // Replaces MinerHook.Update. Everything happens in FixedUpdate instead: training runs the game
    // up to 20x faster, and moving per physics step keeps the hook from skipping over things.
    void Update()
    {
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        switch (state)
        {
            case HookState.Waiting:
                aimAngle += swingDirection * swingSpeed * dt;
                if (aimAngle > maxAngle || aimAngle < minAngle)
                {
                    aimAngle = Mathf.Clamp(aimAngle, minAngle, maxAngle);
                    swingDirection = -swingDirection;
                }
                hookHead.position = RestPosition();
                cooldown -= dt;
                break;

            case HookState.Shooting:
                hookHead.position += (Vector3)(shotDirection * shootSpeed * dt);
                if (Vector3.Distance(transform.position, hookHead.position) >= maxDistance)
                {
                    state = HookState.Retracting;
                }
                break;

            case HookState.Retracting:
                hookHead.position = Vector3.MoveTowards(hookHead.position, transform.position, RetractSpeed() * dt);
                if (Vector3.Distance(transform.position, hookHead.position) < 0.1f)
                {
                    GrabbableObject returned = carried;
                    carried = null;
                    state = HookState.Waiting;
                    cooldown = timeBetweenShots;
                    hookHead.position = RestPosition();

                    ShotReturned?.Invoke(returned);
                    if (returned != null)
                    {
                        if (trainingMode)
                        {
                            returned.gameObject.SetActive(false); // Gone right away, even mid-frame
                            Destroy(returned.gameObject);
                        }
                        else
                        {
                            returned.Collect(); // Normal game: LevelManager adds the score
                        }
                    }
                }
                break;
        }
    }

    void LateUpdate()
    {
        linkLine.SetPosition(0, transform.position);
        linkLine.SetPosition(1, hookHead.position);
    }

    // Shoot in the current aim direction. Returns false if busy or still in the cooldown.
    public bool Fire()
    {
        if (!CanFire)
        {
            return false;
        }
        shotDirection = AimDirection;
        state = HookState.Shooting;
        return true;
    }

    // Training: back to waiting with a random aim
    public void ResetHook()
    {
        carried = null;
        state = HookState.Waiting;
        cooldown = timeBetweenShots;
        aimAngle = Random.Range(minAngle, maxAngle);
        swingDirection = Random.value < 0.5f ? -1 : 1;
        hookHead.position = RestPosition();
    }

    // Called by AIHookCatcher. Same rules as MinerHook: only while shooting, one object per shot.
    public void TouchedMineral(GrabbableObject mineral)
    {
        if (state != HookState.Shooting || mineral.IsCaught)
        {
            return;
        }
        mineral.Catch(hookHead);
        carried = mineral;
        state = HookState.Retracting;
    }

    public void TouchedPlayer(die victim)
    {
        if (state != HookState.Shooting || victim.IsDead)
        {
            return;
        }

        if (trainingMode)
        {
            PlayerCaught?.Invoke();
        }
        else
        {
            victim.Die(hookHead); // Normal game: GAME OVER through LevelManager
        }
    }

    // Empty hook returns at retractSpeed; heavier objects come back slower
    float RetractSpeed()
    {
        if (carried == null)
        {
            return retractSpeed;
        }
        return Mathf.Min(pullForce / Mathf.Max(carried.TotalWeight, 0.0001f), retractSpeed);
    }

    Vector3 RestPosition()
    {
        return transform.position + (Vector3)(AimDirection * restLength);
    }

    public static Vector2 AngleToDirection(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
}
