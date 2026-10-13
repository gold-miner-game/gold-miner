using System.Collections;
using UnityEngine;

// Owner: C (Miner AI)
public class MinerAI : MonoBehaviour
{
    [Header("References")]
    public Hook hook;
    public TelegraphLine telegraph;
    public Transform hookOrigin;
    public Transform pig;

    [Header("Rhythm")]
    public int minTreasureShots = 2;   // treasure hooks before one player hook
    public int maxTreasureShots = 3;
    public float startDelay = 1.5f;
    public float telegraphMin = 1.0f;  // seconds the dotted line is shown
    public float telegraphMax = 2.0f;
    public float cooldown = 0.8f;
    public float overshoot = 0.6f;     // how far past the target the hook travels

    [Header("Colors")]
    public Color treasureColor = Color.yellow;
    public Color playerColor = Color.red;

    private int treasureShotsLeft;

    void Start()
    {
        treasureShotsLeft = Random.Range(minTreasureShots, maxTreasureShots + 1);
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        yield return new WaitForSeconds(startDelay);

        while (GameManager.Instance != null && GameManager.Instance.IsPlaying)
        {
            // 1. Pick a target
            bool aimAtPlayer = treasureShotsLeft <= 0;
            Vector2 targetPos = Vector2.zero;

            if (!aimAtPlayer)
            {
                Treasure t = PickTreasure();
                if (t != null)
                {
                    targetPos = t.transform.position;
                    treasureShotsLeft--;
                }
                else aimAtPlayer = true;
            }
            if (aimAtPlayer)
            {
                targetPos = pig.position;
                treasureShotsLeft = Random.Range(minTreasureShots, maxTreasureShots + 1);
            }

            // 2. Lock the trajectory now, so the player can react to it
            Vector2 origin = hookOrigin.position;
            Vector2 toTarget = targetPos - origin;
            Vector2 dir = toTarget.normalized;
            float length = toTarget.magnitude + overshoot;

            // 3. Telegraph: yellow = treasure, red = player
            telegraph.Show(origin, origin + dir * length, aimAtPlayer ? playerColor : treasureColor);
            yield return new WaitForSeconds(Random.Range(telegraphMin, telegraphMax));
            telegraph.Hide();
            if (!GameManager.Instance.IsPlaying) yield break;

            // 4. Fire, then wait for the hook to come back
            hook.Fire(origin, dir, length, aimAtPlayer ? playerColor : treasureColor);
            while (hook.IsBusy && GameManager.Instance.IsPlaying) yield return null;

            yield return new WaitForSeconds(cooldown);
        }
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
