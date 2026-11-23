using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (Survival mode)
// The hook AI of survival mode only (SurvivalLoader makes one per hole). Normal levels keep MinerAI.
// It has a single aim, the player: no treasure shots, and the hook it fires passes through treasure.
// It only shoots while the player is within range, and asks its HookAimer where to shoot
// (TreasureInterceptAimer: catch the player halfway to the gold it is heading for). When the aimer
// has no meaningful shot, it shoots straight at the player.
public class SurvivalHookAI : MonoBehaviour
{
    // TutorialEvents for the tutorial steps (the dodge tutorial uses these holes too)
    public const string AimEvent = "hook_aim";        // the red dotted line appears; the aim is locked
    public const string FireEvent = "hook_fire";      // the hook leaves the hole
    public const string ShotEvent = "hook_shot";      // any hook leaves its hole, at the player or at gold (MinerAI)
    public const string DodgedEvent = "hook_dodged";  // it came back without hitting the player


    [Header("References")]
    public Hook hook;
    public TelegraphLine telegraph;
    public Transform hookOrigin;
    public Transform target;
    [Tooltip("Guesses where the target will be. Empty: aim at where it is now")]
    public HookAimer aimer;

    [Header("Rhythm")]
    public float startDelay = 1.5f;
    public float telegraphMin = 1f;    // seconds the dotted line is shown
    public float telegraphMax = 1.8f;
    public float cooldown = 0.8f;
    public float overshoot = 0.6f;     // how far past the aim point the hook travels
    [Tooltip("Only shoot while the target is this close to the hook origin (0 = any distance)")]
    public float range = 11f;
    public Color shotColor = Color.red;

    [Tooltip("Hold every shot while on (attack mode's Red Round shoots only with a clear shot)")]
    public bool holdFire;

    [Tooltip("A gold hook (roam mode's Gold Round): the target is a piece of treasure, not the player. " +
             "The shot is yellow, aims straight at it, and fires none of the player-shot events")]
    public bool goldOnly;

    // From the telegraph until the hook is home: the hook's origin must stay put (RedRound waits)
    public bool Aiming { get; private set; }

    static readonly List<SurvivalHookAI> controllers = new List<SurvivalHookAI>();
    SpriteRenderer[] frostBody;
    public bool IsFrozen
    {
        get
        {
            if (frostBody == null) frostBody = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sprite in frostBody)
                if (sprite != null && sprite.GetComponentInParent<Hook>() == null
                    && sprite.GetComponentInParent<TelegraphLine>() == null && FrostField2D.IsFrozen(sprite)) return true;
            return false;
        }
    }
    void OnTransformChildrenChanged() { frostBody = null; }
    public static bool ControllerFrozen(Hook controlled)
    {
        foreach (var controller in controllers)
            if (controller != null && controller.hook == controlled && controller.IsFrozen) return true;
        return false;
    }
    void OnEnable() { if (!controllers.Contains(this)) controllers.Add(this); }
    void OnDisable() { controllers.Remove(this); Aiming = false; }

    IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return null;
            if (!IsFrozen) elapsed += Time.deltaTime;
        }
    }

    void Start()
    {
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        yield return Wait(startDelay);

        while (GameManager.Instance != null && GameManager.Instance.IsPlaying)
        {
            // A gold hook waits for its walker to pick the next piece; a player hook has nothing left to do
            if (target == null) { if (goldOnly) { yield return null; continue; } yield break; }
            if (holdFire || IsFrozen) { yield return null; continue; }
            Vector2 origin = hookOrigin.position;
            if (range > 0f && Vector2.Distance(origin, target.position) > range) { yield return null; continue; }

            // The aimer needs the telegraph time: the hook only leaves once it is over
            float telegraphTime = Random.Range(telegraphMin, telegraphMax);
            // No meaningful guess from the aimer: shoot straight at where the player is
            Vector2 aimAt;
            if (goldOnly || aimer == null || !aimer.TryAim(origin, target, telegraphTime, hook.extendSpeed, out aimAt))
                aimAt = target.position;

            // Lock the trajectory now, so the player can react to it
            Vector2 toTarget = aimAt - origin;
            if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector2.down;
            Vector2 dir = toTarget.normalized;
            float length = toTarget.magnitude + overshoot;

            Aiming = true;
            telegraph.Show(origin, origin + dir * length, shotColor);
            if (!goldOnly) TutorialEvents.Fire(AimEvent);
            yield return Wait(telegraphTime);
            telegraph.Hide();
            if (!GameManager.Instance.IsPlaying) { Aiming = false; yield break; }

            hook.AimedAtPlayer = !goldOnly;
            hook.Fire(origin, dir, length, shotColor);
            if (!goldOnly) TutorialEvents.Fire(FireEvent);
            TutorialEvents.Fire(ShotEvent);
            while (hook.IsBusy && GameManager.Instance.IsPlaying) yield return null;
            Aiming = false;
            if (!goldOnly && GameManager.Instance.IsPlaying && !hook.HitPlayer) TutorialEvents.Fire(DodgedEvent);

            yield return Wait(cooldown);
        }
    }
}
