using System.Collections.Generic;
using UnityEngine;

// Art: the Needle Meowmeow, a lean felt fencer cat with a red bandit mask whose rapier is a giant
// sewing needle on red thread. It is the sprint-stab enemy. Its gameplay is NOT written yet, so this
// is the look only, ready for that script to drive (Meshy-painted, Resources/Art/Enemies/Needle_*;
// design in docs/roam-enemies.md).
//
// How a gameplay script drives it (all art, no physics; it never moves the root):
//   art.Aim(direction)            which way it will sprint (faces it, tilts the dash toward it)
//   art.SetPose(Pose.Windup)      crouch + a red stitched line marching along the sprint path
//   art.SetPose(Pose.Dash)        flying lunge; the needle sews a red thread along the path behind it
//   art.SetPose(Pose.Stab)        the full lunge at the end of the sprint (a hit lands here)
//   art.SetPose(Pose.Stuck)       a miss: needle stuck in the floor, tugging it out (its opening)
//   art.SetPose(Pose.Hit)         knocked back, needle flying loose
//   art.SetPose(Pose.Prowl)       stalking; it bobs on the stop-motion beat while the root moves
// The sewn thread stays on the floor after a dash, then fades.
// Preview Cycle (Inspector) plays the whole attack in a loop by itself, for reviewing the art.
//
// Parts (children, built by EnsureParts when missing, so the prefab and a bare component both work):
//   Body       SpriteRenderer with the pose pictures; the only part that tilts and flips
//   Thread     world-space LineRenderer (twine texture, red) for the sewn trail
//   Telegraph  holds the stitch sprites of the wind-up line (made at runtime)
[DisallowMultipleComponent]
public class NeedleMeowmeowArt : MonoBehaviour
{
    public enum Pose { Prowl, Windup, Dash, Stab, Stuck, Hit }

    [Header("Driven by gameplay")]
    public Pose pose = Pose.Prowl;
    [Tooltip("The sprint direction (world). Only its sign in x flips the cat; dash and stab also tilt toward it")]
    public Vector2 aim = Vector2.right;
    [Tooltip("World units the wind-up's stitched line reaches: set it to the sprint distance")]
    public float telegraphLength = 4f;

    [Header("Look")]
    public Sprite prowl, windup, dash, stab, stuck, hit;
    public Color threadColor = new Color(0.85f, 0.12f, 0.12f);
    [Tooltip("Most the dash and stab tilt from level toward the aim, degrees")]
    public float maxTilt = 35f;
    [Tooltip("Seconds the sewn thread stays after a dash before it is gone")]
    public float threadFade = 1.2f;

    [Header("Review")]
    [Tooltip("Plays prowl, wind-up, dash, stab, stuck, hit in a loop by itself, sprinting back and forth")]
    public bool previewCycle;

    SpriteRenderer body;
    LineRenderer thread;
    Transform telegraph;
    readonly List<SpriteRenderer> stitches = new List<SpriteRenderer>();
    readonly List<Vector3> sewn = new List<Vector3>();
    Sprite stitchArt;
    float fadeFrom = -1f;
    int lastFrame = int.MinValue;
    Pose shownPose = (Pose)(-1);

    // 576 px poses at 256 px a unit, drawn at 0.75: the prowl is about 1.45 units nose to needle tip,
    // a little under the brawler's 1.5, as a lean cat should be
    public const float BodyScale = 0.75f;
    const int MaxStitches = 30;
    const float StitchSpacing = 0.35f;   // like the hooks' aim stitch (TelegraphLine)
    static readonly Vector2 StitchSize = new Vector2(0.21f, 0.09f);

    public void SetPose(Pose next) { pose = next; }
    public void Aim(Vector2 direction) { if (direction.sqrMagnitude > 0.0001f) aim = direction.normalized; }

    void Awake()
    {
        EnsureParts();
        LoadMissingSprites();
        if (thread.sharedMaterial == null) thread.material = new Material(Shader.Find("Sprites/Default"));
        ArtLibrary.Twine(thread, 9f, true);
        stitchArt = ArtLibrary.Get("Fx/Stitch");
    }

    // Builds the children if they are missing (also used by the editor's prefab builder)
    public void EnsureParts()
    {
        Transform b = transform.Find("Body");
        if (b == null) { b = new GameObject("Body").transform; b.SetParent(transform, false); b.localScale = Vector3.one * BodyScale; }
        body = b.GetComponent<SpriteRenderer>();
        if (body == null) { body = b.gameObject.AddComponent<SpriteRenderer>(); body.sortingOrder = 2; }

        Transform t = transform.Find("Thread");
        if (t == null) { t = new GameObject("Thread").transform; t.SetParent(transform, false); }
        thread = t.GetComponent<LineRenderer>();
        if (thread == null)
        {
            thread = t.gameObject.AddComponent<LineRenderer>();
            thread.useWorldSpace = true;
            thread.widthMultiplier = 0.07f;
            thread.positionCount = 0;
            thread.numCapVertices = 2;
            thread.sortingOrder = 1;   // under the cat, over the floor
            thread.alignment = LineAlignment.TransformZ;
        }

        telegraph = transform.Find("Telegraph");
        if (telegraph == null) { telegraph = new GameObject("Telegraph").transform; telegraph.SetParent(transform, false); }
    }

    public void LoadMissingSprites()
    {
        if (prowl == null) prowl = ArtLibrary.Get("Enemies/Needle_Prowl");
        if (windup == null) windup = ArtLibrary.Get("Enemies/Needle_Windup");
        if (dash == null) dash = ArtLibrary.Get("Enemies/Needle_Dash");
        if (stab == null) stab = ArtLibrary.Get("Enemies/Needle_Stab");
        if (stuck == null) stuck = ArtLibrary.Get("Enemies/Needle_Stuck");
        if (hit == null) hit = ArtLibrary.Get("Enemies/Needle_Hit");
        if (body != null && body.sprite == null) body.sprite = prowl;
    }

    void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;   // paused: a still photo
        if (previewCycle) Preview();

        // Attack poses change at once (a hit must read on its frame); the prowl bob and the marching
        // stitches wait for the next picture of the shared beat
        bool posed = pose != shownPose;
        int frame = StopMotion.Frame;
        bool beat = frame != lastFrame;
        lastFrame = frame;
        if (posed) EnterPose();
        if (posed || beat) Draw(frame);
        Sew();
    }

    void EnterPose()
    {
        bool wasSewing = shownPose == Pose.Dash || shownPose == Pose.Stab;
        bool sewing = pose == Pose.Dash || pose == Pose.Stab;
        if (sewing && !wasSewing) { sewn.Clear(); fadeFrom = -1f; }
        if (!sewing && wasSewing) fadeFrom = Time.time;
        shownPose = pose;
        body.sprite = SpriteFor(pose);
        ShowTelegraph(pose == Pose.Windup);
    }

    Sprite SpriteFor(Pose p)
    {
        switch (p)
        {
            case Pose.Windup: return windup;
            case Pose.Dash: return dash;
            case Pose.Stab: return stab;
            case Pose.Stuck: return stuck;
            case Pose.Hit: return hit;
            default: return prowl;
        }
    }

    void Draw(int frame)
    {
        bool left = aim.x < -0.05f;
        body.flipX = left;
        float angle = 0f;
        Vector3 offset = Vector3.zero;
        if (pose == Pose.Dash || pose == Pose.Stab)
        {
            // tilt toward the aim, measured from level on the side it faces
            float a = Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(a, -maxTilt, maxTilt) * (left ? -1f : 1f);
        }
        else if (pose == Pose.Prowl)
        {
            // a stalking step every other picture, leaning with the shared rock
            offset.y = (frame & 1) == 0 ? 0.04f : 0f;
            angle = -StopMotion.Rock * StopMotion.RockDegrees;
        }
        else if (pose == Pose.Hit) offset.x = left ? 0.08f : -0.08f;   // thrown back from where it faces
        body.transform.localPosition = offset;
        body.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (pose == Pose.Windup) MarchTelegraph(frame);
    }

    // The wind-up's red running stitch along the sprint path, marching outward a stitch every 3 poses
    void ShowTelegraph(bool on)
    {
        if (!on) { foreach (SpriteRenderer s in stitches) if (s != null) s.enabled = false; return; }
        if (stitchArt == null) return;
        while (stitches.Count < MaxStitches)
        {
            var go = new GameObject("Stitch");
            go.transform.SetParent(telegraph, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = stitchArt;
            sr.sortingOrder = 1;
            stitches.Add(sr);
        }
    }

    void MarchTelegraph(int frame)
    {
        if (stitchArt == null || stitches.Count == 0) return;
        Vector2 dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
        float shift = StitchSpacing * ((frame / 3) % 2) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 spriteSize = stitchArt.bounds.size;
        Vector3 scale = new Vector3(StitchSize.x / Mathf.Max(0.001f, spriteSize.x), StitchSize.y / Mathf.Max(0.001f, spriteSize.y), 1f);
        Vector3 lossy = transform.lossyScale;
        scale.x /= Mathf.Max(0.001f, Mathf.Abs(lossy.x));
        scale.y /= Mathf.Max(0.001f, Mathf.Abs(lossy.y));
        for (int i = 0; i < stitches.Count; i++)
        {
            float d = 0.45f + shift + i * StitchSpacing;
            SpriteRenderer s = stitches[i];
            bool on = d <= telegraphLength;
            s.enabled = on;
            if (!on) continue;
            s.color = threadColor;
            s.transform.position = (Vector2)transform.position + dir * d;
            s.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            s.transform.localScale = scale;
        }
    }

    // While dashing and stabbing, the needle sews: a red thread laid on the floor along the path
    void Sew()
    {
        bool sewing = pose == Pose.Dash || pose == Pose.Stab;
        if (sewing)
        {
            Vector3 at = transform.position;
            if (sewn.Count == 0 || (sewn[sewn.Count - 1] - at).sqrMagnitude > 0.01f)
            {
                sewn.Add(at);
                if (sewn.Count > 64) sewn.RemoveAt(0);
            }
        }
        float alpha = 1f;
        if (!sewing && fadeFrom >= 0f)
        {
            // fades in held steps on the pose clock, like everything else
            float t = StopMotion.Snap(Time.time - fadeFrom, StopMotion.Pose) / Mathf.Max(0.01f, threadFade);
            alpha = 1f - Mathf.Clamp01(t);
            if (alpha <= 0f) { sewn.Clear(); fadeFrom = -1f; }
        }
        if (thread.positionCount != sewn.Count) thread.positionCount = sewn.Count;
        for (int i = 0; i < sewn.Count; i++) thread.SetPosition(i, sewn[i]);
        Color c = threadColor;
        c.a = alpha;
        thread.startColor = thread.endColor = c;
    }

    // --- Review: the whole attack by itself -------------------------------------------------------
    float previewAt;
    int previewStep;
    bool previewStarted;
    static readonly Pose[] PreviewPoses = { Pose.Prowl, Pose.Windup, Pose.Dash, Pose.Stab, Pose.Stuck, Pose.Prowl, Pose.Windup, Pose.Dash, Pose.Stab, Pose.Hit };
    static readonly float[] PreviewHolds = { 1.2f, 0.9f, 0.35f, 0.35f, 0.9f, 1.0f, 0.9f, 0.35f, 0.35f, 0.9f };

    void Preview()
    {
        if (!previewStarted) { previewStarted = true; previewAt = Time.time; previewStep = 0; }
        if (Time.time - previewAt >= PreviewHolds[previewStep])
        {
            previewAt = Time.time;
            previewStep = (previewStep + 1) % PreviewPoses.Length;
            if (previewStep == 0 || previewStep == 5) aim = new Vector2(aim.x < 0f ? 1f : -1f, 0f);   // turn round: sprints back and forth
        }
        pose = PreviewPoses[previewStep];
        // sprint along the aim during the dash (preview only; gameplay moves the real enemy)
        if (pose == Pose.Dash || pose == Pose.Stab)
            transform.position += (Vector3)(aim * (telegraphLength / 0.7f) * Time.deltaTime);
    }
}
