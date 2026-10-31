using System.Collections;
using UnityEngine;

// level_yk only: each listed miner keeps shooting at the treasure chosen here for as long as it
// exists, then its MinerAI picks every shot after that as usual. The shots are the miner's own:
// same start delay, telegraph, hook and cooldown, so they look and pay like normal treasure shots.
// A hook still stops at the first thing on its line, so a shot at the target can haul whatever
// treasure is in front of it instead.
// It also sets the level's invincible time after a hit (none, by default) and makes every rock a
// permanent wall.
public class LevelYkOpening : MonoBehaviour
{
    [System.Serializable]
    public class Opening
    {
        public MinerAI miner;
        public Treasure target;
    }

    [Tooltip("One entry per miner: it shoots at this treasure until the treasure is gone. A miner " +
             "not listed here (or with an empty target) shoots as usual from the start")]
    public Opening[] openings;

    [Tooltip("Seconds after a hit when the pig can't lose another Life, set on the GameManager when " +
             "the level starts. 0: every hook that touches the pig costs a Life")]
    public float invincibleTime = 0f;

    [Tooltip("On: every rock in the scene is a permanent wall (Rock.MakeWall), so a hook stops on it " +
             "without breaking it. Off: rocks shatter after blocking one shot, like battle rocks")]
    public bool rocksAreWalls = true;

    void Start()
    {
        // GameManager.Instance is set in its Awake, so it exists by the time any Start runs
        if (GameManager.Instance != null) GameManager.Instance.invincibleTime = invincibleTime;
    }

    void Awake()
    {
        // The corridor only works if its walls survive every hook
        if (rocksAreWalls)
            foreach (Rock rock in FindObjectsByType<Rock>(FindObjectsSortMode.None)) rock.MakeWall();

        // Awake runs before every Start, so the miners never pick a target themselves meanwhile
        foreach (Opening o in openings)
        {
            if (o.miner == null || o.target == null) continue;
            o.miner.autoShoot = false;
            StartCoroutine(ShootAtTarget(o));
        }
    }

    IEnumerator ShootAtTarget(Opening o)
    {
        MinerAI miner = o.miner;
        yield return new WaitForSeconds(miner.startDelay);

        // Eaten or delivered treasure is destroyed, so the target turns null when it is gone
        while (Playing && o.target != null)
        {
            // On another miner's hook: wait to see whether it gets home or is knocked loose
            if (o.target.IsTaken) { yield return null; continue; }

            Vector2 origin = miner.hookOrigin.position;
            Vector2 toTarget = (Vector2)o.target.transform.position - origin;
            Vector2 dir = toTarget.normalized;
            float length = toTarget.magnitude + miner.overshoot;

            miner.telegraph.Show(origin, origin + dir * length, miner.treasureColor);
            yield return new WaitForSeconds(Random.Range(miner.telegraphMin, miner.telegraphMax));
            miner.telegraph.Hide();
            if (!Playing) break;

            miner.hook.Fire(origin, dir, length, miner.treasureColor);
            while (miner.hook.IsBusy && Playing) yield return null;
            yield return new WaitForSeconds(miner.cooldown);
        }

        miner.autoShoot = true;   // MinerAI's own loop takes over from the next shot
    }

    static bool Playing => GameManager.Instance != null && GameManager.Instance.IsPlaying;
}
