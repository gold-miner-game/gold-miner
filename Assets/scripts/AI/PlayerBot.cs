using UnityEngine;

// Put this on the Player. A simple scripted opponent for training the miner AI, so no human needs
// to play: it wanders around the field and sometimes dodges the hook.
// While it is in control it switches off the player script (keyboard movement) and moves the
// Rigidbody2D itself, using the player's moveSpeed.
[RequireComponent(typeof(player), typeof(Rigidbody2D))]
public class PlayerBot : MonoBehaviour
{
    [Tooltip("When on, the bot moves the player instead of the keyboard. MinerAgent turns this on while training.")]
    public bool controlPlayer;

    [Header("Wandering")]
    public Vector2 areaMin = new Vector2(-7f, -4.7f);
    public Vector2 areaMax = new Vector2(7f, 2.5f);
    [Tooltip("Chance to stand still instead of picking a new direction.")]
    [Range(0f, 1f)] public float idleChance = 0.2f;
    public Vector2 changeDirectionEvery = new Vector2(0.4f, 2f);

    [Header("Dodging")]
    [Tooltip("Each episode the bot gets a random skill up to this value: the chance it reacts to a shot.")]
    [Range(0f, 1f)] public float maxDodgeSkill = 0.8f;
    [Tooltip("How close the hook's path must come before the bot reacts.")]
    public float dodgeRadius = 1.2f;

    private player movement;
    private die victim;
    private Rigidbody2D body;
    private AIMinerHook hook;
    private Vector2 wanderDirection;
    private float changeTimer;
    private float dodgeSkill;
    private bool dodgingThisShot;
    private bool wasShooting;

    void Awake()
    {
        movement = GetComponent<player>();
        victim = GetComponent<die>();
        body = GetComponent<Rigidbody2D>();
        hook = FindFirstObjectByType<AIMinerHook>();
        ResetBot();
    }

    // Called by MinerAgent at the start of every episode
    public void ResetBot()
    {
        dodgeSkill = Random.Range(0f, maxDodgeSkill);
        changeTimer = 0f;
        wasShooting = false;
    }

    void FixedUpdate()
    {
        if (!controlPlayer || (victim != null && victim.IsDead))
        {
            return;
        }
        movement.enabled = false; // Keyboard off while the bot drives

        changeTimer -= Time.fixedDeltaTime;
        if (changeTimer <= 0f)
        {
            wanderDirection = Random.value < idleChance ? Vector2.zero : Random.insideUnitCircle.normalized;
            changeTimer = Random.Range(changeDirectionEvery.x, changeDirectionEvery.y);
        }

        Vector2 position = body.position;
        Vector2 direction = wanderDirection;

        // Head back into the field if we wandered out
        Vector2 clamped = new Vector2(Mathf.Clamp(position.x, areaMin.x, areaMax.x), Mathf.Clamp(position.y, areaMin.y, areaMax.y));
        if (clamped != position)
        {
            direction = (clamped - position).normalized;
            wanderDirection = direction;
        }

        // Decide once per shot whether the bot notices it
        bool shooting = hook != null && hook.State == AIMinerHook.HookState.Shooting;
        if (shooting && !wasShooting)
        {
            dodgingThisShot = Random.value < dodgeSkill;
        }
        wasShooting = shooting;

        if (shooting && dodgingThisShot)
        {
            Vector2 dodge = DodgeDirection(position);
            if (dodge != Vector2.zero)
            {
                direction = dodge;
            }
        }

        body.linearVelocity = direction * movement.moveSpeed;
        Face(direction.x);
    }

    // Step sideways out of the hook's path if it is coming at us
    Vector2 DodgeDirection(Vector2 position)
    {
        Vector2 origin = hook.transform.position;
        Vector2 shot = hook.ShotDirection;
        Vector2 toPlayer = position - origin;

        float along = Vector2.Dot(toPlayer, shot);
        float headAlong = Vector2.Dot((Vector2)hook.hookHead.position - origin, shot);
        if (along < headAlong || along > hook.maxDistance + dodgeRadius)
        {
            return Vector2.zero; // Already passed us, or can't reach us
        }

        Vector2 side = toPlayer - shot * along;
        if (side.magnitude > dodgeRadius)
        {
            return Vector2.zero;
        }
        if (side.sqrMagnitude < 0.0001f)
        {
            side = new Vector2(-shot.y, shot.x); // Dead center: pick a side
        }
        return side.normalized;
    }

    // Same look as the player script: mirror when moving left/right
    void Face(float x)
    {
        Vector3 scale = transform.localScale;
        if ((x < 0f && scale.x > 0f) || (x > 0f && scale.x < 0f))
        {
            scale.x = -scale.x;
            transform.localScale = scale;
        }
    }
}
