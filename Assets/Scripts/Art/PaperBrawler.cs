using UnityEngine;

// Art: the Red Square drawn as a felt Meowmeow brawler (red boxing gloves, red headband, glowing
// red eyes; Meshy-painted from the miner cat, Resources/Art/Enemies/Brawler_*), acted out on the
// stop-motion pose clock. Art only: it swaps the square's own root sprite and flip, so the box
// collider, HP blink (HookHealth), frost (FrostBody2D) and every gameplay value stay as they were.
//   asleep ............ Guard, standing its ground
//   walking ........... a waddle: Guard tipped one way, Guard, tipped the other, Guard (_w1/_w2)
//   close to the player Windup: crouched, glove cocked back
//   ram that lands .... Punch (glove thrust at you, speed strips), then Guard while it backs off
//   ram blocked / shoved Bonk: dizzy, stars, headband over one eye, until it charges again
// It faces the player (flipX), turning only on a new pose like a hand-moved puppet.
[DisallowMultipleComponent]
public class PaperBrawler : MonoBehaviour
{
    [Tooltip("World units to the player at which it cocks its glove back")]
    public float windupRange = 1.8f;
    [Tooltip("Seconds the punch pose is held after a ram that lands")]
    public float punchHold = 0.3f;

    static Sprite guard, walkA, walkB, windup, punch, bonk;

    RedSquare square;
    SpriteRenderer look;
    Vector3 lastPosition;
    int lastFrame = int.MinValue, walkStep;
    float punchUntil = -1f;
    bool wasRammed;
    Sprite shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { guard = walkA = walkB = windup = punch = bonk = null; }

    static bool Load()
    {
        if (guard != null) return true;
        guard = ArtLibrary.Get("Enemies/Brawler_Guard");
        walkA = ArtLibrary.Get("Enemies/Brawler_Guard_w1");
        walkB = ArtLibrary.Get("Enemies/Brawler_Guard_w2");
        windup = ArtLibrary.Get("Enemies/Brawler_Windup");
        punch = ArtLibrary.Get("Enemies/Brawler_Punch");
        bonk = ArtLibrary.Get("Enemies/Brawler_Bonk");
        if (walkA == null) walkA = guard;
        if (walkB == null) walkB = guard;
        if (windup == null) windup = guard;
        if (punch == null) punch = guard;
        if (bonk == null) bonk = guard;
        return guard != null;
    }

    void Start()
    {
        square = GetComponent<RedSquare>();
        look = GetComponent<SpriteRenderer>();
        if (square == null || look == null || !Load()) { enabled = false; return; }   // keeps the old felt block
        look.color = Color.white;   // painted felt, not a tinted block
        lastPosition = transform.position;
        Show(guard);
    }

    void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;   // paused: a still photo

        bool rammed = square.Rammed;
        if (rammed && !wasRammed && !square.LastRamBlocked) punchUntil = Time.time + punchHold;
        wasRammed = rammed;

        // The moment of a hit changes the picture at once; everything else waits for the next pose
        bool impact = Time.time < punchUntil || (rammed && square.LastRamBlocked) || square.BeingPushed;
        int frame = StopMotion.Frame;
        bool newPose = frame != lastFrame;
        if (!newPose && !(impact && shown != Pick(false))) return;
        lastFrame = frame;

        Vector3 now = transform.position;
        bool moving = (now - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = now;
        if (newPose && moving) walkStep++;

        Show(Pick(moving));
        Face();
    }

    Sprite Pick(bool moving)
    {
        if (square.BeingPushed) return bonk;
        if (Time.time < punchUntil) return punch;
        if (square.Recovering) return square.LastRamBlocked ? bonk : guard;
        Transform target = square.Target;
        if (square.Hunting && target != null
            && Vector2.Distance(transform.position, target.position) <= windupRange) return windup;
        if (!moving) return guard;
        switch (walkStep & 3) { case 0: return walkA; case 2: return walkB; default: return guard; }
    }

    void Face()
    {
        Transform target = square.Target;
        if (target == null) return;
        float dx = target.position.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.15f) look.flipX = dx < 0f;   // the art's headband tails stream to its right
    }

    void Show(Sprite sprite)
    {
        if (sprite == null || shown == sprite) return;
        shown = sprite;
        look.sprite = sprite;
    }
}
