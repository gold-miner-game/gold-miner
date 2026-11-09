using UnityEngine;

// Owner: Butian (attack mode)
// The push of the pig's attack (MrHydeController.Attack) on an enemy that stands still: a miner or a
// hook hole (HookHealth). It slides the enemy away from the pig, stopped by walls, and keeps its
// resting hook in the moved spot (the hook isn't its child, so it would stay behind). Walking enemies
// (RedEnemy) take the push in their own movement instead (RedEnemy.PushBack).
public class Knockback : MonoBehaviour
{
    Vector2 dir;
    float speed, until = -1f;
    Hook hook;
    Transform hookHome;   // where the resting hook belongs (the miner's hook origin, or the hole)
    bool moved;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];

    // `distance` world units away along `away`, over `time` seconds
    public static void Push(HookHealth target, Vector2 away, float distance, float time)
    {
        if (target == null || away.sqrMagnitude < 0.0001f) return;
        RedEnemy walker = target.GetComponent<RedEnemy>();
        if (walker != null) { walker.PushBack(away, distance, time); return; }

        Knockback k = target.GetComponent<Knockback>();
        if (k == null)
        {
            k = target.gameObject.AddComponent<Knockback>();
            MinerAI miner = target.GetComponent<MinerAI>();
            SurvivalHookAI hole = target.GetComponent<SurvivalHookAI>();
            if (miner != null) { k.hook = miner.hook; k.hookHome = miner.hookOrigin; }
            else if (hole != null) { k.hook = hole.hook; k.hookHome = target.transform; }
        }
        k.dir = away.normalized;
        k.speed = distance / Mathf.Max(0.01f, time);
        k.until = Time.time + time;
    }

    void Update()
    {
        if (Time.time >= until) return;
        float step = speed * Time.deltaTime;
        if (Blocked(step + 0.05f)) { until = -1f; return; }
        transform.position += (Vector3)(dir * step);
        moved = true;
    }

    void LateUpdate()
    {
        if (moved && hook != null && hookHome != null && !hook.IsBusy)
            hook.transform.position = hookHome.position;
    }

    // A wall, a rock, or anything solid that isn't part of this enemy, the player or treasure
    bool Blocked(float length)
    {
        HookHealth health = GetComponent<HookHealth>();
        Collider2D box = health != null ? health.Box : GetComponent<Collider2D>();
        if (box == null) return false;
        Bounds b = box.bounds;
        int n = Physics2D.BoxCastNonAlloc(b.center, b.size * 0.9f, 0f, dir, hits, length);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null || c.isTrigger || c == box) continue;
            if (c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<PlayerCharacter>() != null || c.GetComponentInParent<Treasure>() != null) continue;
            if (c.GetComponentInParent<Hook>() != null || c.GetComponentInParent<HookHealth>() != null) continue;
            return true;
        }
        return false;
    }
}
