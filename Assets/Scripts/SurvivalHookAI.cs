using System.Collections;
using UnityEngine;

// Owner: Butian (Survival mode)
// The hook AI of survival mode only (SurvivalLoader makes one per hole). Normal levels keep MinerAI.
// It has a single aim, the player: no treasure shots, and the hook it fires passes through treasure.
// It only shoots while the player is within range, and asks its HookAimer where to shoot
// (TreasureInterceptAimer: catch the player halfway to the gold it is heading for). When the aimer
// has no meaningful shot, it shoots straight at the player.
public class SurvivalHookAI : MonoBehaviour
{
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

    void Start()
    {
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        yield return new WaitForSeconds(startDelay);

        while (GameManager.Instance != null && GameManager.Instance.IsPlaying)
        {
            if (target == null) yield break;
            Vector2 origin = hookOrigin.position;
            if (range > 0f && Vector2.Distance(origin, target.position) > range) { yield return null; continue; }

            // The aimer needs the telegraph time: the hook only leaves once it is over
            float telegraphTime = Random.Range(telegraphMin, telegraphMax);
            // No meaningful guess from the aimer: shoot straight at where the player is
            Vector2 aimAt;
            if (aimer == null || !aimer.TryAim(origin, target, telegraphTime, hook.extendSpeed, out aimAt))
                aimAt = target.position;

            // Lock the trajectory now, so the player can react to it
            Vector2 toTarget = aimAt - origin;
            if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector2.down;
            Vector2 dir = toTarget.normalized;
            float length = toTarget.magnitude + overshoot;

            telegraph.Show(origin, origin + dir * length, shotColor);
            yield return new WaitForSeconds(telegraphTime);
            telegraph.Hide();
            if (!GameManager.Instance.IsPlaying) yield break;

            hook.Fire(origin, dir, length, shotColor);
            while (hook.IsBusy && GameManager.Instance.IsPlaying) yield return null;

            yield return new WaitForSeconds(cooldown);
        }
    }
}
