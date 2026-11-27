using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the hook's claw as the keyframe's brass cut-out (Resources/Art/Fx/ClawPlate, ClawProngA/B): a riveted
// hinge plate at the pivot and two curved brass prongs. Hook still draws its claw arms as line renderers;
// the frost rules read those and their reach is the real grab, so they stay as they are, under the brass.
//   - Each prong is rooted at the real pivot (its rivet hole) and reaches the real arm's tip, so the claw
//     never looks bigger than what it grabs. The prong whose curve bulges to the arm's elbow side is used.
//   - Stop-motion "cracked" claw: the drawn opening steps through held angles on twos (closing 40, 28, 16,
//     10, 12 degrees; opening 12, 26, 43, 40: a small bounce and overshoot), following the real claw one
//     pose at a time instead of sliding with it. Gameplay geometry and timing are untouched.
//   - Colour still means something: the brass is tinted toward the hook's colour (red = aimed at you,
//     yellow = treasure); icy while the arms are frozen.
//   - The keyframe's thick two-ply cable (Resources/Art/Tiles/Cable): an art line drawn over the rope along
//     its exact points, wider than the rope (whose width the frost reads, so it stays as it is), tinted by
//     the rope's colour and alpha, icy while the rope is frozen.
// World map: the cats there are a different enemy (their hooks stop at rocks; in the levels hooks fly over
// them), so their hook is drawn as one too: a forged-iron grappling hook (Fx/Grapple) on an iron chain
// (Tiles/Chain) instead of the brass claw on two-ply twine. Iron bites into rock, which is the hint.
// Read-only: everything comes from the arms every frame. The pieces live under their own root (never
// parented to the hook) and carry FrostIgnore2D. Without the art: the old riveted tin hub.
[DefaultExecutionOrder(31000)]   // after Hook has placed its arms this frame
public class PaperClaws : MonoBehaviour
{
    static readonly Color Ice = new Color(0.72f, 0.9f, 1f);
    static readonly float[] Closing = { 40f, 28f, 16f, 10f, 12f };
    static readonly float[] Opening = { 12f, 26f, 43f, 40f };
    static PaperClaws instance;

    // root (rivet hole) and tip of each prong, in its 256 px canvas (0..1), measured from the art
    static readonly Vector2 RootA = new Vector2(0.438f, 0.098f), TipA = new Vector2(0.367f, 0.992f);
    static readonly Vector2 RootB = new Vector2(0.545f, 0.1f), TipB = new Vector2(0.613f, 0.992f);
    static readonly Vector2 PlateRivet = new Vector2(0.544f, 0.438f);

    class Claw
    {
        public Hook hook;
        public LineRenderer left, right;
        public SpriteRenderer plate, prongL, prongR;
        public LineRenderer rope, ropeOutline, cable;
        public Color ropeColor = Color.white, outlineColor = Color.clear;   // the colours Hook last gave its rope (we hold them at alpha 0)
        public LineRenderer[] arms;   // ClawLeft, its outline, ClawRight, its outline: hidden under the brass claw
        public Color[] armColors;
        public float drawn = 40f;     // the held drawn opening (degrees)
        public int lastFrame = -1;
        public int step;              // index into the running sequence
        public bool closing;
        public bool map;              // a world-map hook: grapple on a chain
    }

    readonly List<Claw> claws = new List<Claw>();
    Sprite plateArt, prongA, prongB, grappleArt;
    Material cableMaterial, chainMaterial;
    const float ChainAspect = 256f / 85f;   // Tiles/Chain: two whole link periods, seamless
    static readonly Vector2 GrappleEye = new Vector2(0.5f, 0.855f);   // the eye ring's hole in Fx/Grapple (prongs down)
    const float GrappleLength = 0.844f;     // eye to prong tips, in sprite widths
    readonly Vector3[] points = new Vector3[64];
    const float CableWidth = 0.19f;   // the keyframe twine, its two strands parting into open eyes on the (larger drawn) claw
    const float ClawScale = 1.3f;     // the brass claw is drawn larger than its arms, so the thick rope has a plate to tie onto
    const float CableAspect = 641f / 128f;   // Tiles/Cable: one full lazy twist (two open eyes), seamless
    float nextScan;
    bool drawPose = true;
    int artFrame = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Claws");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperClaws>();
        SceneManager.sceneLoaded -= OnSceneLoaded;   // domain reload is off: never stack
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (instance != null) instance.nextScan = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        Sprite plate = ArtLibrary.Get("Fx/ClawPlate"), a = ArtLibrary.Get("Fx/ClawProngA"), b = ArtLibrary.Get("Fx/ClawProngB");
        if (plate == null || a == null || b == null)
        {
            Sprite hub = ArtLibrary.Get("Fx/ClawHub");
            if (hub == null) { enabled = false; return; }
            plateArt = Remake(hub, new Vector2(0.5f, 0.5f));   // fallback: the old tin hub, no prongs
            return;
        }
        Sprite cable = ArtLibrary.Get("Tiles/Cable");
        Shader sprites = Shader.Find("Sprites/Default");
        if (cable != null && sprites != null) cableMaterial = new Material(sprites) { mainTexture = cable.texture, name = "Paper Cable" };
        Sprite chain = ArtLibrary.Get("Tiles/Chain"), grapple = ArtLibrary.Get("Fx/Grapple");
        if (chain != null && grapple != null && sprites != null)
        {
            chainMaterial = new Material(sprites) { mainTexture = chain.texture, name = "Paper Chain" };
            grappleArt = Remake(grapple, GrappleEye);
        }
        plateArt = Remake(plate, PlateRivet);
        prongA = Remake(a, RootA);
        prongB = Remake(b, RootB);
    }

    // 1 unit wide with its own pivot; scaled per hook below
    static Sprite Remake(Sprite art, Vector2 pivot)
    {
        Texture2D t = art.texture;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, t.width, 0, SpriteMeshType.FullRect);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (Hook hook in FindObjectsByType<Hook>(FindObjectsSortMode.None))
                if (!claws.Exists(c => c.hook == hook) && !Add(hook) && Time.timeSinceLevelLoad < 3f) nextScan = Time.unscaledTime;   // still being built: look again next frame
        }
        // stop-motion mode: the claw and twine are redrawn only on a new pose and held between (the hidden
        // gameplay strokes keep moving smoothly underneath); hiding them still runs every frame
        int frame = StopMotion.Frame;
        drawPose = !StopMotion.Enabled || frame != artFrame;
        artFrame = frame;
        for (int i = claws.Count - 1; i >= 0; i--)
        {
            Claw c = claws[i];
            if (c.hook == null || c.left == null || c.right == null)
            {
                if (c.plate != null) Destroy(c.plate.gameObject);
                if (c.prongL != null) Destroy(c.prongL.gameObject);
                if (c.prongR != null) Destroy(c.prongR.gameObject);
                if (c.cable != null) Destroy(c.cable.gameObject);
                claws.RemoveAt(i);
                continue;
            }
            Pose(c);
            PoseCable(c);
        }
    }

    bool Add(Hook hook)
    {
        Transform l = hook.transform.Find("ClawLeft"), r = hook.transform.Find("ClawRight");
        LineRenderer left = l != null ? l.GetComponent<LineRenderer>() : null;
        LineRenderer right = r != null ? r.GetComponent<LineRenderer>() : null;
        if (left == null || right == null) return false;   // not drawn yet; the next scan tries again
        Transform ropeT = hook.transform.Find("Rope");
        LineRenderer rope = ropeT != null ? ropeT.GetComponent<LineRenderer>() : null;
        Transform outlineT = hook.transform.Find("RopeOutline");
        bool map = grappleArt != null && OnMap();
        claws.Add(new Claw
        {
            hook = hook, left = left, right = right,
            plate = Piece("Claw Plate", plateArt),
            prongL = prongA != null ? Piece("Claw Prong", prongA) : null,
            prongR = prongB != null ? Piece("Claw Prong", prongB) : null,
            rope = rope,
            ropeOutline = outlineT != null ? outlineT.GetComponent<LineRenderer>() : null,
            arms = new[] { left, Line(hook, "ClawLeftOutline"), right, Line(hook, "ClawRightOutline") },
            armColors = new[] { Color.white, Color.clear, Color.white, Color.clear },
            cable = rope != null && cableMaterial != null ? Cable(map ? chainMaterial : cableMaterial) : null,
            map = map,
        });
        return true;
    }

    static bool OnMap() => MapHub.Active != null || SceneManager.GetActiveScene().name == "world_map";

    LineRenderer Cable(Material material)
    {
        var go = new GameObject("Paper Cable");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Tile;
        line.numCapVertices = 2;
        line.widthMultiplier = CableWidth;
        line.enabled = false;
        return line;
    }

    // The cable is a casual two-ply jute twine (Meshy-painted from the keyframe, a lazy twist with open eyes) laid over
    // the rope's own points every frame. The texture is pinned at the hook end and tiled at a fixed length,
    // never stretched: paying out shows more twists at the burrow while the ones by the claw ride with it.
    void PoseCable(Claw c)
    {
        if (c.cable == null || c.rope == null) return;
        bool shown = c.rope.enabled && c.rope.gameObject.activeInHierarchy && c.rope.positionCount >= 2 && !PaperMiners.UnderHatch(c.hook);
        c.cable.enabled = shown;
        if (PaperMiners.UnderHatch(c.hook))   // under the closed hatch: the gameplay rope stays hidden too
        {
            Hold(c.rope, ref c.ropeColor, false);
            Hold(c.ropeOutline, ref c.outlineColor, false);
        }
        if (!shown) return;
        // The twine is the rope's whole look, its open eyes included, so the gameplay rope (fill and outline)
        // is held at alpha 0 underneath: it stays enabled, so frost still finds it. Hook keeps its own colours
        // and repaints them (owner, hostile, cut fade); a non-zero alpha is a new colour, which we keep for the
        // twine. While frost patches lie on the rope it is drawn again, because they take its vertex alpha.
        bool frosted = Frosted(c.rope) || Frosted(c.ropeOutline);
        Hold(c.rope, ref c.ropeColor, frosted);
        Hold(c.ropeOutline, ref c.outlineColor, frosted);
        if (!drawPose) return;
        int n = Mathf.Min(c.rope.GetPositions(points), points.Length);
        Vector2 hook = c.hook.transform.position;
        if (((Vector2)points[n - 1] - hook).sqrMagnitude < ((Vector2)points[0] - hook).sqrMagnitude)
            System.Array.Reverse(points, 0, n);   // Tile mode counts from point 0: make that the hook's end
        c.cable.positionCount = n;
        c.cable.SetPositions(points);
        Color rope = c.ropeColor;
        bool frozen = FrostField2D.IsFrozen(c.rope);
        // jute dyed toward its owner's colour (the claw carries it fully); the iron chain only a little
        Color tint = frozen ? Ice : Color.Lerp(Color.white, new Color(rope.r, rope.g, rope.b), c.map ? 0.2f : 0.3f);
        tint.a = rope.a;
        c.cable.startColor = c.cable.endColor = tint;
        c.cable.textureScale = new Vector2(1f / (CableWidth * (c.map ? ChainAspect : CableAspect)), 1f);
        c.cable.sortingLayerID = c.rope.sortingLayerID;
        c.cable.sortingOrder = c.rope.sortingOrder + 1;   // over the gameplay rope, under the claw
    }

    static LineRenderer Line(Hook hook, string child)
    {
        Transform t = hook.transform.Find(child);
        return t != null ? t.GetComponent<LineRenderer>() : null;
    }

    static void Hold(LineRenderer line, ref Color kept, bool show)
    {
        if (line == null) return;
        Color now = line.startColor;
        if (now.a > 0f && (!show || now != kept)) kept = now;   // Hook painted it since our last frame
        Color want = kept;
        if (!show) want.a = 0f;
        if (line.startColor != want) line.startColor = line.endColor = want;
    }

    static bool Frosted(LineRenderer line)
    {
        if (line == null) return false;
        FrostArtOverlay2D patches = line.GetComponentInChildren<FrostArtOverlay2D>();
        LineRenderer drawn = patches != null ? patches.GetComponent<LineRenderer>() : null;
        return drawn != null && drawn.enabled;
    }

    SpriteRenderer Piece(string pieceName, Sprite art)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        sr.enabled = false;
        return sr;
    }

    void Pose(Claw c)
    {
        bool shown = c.left.enabled && c.left.gameObject.activeInHierarchy && c.left.positionCount >= 3
                     && !PaperMiners.UnderHatch(c.hook);   // still down the shaft under the closed level-start hatch
        c.plate.enabled = shown;
        if (c.prongL != null) c.prongL.enabled = shown && !c.map;   // the map's grapple is one piece
        if (c.prongR != null) c.prongR.enabled = shown && !c.map;
        if (PaperMiners.UnderHatch(c.hook))   // under the closed hatch: the gameplay arms stay hidden too
            for (int i = 0; i < c.arms.Length; i++) Hold(c.arms[i], ref c.armColors[i], false);
        if (!shown) return;

        // The brass claw is drawn larger than the real arms, so the arms (and their outlines) would peek out
        // beside it as a second claw: hold them at alpha 0 like the rope (still enabled for frost), and draw
        // them again while frost patches lie on them or when there is no prong art to stand in.
        bool armFrost = c.prongL == null;
        foreach (LineRenderer arm in c.arms) armFrost |= Frosted(arm);
        for (int i = 0; i < c.arms.Length; i++) Hold(c.arms[i], ref c.armColors[i], armFrost);
        if (!drawPose) return;

        bool frozen = FrostField2D.IsFrozen(c.left) || FrostField2D.IsFrozen(c.right);
        Color line = c.armColors[0];
        Color tint = frozen ? Ice : Color.Lerp(Color.white, new Color(line.r, line.g, line.b), 0.45f);
        tint.a = line.a;   // a cut rope's half fades out with its arms

        Vector2 pivot = c.left.GetPosition(0);
        Vector2 tipL = c.left.GetPosition(2), tipR = c.right.GetPosition(2);
        Vector2 dir = ((tipL + tipR) * 0.5f - pivot);
        if (dir.sqrMagnitude < 1e-6f) dir = (Vector2)c.left.GetPosition(1) - pivot;
        dir.Normalize();
        float reach = Mathf.Max(0.05f, c.hook.clawLength);

        if (c.map)   // the grapple: one forged piece, eye ring on the chain's end, prongs along the shot
        {
            if (c.prongL != null) c.prongL.enabled = false;
            if (c.prongR != null) c.prongR.enabled = false;
            c.plate.sprite = grappleArt;
            float along = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;   // the art points down
            c.plate.transform.SetPositionAndRotation(pivot, Quaternion.Euler(0f, 0f, along));
            c.plate.transform.localScale = Vector3.one * (reach * 1.04f * ClawScale / GrappleLength);
            Color iron = frozen ? Ice : Color.Lerp(Color.white, new Color(line.r, line.g, line.b), 0.25f);
            iron.a = line.a;
            c.plate.color = iron;
            Order(c.plate, c.left, 3);
            return;
        }

        // plate: rivet on the pivot, long side pointing back up the rope
        float back = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg - 90f;
        c.plate.transform.SetPositionAndRotation(pivot, Quaternion.Euler(0f, 0f, back));
        c.plate.transform.localScale = Vector3.one * (prongA != null ? reach * 0.62f * ClawScale : Mathf.Max(0.12f, c.hook.clawWidth * 2.6f));
        c.plate.color = tint;
        Order(c.plate, c.left, 3);
        if (c.prongL == null) return;

        // the real opening (degrees between the centre line and an arm's elbow), followed in held poses
        Vector2 elbow = c.left.GetPosition(1);
        float actual = Vector2.Angle(dir, elbow - pivot);
        Step(c, actual);

        // each tip's real direction, re-angled to the drawn opening
        float actualTip = Mathf.Max(1f, Vector2.Angle(dir, tipL - pivot));
        float tipAngle = actualTip * c.drawn / Mathf.Max(1f, actual);
        Prong(c.prongL, c.left, pivot, dir, tipAngle, tint, reach);
        Prong(c.prongR, c.right, pivot, dir, tipAngle, tint, reach);
    }

    // Advance the drawn opening one held pose per twos frame toward the real one.
    static void Step(Claw c, float actual)
    {
        int frame = StopMotion.Frame;
        if (frame == c.lastFrame) return;
        c.lastFrame = frame;
        bool wantClosed = actual < 26f;
        if (wantClosed != c.closing) { c.closing = wantClosed; c.step = 0; }
        float[] seq = c.closing ? Closing : Opening;
        if (c.step < seq.Length) c.drawn = seq[c.step++];
        else c.drawn = seq[seq.Length - 1];
    }

    void Prong(SpriteRenderer prong, LineRenderer arm, Vector2 pivot, Vector2 dir, float tipAngle, Color tint, float reach)
    {
        Vector2 elbow = arm.GetPosition(1), tip = arm.GetPosition(2);
        // which side of the centre line this arm opens to
        float side = Mathf.Sign(dir.x * (elbow - pivot).y - dir.y * (elbow - pivot).x);
        if (side == 0f) side = 1f;
        Vector2 toTip = Quaternion.Euler(0f, 0f, side * tipAngle) * dir;
        float length = Mathf.Max(0.05f, Vector2.Distance(pivot, tip)) * 1.04f * ClawScale;

        // the prong's curve must bulge to the elbow's side: A bulges to its right, B to its left
        bool useA = side < 0f;
        Sprite art = useA ? prongA : prongB;
        Vector2 artVec = (useA ? TipA - RootA : TipB - RootB);   // canvas units (1 = the sprite's width)
        prong.sprite = art;
        float artAngle = Mathf.Atan2(artVec.y, artVec.x) * Mathf.Rad2Deg;
        float worldAngle = Mathf.Atan2(toTip.y, toTip.x) * Mathf.Rad2Deg;
        prong.transform.SetPositionAndRotation(pivot, Quaternion.Euler(0f, 0f, worldAngle - artAngle));
        prong.transform.localScale = Vector3.one * (length / artVec.magnitude);
        prong.color = tint;
        Order(prong, arm, 2);
    }

    static void Order(SpriteRenderer piece, LineRenderer line, int above)
    {
        piece.sortingLayerID = line.sortingLayerID;
        piece.sortingOrder = Mathf.Min(short.MaxValue, line.sortingOrder + above);
    }
}
