using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owner: C (Miner AI)
public class MinerAI : MonoBehaviour
{
    // Maze tutorial: when false the miner waits until Arm(true) or FireScripted.
    public void Arm(bool on)
    {
        autoShoot = on;
        if (on && startDelay > 10f) startDelay = 0.2f;
    }

    // One scripted shot at a world point (yellow treasure path, or red if aimAtPlayer).
    public IEnumerator FireScripted(Vector2 targetPos, bool aimAtPlayer)
    {
        if (hook == null || hookOrigin == null) yield break;
        bool prev = autoShoot;
        autoShoot = false;

        Vector2 origin = hookOrigin.position;
        Vector2 toTarget = targetPos - origin;
        Vector2 dir = toTarget.normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;
        float length = toTarget.magnitude + overshoot;

        Color color = aimAtPlayer ? playerColor : treasureColor;
        // windup 0 = fire immediately (E/M tutorial). Skip the telegraph wait entirely.
        if (telegraph != null && telegraphMin > 0.02f)
        {
            telegraph.Show(origin, origin + dir * length, color);
            float wait = telegraphMin;
            float end = Time.time + wait;
            while (Time.time < end) yield return null;
            telegraph.Hide();
        }

        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
        {
            autoShoot = prev;
            yield break;
        }

        hook.AimedAtPlayer = aimAtPlayer;
        hook.Fire(origin, dir, length, color);
        while (hook.IsBusy && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
            yield return null;

        autoShoot = prev;
    }

    public enum Strategy
    {
        Weighted,    // random treasure, weighted by value
        Efficient,   // best value per shot time from this hook, skipping treasure another miner is going for
    }

    [Header("References")]
    public Hook hook;
    public TelegraphLine telegraph;
    public Transform hookOrigin;
    public Transform pig;

    [Header("Rhythm")]
    public int minTreasureShots = 2;   // treasure hooks before one player hook
    public int maxTreasureShots = 3;
    [Tooltip("> 0: each shot rolls this chance to aim at the pig instead, never twice in a row " +
             "(replaces the treasure shot counts above)")]
    [Range(0f, 1f)]
    public float pigShotChance = 0f;
    public float startDelay = 1.5f;
    public float telegraphMin = 1.0f;  // seconds the dotted line is shown
    public float telegraphMax = 2.0f;
    public float cooldown = 0.8f;
    public float overshoot = 0.6f;     // how far past the target the hook travels

    [Header("Targeting")]
    public Strategy strategy = Strategy.Weighted;
    public bool shootsPlayer = true;   // false: only treasure shots (no-kill levels)
    public bool huntPigOnly;          // never gold: every shot aims at the pig
    public float huntReach;           // 0: stop just past the pig. Above 0: the rope travels at least this far
    public bool aimThroughPig;        // only treasure whose line crosses the pig's start
    public bool greedyValue;          // biggest treasure first, then keep competing for the rest
    public bool resumeAfterScripted;  // a scripted opening pull, then shoot on its own
    public bool autoShoot = true;      // false: the miner waits (a tutorial drives the hook itself)

    [Header("Colors")]
    public Color treasureColor = Color.yellow;
    public Color playerColor = Color.red;

    private int treasureShotsLeft;
    private bool lastShotAtPig;

    // Treasure each miner is going for, until its hook is home (Efficient strategy)
    private static readonly Dictionary<MinerAI, Treasure> claims = new Dictionary<MinerAI, Treasure>();
    private static readonly List<MinerAI> miners = new List<MinerAI>();

    // From the telegraph until the hook is home: the hook's origin must stay put (a walking enemy waits)
    public bool Aiming { get; private set; }

    // Where the lane was when the level started. Gold hooks keep aiming through this spot
    // after the pig runs off to wait.
    Vector2 lanePoint;
    bool lanePointSet;

    void OnEnable()
    {
        if (!miners.Contains(this)) miners.Add(this);
    }

    void OnDisable()
    {
        miners.Remove(this);
        claims.Remove(this);
        Aiming = false;
    }

    void Start()
    {
        if (pig != null)
        {
            lanePoint = pig.position;
            lanePointSet = true;
        }
        treasureShotsLeft = Random.Range(minTreasureShots, maxTreasureShots + 1);
        StartCoroutine(Loop());
    }

    // Waits in game time, except during bullet time (TimeMovesWithPlayer slowing the world while the
    // player stands still): then in real time, so the miner keeps aiming and firing at its usual pace
    // and only the hook flies in slow motion. A pause (time scale 0) still holds it.
    static IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            yield return null;
            if (Time.timeScale <= 0f) continue;
            t += TimeMovesWithPlayer.Slowness > 0f ? Time.unscaledDeltaTime : Time.deltaTime;
        }
    }

    IEnumerator Loop()
    {
        yield return Wait(startDelay);

        while (GameManager.Instance != null && GameManager.Instance.IsPlaying)
        {
            if (!autoShoot)
            {
                // The opening diamond was this hook's first grab. Once that pull is over, it
                // goes back out for whatever gold is still lying around.
                if (resumeAfterScripted && hook != null && !hook.IsBusy)
                {
                    autoShoot = true;
                    aimThroughPig = false;
                }
                else
                {
                    yield return null;
                    continue;
                }
            }

            // 1. Pick a target. A hunter never looks at gold.
            bool aimAtPlayer = huntPigOnly || (shootsPlayer && PigShotDue());
            Vector2 targetPos = Vector2.zero;

            if (huntPigOnly && pig == null) { yield return null; continue; }

            if (!aimAtPlayer)
            {
                Treasure t = greedyValue ? PickGreedy()
                    : strategy == Strategy.Efficient ? PickEfficient() : PickTreasure();
                if (t != null)
                {
                    targetPos = t.transform.position;
                    treasureShotsLeft--;
                    if (strategy == Strategy.Efficient) claims[this] = t;
                }
                else if (shootsPlayer) aimAtPlayer = true;
                else { yield return null; continue; }   // nothing to shoot at
            }
            if (aimAtPlayer)
            {
                targetPos = pig.position;
                treasureShotsLeft = Random.Range(minTreasureShots, maxTreasureShots + 1);
            }
            lastShotAtPig = aimAtPlayer;
            Aiming = true;

            // 2. Lock the trajectory now, so the player can react to it
            Vector2 origin = hookOrigin.position;
            Vector2 toTarget = targetPos - origin;
            Vector2 dir = toTarget.normalized;
            float length = toTarget.magnitude + overshoot;
            // A hunter's rope crosses the whole arena, so the pig is in range from anywhere on screen.
            if (aimAtPlayer && huntReach > 0f) length = Mathf.Max(length, huntReach);

            // 3. Telegraph: yellow = treasure, red = player
            telegraph.Show(origin, origin + dir * length, aimAtPlayer ? playerColor : treasureColor);
            if (aimAtPlayer) TutorialEvents.Fire(SurvivalHookAI.AimEvent);
            yield return Wait(Random.Range(telegraphMin, telegraphMax));
            telegraph.Hide();
            if (!GameManager.Instance.IsPlaying) { Aiming = false; yield break; }

            // 4. Fire, then wait for the hook to come back
            hook.AimedAtPlayer = aimAtPlayer;
            hook.Fire(origin, dir, length, aimAtPlayer ? playerColor : treasureColor);
            if (aimAtPlayer) TutorialEvents.Fire(SurvivalHookAI.FireEvent);
            TutorialEvents.Fire(SurvivalHookAI.ShotEvent);   // every shot, the gold ones too
            while (hook.IsBusy && GameManager.Instance.IsPlaying) yield return null;
            claims.Remove(this);
            Aiming = false;
            // The lane shot (the caged gold) is done. Later shots chase whatever is left.
            if (greedyValue && aimThroughPig && !lastShotAtPig) aimThroughPig = false;
            if (aimAtPlayer && GameManager.Instance.IsPlaying && !hook.HitPlayer) TutorialEvents.Fire(SurvivalHookAI.DodgedEvent);
            yield return Wait(cooldown);
        }
    }

    // Whether this shot goes for the pig: a random roll when pigShotChance is set (but never right
    // after a pig shot), otherwise after the counted treasure shots
    bool PigShotDue()
    {
        if (pigShotChance > 0f) return !lastShotAtPig && Random.value < pigShotChance;
        return treasureShotsLeft <= 0;
    }

    // Biggest piece whose line still crosses the pig, then the biggest piece nobody else is
    // lining up through the pig. Claimed gold is skipped until nothing else is left.
    Treasure PickGreedy()
    {
        if (aimThroughPig)
        {
            Treasure lane = BestValue(true, true, false);
            if (lane != null) return lane;
        }
        Treasure free = BestValue(true, false, true);
        return free != null ? free : BestValue(false, false, true);
    }

    Treasure BestValue(bool skipClaimed, bool laneOnly, bool skipReserved)
    {
        Treasure best = null;
        int bestValue = int.MinValue;
        float bestDist = float.PositiveInfinity;
        Vector2 origin = hookOrigin.position;
        foreach (Treasure t in Treasure.Active)
        {
            if (t == null) continue;
            if (laneOnly && !CrossesPig(t)) continue;
            if (skipReserved && ReservedForOtherLane(t)) continue;
            if (skipClaimed && ClaimedByOther(t)) continue;
            float d = Vector2.Distance(origin, t.transform.position);
            if (t.value > bestValue || (t.value == bestValue && d < bestDist))
            {
                bestValue = t.value;
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    bool ReservedForOtherLane(Treasure t)
    {
        foreach (MinerAI other in miners)
        {
            if (other == this || other == null || !other.aimThroughPig || other.huntPigOnly) continue;
            if (other.CrossesPig(t)) return true;
        }
        return false;
    }

    bool CrossesPig(Treasure t)
    {
        if (!lanePointSet && pig == null) return false;
        Vector2 p = lanePointSet ? lanePoint : (Vector2)pig.position;
        Vector2 a = hookOrigin.position;
        Vector2 b = t.transform.position;
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.01f) return false;
        float u = Vector2.Dot(p - a, ab) / len2;
        if (u < 0.12f || u > 0.88f) return false;
        Vector2 closest = a + ab * u;
        return (p - closest).sqrMagnitude < 0.45f * 0.45f;
    }

    // Value per second of a full shot at it (telegraph, out, back with its weight, cooldown), as if
    // there were no pig. Treasure another miner is going for is skipped unless nothing else is left.
    Treasure PickEfficient()
    {
        Treasure best = Best(true);
        return best != null ? best : Best(false);
    }

    Treasure Best(bool skipClaimed)
    {
        Vector2 origin = hookOrigin.position;
        float telegraphMean = 0.5f * (telegraphMin + telegraphMax);
        Treasure best = null;
        float bestRate = float.NegativeInfinity;
        foreach (Treasure t in Treasure.Active)
        {
            if (skipClaimed && ClaimedByOther(t)) continue;
            float d = Vector2.Distance(origin, t.transform.position);
            float shot = telegraphMean + d / hook.extendSpeed
                       + d / (hook.retractSpeed * hook.RetractMultiplier(t.type)) + cooldown;
            float rate = t.value / shot;
            if (rate > bestRate) { bestRate = rate; best = t; }
        }
        return best;
    }

    bool ClaimedByOther(Treasure t)
    {
        foreach (var kv in claims)
            if (kv.Key != this && kv.Value == t) return true;
        return false;
    }

    // Weighted random: a $600 diamond is picked 12 times as often as a $50 gold
    Treasure PickTreasure()
    {
        int total = 0;
        foreach (Treasure t in Treasure.Active) total += Mathf.Max(1, t.value);
        if (total == 0) return null;

        int roll = Random.Range(0, total);
        foreach (Treasure t in Treasure.Active)
        {
            roll -= Mathf.Max(1, t.value);
            if (roll < 0) return t;
        }
        return null;
    }
}
