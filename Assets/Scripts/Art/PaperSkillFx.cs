using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: skill and item performances that used to be only a colour change, all posed on twos (StopMotion):
//   - Swift: no effect on purpose; the speed itself is the feedback.
//   - Block (Mr. Hyde, and Brace): no effect here; the body turning blue (BlockFlash) says it (playtest: the
//     paper guard ring read as clutter).
//   - Roll (JK. Rolling): a felt dust puff kicked out behind the roll on every other pose.
//   - Shield: the paper-bag helmet worn over the head (PlayerCharacter's ShieldBubble, covering everything
//     above the eyes); when the shield runs out it tears into three pieces that burst out and fall away over
//     five held poses, with a felt puff.
//   - Time machine (Slowness): the floor layer (felt floor, strata, props, lamplight) bleaches toward white
//     paper in held steps as time slows, so every game piece stands out in full colour on a blank page.
// Read-only: it only reads PlayerCharacter (CurrentSpeed, moveSpeed, IsBlocking), CatController.IsRolling and
// TimeMovesWithPlayer.Slowness. Everything drawn lives under its own root with FrostIgnore2D; the wash is on
// the floor layer (under every game piece); nothing is parented to a character; no colliders.
[DefaultExecutionOrder(32003)]   // after the pose layers (PaperStepSkills 31000, PaperPuppet 32000, PaperHolds 32002): effects sit on the held poses
public class PaperSkillFx : MonoBehaviour
{
    const int WashOrder = short.MinValue + 4;   // floor MinValue, strata +1, props +2, lamplight +3: over all of them
    const float WashMax = 0.6f;                  // the floor still shows faintly through, like thin paper
    static PaperSkillFx instance;

    SpriteRenderer wash;
    // the bag's tear: pieces A (upper left), B (upper right), C (the cuff); where each flies (in bag sizes)
    Sprite[] bagPieces;
    SpriteRenderer[] tear;
    static readonly Vector2[] TearFrom = { new Vector2(-0.24f, 0.12f), new Vector2(0.24f, 0.12f), new Vector2(0f, -0.3f) };
    static readonly Vector2[] TearTo = { new Vector2(-0.8f, 0.45f), new Vector2(0.85f, 0.4f), new Vector2(0.05f, -0.75f) };
    static readonly float[] TearSpin = { 40f, -48f, 12f };
    static readonly float[] TearWidth = { 0.52f, 0.53f, 1f };   // piece width as a share of the helmet's
    static readonly float[] TearPose = { 0f, 0.35f, 0.62f, 0.82f, 1f };   // held poses: a burst, then drifting
    SpriteRenderer bag;      // the player's bag (ShieldBubble), watched for the moment it goes away
    bool bagWasOn;
    Vector3 bagAt; Vector2 bagSize; int bagOrder, bagLayer;
    int tearFrom = -1;
    PlayerCharacter player;
    CatController cat;
    int lastFrame = -1;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Skill Fx");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperSkillFx>();
        SceneManager.sceneLoaded -= OnSceneLoaded;   // domain reload is off: never stack
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (instance != null) { instance.nextScan = 0f; instance.ForgetBag(); } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        bagPieces = new[] { ArtLibrary.Get("Fx/HelmetPieceA"), ArtLibrary.Get("Fx/HelmetPieceB"), ArtLibrary.Get("Fx/HelmetPieceC") };
        tear = new SpriteRenderer[3];
        for (int i = 0; i < 3; i++) tear[i] = Piece("Bag Piece", bagPieces[i], 0);
        Texture2D white = Texture2D.whiteTexture;   // one unit of plain white, stretched over the view
        wash = Piece("Time Wash", Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width), WashOrder);
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
            PlayerCharacter found = GameManager.Instance != null ? FindAnyObjectByType<PlayerCharacter>() : null;
            if (found != player) ForgetBag();   // a new character: the old bag's history is not its own
            player = found;
            cat = player as CatController;
            bag = null;
            if (player != null)
                foreach (SpriteRenderer r in player.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (r.name == "ShieldBubble") bag = r;
                }
        }
        int frame = StopMotion.Frame;
        bool newPose = frame != lastFrame;
        lastFrame = frame;

        if (player == null || !player.isActiveAndEnabled || player.body == null)
        {
            wash.enabled = false;
            ForgetBag();
            if (tear != null) foreach (SpriteRenderer r in tear) r.enabled = false;
            return;
        }
        Vector3 at = player.body.bounds.center;
        float bodySize = Mathf.Max(player.body.bounds.size.x, player.body.bounds.size.y);

        if (newPose)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
            // Swift shows nothing: the extra speed is the feedback (the user's call; afterimages read as noise)
            if (cat != null && cat.IsRolling && frame % 2 == 0 && speed > 0.5f)
                PaperFx.Puff(at - (Vector3)(rb.linearVelocity.normalized * bodySize * 0.45f), Mathf.Clamp(bodySize, 0.4f, 1.2f));
        }
        Tear(frame, newPose);
        Clock(at, bodySize, frame, newPose);
    }

    void ForgetBag()
    {
        bagWasOn = false;
        tearFrom = -1;
        if (tear != null) foreach (SpriteRenderer r in tear) if (r != null) r.enabled = false;
    }

    // ---- Shield: the paper bag tears ----
    void Tear(int frame, bool newPose)
    {
        bool on = bag != null && bag.enabled && bag.gameObject.activeInHierarchy;
        if (on)
        {
            Bounds b = bag.bounds;
            bagAt = b.center; bagSize = b.size; bagOrder = bag.sortingOrder; bagLayer = bag.sortingLayerID;
        }
        // it just ran out mid-round: tear it (a round ending also clears the shield; that is not an expiry)
        else if (bagWasOn && bag != null && bagPieces[0] != null && GameManager.Instance != null && GameManager.Instance.IsPlaying) tearFrom = frame;
        if (on) tearFrom = -1;   // a new shield while the old bag was still falling: the fresh bag wins
        bagWasOn = on;
        int step = tearFrom < 0 ? -1 : frame - tearFrom;
        bool show = step >= 0 && step < TearPose.Length;
        for (int i = 0; i < 3; i++) tear[i].enabled = show && bagPieces[i] != null;
        if (!show) { if (step >= TearPose.Length) tearFrom = -1; return; }
        if (step == 0 && newPose) PaperFx.Puff(bagAt, Mathf.Clamp(bagSize.x, 0.4f, 1.4f));
        float t = TearPose[step];
        float fall = t * t * 0.35f;                              // pieces drop as they fly
        float alpha = step < 3 ? 1f : step == 3 ? 0.65f : 0.3f;
        for (int i = 0; i < 3; i++)
        {
            SpriteRenderer r = tear[i];
            if (!r.enabled) continue;
            Vector2 off = Vector2.Lerp(TearFrom[i], TearTo[i], t) - new Vector2(0f, fall);
            r.transform.SetPositionAndRotation(bagAt + new Vector3(off.x * bagSize.x, off.y * bagSize.y, 0f), Quaternion.Euler(0f, 0f, TearSpin[i] * t));
            r.transform.localScale = Vector3.one * (bagSize.x * TearWidth[i] / bagPieces[i].bounds.size.x);
            r.sortingLayerID = bagLayer; r.sortingOrder = bagOrder;
            r.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    // ---- Time machine: white wash ----
    // Stepped, not faded: the wash jumps between fifths, like a lighting change on set.
    void Clock(Vector3 at, float bodySize, int frame, bool newPose)
    {
        float slow = Mathf.Clamp01(TimeMovesWithPlayer.Slowness);
        Camera cam = Camera.main;
        bool show = slow > 0.02f && cam != null && cam.orthographic;
        wash.enabled = show;
        if (!show) return;
        float step = Mathf.Ceil(slow * 5f) / 5f;
        float h = cam.orthographicSize * 2f + 1f, w = h * Mathf.Max(cam.aspect, 0.1f) + 1f;
        Vector3 c = cam.transform.position;
        wash.transform.SetPositionAndRotation(new Vector3(c.x, c.y, 0f), Quaternion.identity);
        wash.transform.localScale = new Vector3(w, h, 1f);
        wash.color = new Color(0.97f, 0.93f, 0.82f, step * WashMax);   // warm parchment, so white labels still read
    }
}
