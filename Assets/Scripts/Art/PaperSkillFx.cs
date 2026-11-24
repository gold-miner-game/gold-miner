using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: skill and item performances that used to be only a colour change, all posed on twos (StopMotion):
//   - Swift: cut-paper afterimages. Every other pose while the player runs faster than its base speed, a
//     pale copy of its cut-outs is left behind and fades over three poses (stop-motion's multiple exposure).
//   - Block (Mr. Hyde, and Brace): a guard ring of eight stitched paper plates snaps up round the body
//     (pop poses), then is held and nudged on threes while the block lasts.
//   - Roll (JK. Rolling): a felt dust puff kicked out behind the roll on every other pose.
//   - Time machine (Slowness): a cut-paper clock face on the floor under the player, fading in with the
//     slowness; its hands tick a pose at a time.
// Read-only: it only reads PlayerCharacter (CurrentSpeed, moveSpeed, IsBlocking), CatController.IsRolling and
// TimeMovesWithPlayer.Slowness. Everything drawn lives under its own root with FrostIgnore2D; the clock is on
// the floor layer (under every game piece); nothing is parented to a character; no colliders.
[DefaultExecutionOrder(32001)]   // after the pose layers (PaperStepSkills 31000, PaperPuppet 32000): ghosts copy the held poses
public class PaperSkillFx : MonoBehaviour
{
    const int ClockOrder = short.MinValue + 4;   // floor MinValue, strata +1, props +2, lamplight +3
    static readonly Color GhostTint = new Color(1f, 0.92f, 0.66f, 0.5f);
    static readonly float[] GuardPop = { 0.7f, 1.12f, 1f };
    static PaperSkillFx instance;

    class Ghost { public List<SpriteRenderer> parts = new List<SpriteRenderer>(); public int born; }

    readonly List<Ghost> ghosts = new List<Ghost>();
    readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    readonly List<SpriteRenderer> scratch = new List<SpriteRenderer>();
    Sprite guardArt, faceArt, handArt, hourArt;
    SpriteRenderer guard, face, minuteHand, hourHand;
    PlayerCharacter player;
    CatController cat;
    int lastFrame = -1, guardSince = -1, ticks;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Skill Fx");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperSkillFx>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start()
    {
        guardArt = ArtLibrary.Get("Fx/GuardRing");
        faceArt = ArtLibrary.Get("Fx/ClockFace");
        handArt = Hand("Fx/ClockHand");
        hourArt = Hand("Fx/ClockHourHand");
        guard = Piece("Guard Ring", guardArt, 0);
        face = Piece("Clock Face", faceArt, ClockOrder);
        minuteHand = Piece("Clock Hand", handArt, ClockOrder + 1);
        hourHand = Piece("Clock Hand", hourArt != null ? hourArt : handArt, ClockOrder + 1);
    }

    // A felt-card clock hand pointing up, pivoted on its rivet near the tail; 1 unit long.
    static Sprite Hand(string name)
    {
        Sprite art = ArtLibrary.Get(name);
        if (art == null) return null;
        Texture2D t = art.texture;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.48f, 0.045f), t.height, 0, SpriteMeshType.FullRect);
    }

    SpriteRenderer Piece(string pieceName, Sprite art, int order)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            player = GameManager.Instance != null ? FindAnyObjectByType<PlayerCharacter>() : null;
            cat = player as CatController;
        }
        int frame = StopMotion.Frame;
        bool newPose = frame != lastFrame;
        lastFrame = frame;

        FadeGhosts(frame, newPose);
        if (player == null || !player.isActiveAndEnabled || player.body == null)
        {
            guard.enabled = face.enabled = minuteHand.enabled = hourHand.enabled = false;
            return;
        }
        Vector3 at = player.body.bounds.center;
        float bodySize = Mathf.Max(player.body.bounds.size.x, player.body.bounds.size.y);

        if (newPose)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
            bool swift = player.CurrentSpeed > player.moveSpeed * 1.25f && speed > player.moveSpeed * 0.5f;
            if (swift && frame % 2 == 0) LeaveGhost(frame);
            if (cat != null && cat.IsRolling && frame % 2 == 0 && speed > 0.5f)
                PaperFx.Puff(at - (Vector3)(rb.linearVelocity.normalized * bodySize * 0.45f), Mathf.Clamp(bodySize, 0.4f, 1.2f));
        }
        Guard(at, bodySize, frame);
        Clock(at, bodySize, frame, newPose);
    }

    // ---- Swift afterimages ----
    void LeaveGhost(int frame)
    {
        var g = new Ghost { born = frame };
        scratch.Clear();
        player.GetComponentsInChildren(false, scratch);
        foreach (SpriteRenderer src in scratch)
        {
            if (!src.enabled || src.sprite == null || src.name == "ShieldBubble" || src.color.a < 0.05f
                || src.maskInteraction != SpriteMaskInteraction.None) continue;   // skip masked overlays (cooldown tint)
            SpriteRenderer copy = pool.Count > 0 ? pool.Pop() : NewGhostPart();
            copy.gameObject.SetActive(true);
            copy.sprite = src.sprite;
            copy.flipX = src.flipX; copy.flipY = src.flipY;
            copy.sortingLayerID = src.sortingLayerID;
            copy.sortingOrder = src.sortingOrder - 20;   // behind the character's own cut-outs
            copy.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            copy.transform.localScale = src.transform.lossyScale;
            copy.color = GhostTint;
            g.parts.Add(copy);
        }
        ghosts.Add(g);
    }

    SpriteRenderer NewGhostPart()
    {
        var go = new GameObject("Swift Ghost");
        go.transform.SetParent(transform, false);
        return go.AddComponent<SpriteRenderer>();
    }

    void FadeGhosts(int frame, bool newPose)
    {
        if (!newPose) return;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost g = ghosts[i];
            int age = frame - g.born;   // held poses: 0.5, 0.32, 0.16, gone
            float a = age <= 0 ? 0.5f : age == 1 ? 0.32f : age == 2 ? 0.16f : 0f;
            foreach (SpriteRenderer p in g.parts)
            {
                if (p == null) continue;
                if (a <= 0f) { p.gameObject.SetActive(false); pool.Push(p); }
                else p.color = new Color(GhostTint.r, GhostTint.g, GhostTint.b, a);
            }
            if (a <= 0f) ghosts.RemoveAt(i);
        }
    }

    // ---- Block guard ring ----
    void Guard(Vector3 at, float bodySize, int frame)
    {
        bool blocking = guardArt != null && player.IsBlocking;
        if (!blocking) { guard.enabled = false; guardSince = -1; return; }
        if (guardSince < 0) guardSince = frame;
        int step = frame - guardSince;
        float pop = step < GuardPop.Length ? GuardPop[step] : 1f;
        float nudge = (StopMotion.BoilFrame % 2 == 0 ? 1f : -1f) * 4f;   // handled, on threes
        guard.enabled = true;
        guard.sortingLayerID = player.body.sortingLayerID;
        guard.sortingOrder = player.body.sortingOrder + 30;
        guard.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, nudge));
        guard.transform.localScale = Vector3.one * bodySize * 1.7f * pop;
    }

    // ---- Time machine clock ----
    void Clock(Vector3 at, float bodySize, int frame, bool newPose)
    {
        float slow = faceArt != null && handArt != null ? TimeMovesWithPlayer.Slowness : 0f;
        bool show = slow > 0.02f;
        face.enabled = minuteHand.enabled = hourHand.enabled = show;
        if (!show) return;
        if (newPose && frame % 3 == 0) ticks++;   // the minute hand ticks a notch every third pose
        float size = Mathf.Max(3.2f, bodySize * 5f);
        float alpha = Mathf.Clamp01(slow) * 0.45f;   // the painted face is bright: a wash, not a sticker
        face.transform.SetPositionAndRotation(new Vector3(at.x, at.y, 0f), Quaternion.identity);
        face.transform.localScale = Vector3.one * size;
        face.color = new Color(1f, 1f, 1f, alpha);
        minuteHand.transform.SetPositionAndRotation(new Vector3(at.x, at.y, 0f), Quaternion.Euler(0f, 0f, -ticks * 6f));
        minuteHand.transform.localScale = Vector3.one * size * 0.4f;
        minuteHand.color = new Color(1f, 1f, 1f, alpha);
        hourHand.transform.SetPositionAndRotation(new Vector3(at.x, at.y, 0f), Quaternion.Euler(0f, 0f, -ticks * 0.5f - 90f));
        hourHand.transform.localScale = Vector3.one * size * 0.26f;   // both hands are 1 unit long as sprites: the hour hand is drawn shorter here
        hourHand.color = new Color(1f, 1f, 1f, alpha);
    }
}
