using System;
using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (attacking the hooks)
// A hook the player can destroy: a miner (MinerAI) or a hook hole (SurvivalHookAI). It gets a solid
// box around what it draws, so the player bumps into it. Mr. Hyde's Space next to it (within
// MrHydeController.attackReach of the box) takes a point off: it blinks. At 0 it stops shooting,
// blinks fast for a moment and is gone. LevelLoader adds one to every hook in a level with
// "hook_hp" (the block tutorial).
public class HookHealth : MonoBehaviour
{
    public int hp = 1;
    [Tooltip("Flashes after a hit that doesn't destroy it (it can't be hit again meanwhile)")]
    public int hitBlinks = 2;
    [Tooltip("Flashes before it disappears")]
    public int deathBlinks = 2;
    [Tooltip("Seconds one flash takes (hidden, then shown)")]
    public float blinkTime = 0.2f;
    [Tooltip("Switched off with it when it is destroyed (its hook, its telegraph, the hole's root)")]
    public GameObject[] parts = new GameObject[0];

    public const string DamagedEvent = "hook_damaged";
    public const string DestroyedEvent = "hook_destroyed";

    public static event Action<HookHealth> Destroyed;

    static readonly List<HookHealth> all = new List<HookHealth>();

    public bool Alive => hp > 0;
    public BoxCollider2D Box { get; private set; }

    SpriteRenderer[] sprites;
    float blinkUntil = -1f;
    float blinkStart;
    bool dying;

    void Awake()
    {
        // The body only: not the hook or the telegraph dots (children of a miner), and only the
        // ones that show, so a hidden placeholder stays hidden after blinking
        sprites = Array.FindAll(GetComponentsInChildren<SpriteRenderer>(true), sr =>
            sr.enabled && !sr.forceRenderingOff
            && sr.GetComponentInParent<Hook>(true) == null && sr.GetComponentInParent<TelegraphLine>(true) == null);
        Box = FitBox();
    }

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); Destroyed = null; }   // Play without a domain reload keeps statics

    // A solid box around the visible sprites (static: no body, it never moves)
    BoxCollider2D FitBox()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null) return box;
        box = gameObject.AddComponent<BoxCollider2D>();
        bool any = false;
        Bounds world = new Bounds(transform.position, Vector3.zero);
        foreach (SpriteRenderer sr in sprites)
        {
            if (sr == null || sr.sprite == null) continue;
            if (any) world.Encapsulate(sr.bounds);
            else { world = sr.bounds; any = true; }
        }
        if (!any) { box.size = Vector2.one; return box; }
        Vector3 scale = transform.lossyScale;
        box.offset = transform.InverseTransformPoint(world.center);
        box.size = new Vector2(world.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                               world.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)));
        return box;
    }

    // The closest living hook whose box is within `reach` of `from`, or null
    public static HookHealth InReach(Vector2 from, float reach)
    {
        HookHealth best = null;
        float bestDistance = reach;
        foreach (HookHealth h in all)
        {
            if (h == null || !h.Alive || h.Box == null) continue;
            float d = Vector2.Distance(from, h.Box.ClosestPoint(from));
            if (d <= bestDistance) { bestDistance = d; best = h; }
        }
        return best;
    }

    // One hit. Ignored while it still blinks from the last one
    public void Damage(int amount = 1)
    {
        if (!Alive || Time.time < blinkUntil) return;
        hp = Mathf.Max(0, hp - amount);
        TutorialEvents.Fire(DamagedEvent);
        blinkStart = Time.time;
        if (hp > 0) { blinkUntil = blinkStart + hitBlinks * blinkTime; return; }

        // Out: stop shooting at once, blink, then go
        dying = true;
        blinkUntil = blinkStart + deathBlinks * blinkTime;
        StopShooting();
    }

    void StopShooting()
    {
        MinerAI miner = GetComponent<MinerAI>();
        if (miner != null)
        {
            miner.StopAllCoroutines();
            if (miner.telegraph != null) miner.telegraph.Hide();
            if (miner.hook != null) miner.hook.gameObject.SetActive(false);
        }
        SurvivalHookAI hole = GetComponent<SurvivalHookAI>();
        if (hole != null)
        {
            hole.StopAllCoroutines();
            if (hole.telegraph != null) hole.telegraph.Hide();
            if (hole.hook != null) hole.hook.gameObject.SetActive(false);
        }
    }

    // Time.time: a frozen game holds the blinking too
    void Update()
    {
        bool blinking = Time.time < blinkUntil;
        if (blinking || wasBlinking)   // only while blinking, so nothing else's hiding is undone
        {
            // Each flash: hidden for the first half, shown for the second
            bool show = !blinking || blinkTime <= 0f || Mathf.Repeat(Time.time - blinkStart, blinkTime) >= blinkTime * 0.5f;
            foreach (SpriteRenderer sr in sprites) if (sr != null) sr.enabled = show;
        }
        wasBlinking = blinking;

        if (dying && !blinking) Gone();
    }

    bool wasBlinking;

    void Gone()
    {
        dying = false;
        TutorialEvents.Fire(DestroyedEvent);
        Destroyed?.Invoke(this);
        foreach (GameObject part in parts) if (part != null) part.SetActive(false);
        gameObject.SetActive(false);
    }
}
