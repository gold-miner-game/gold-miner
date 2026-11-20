using System.Collections.Generic;
using UnityEngine;
using FrostEffect = FrostBody2D.FrostBody2D;

// Shared frost patches and gameplay queries. Each touched patch lasts five seconds.
[DefaultExecutionOrder(1000)]
public sealed class FrostField2D : MonoBehaviour
{
    public const float FrostDuration = 5f;
    static FrostField2D instance;
    readonly List<Cone> cones = new List<Cone>();
    readonly Dictionary<Renderer, Surface> surfaces = new Dictionary<Renderer, Surface>();
    readonly List<Renderer> remove = new List<Renderer>();
    readonly Dictionary<MinerAI, float> frostedMiners = new Dictionary<MinerAI, float>();
    readonly List<SpriteRenderer> minerArtwork = new List<SpriteRenderer>();
    static readonly List<Cone> NoCones = new List<Cone>();
    Renderer[] candidates;
    FrostEffect template;
    int coneFrame = -1;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    public static bool IsFrozen(Renderer renderer)
        => instance != null && renderer != null && instance.surfaces.TryGetValue(renderer, out Surface surface)
            && surface.HasFrost(Time.time);

    public static bool IsFrozen(Transform root)
    {
        if (instance == null || root == null) return false;
        foreach (var pair in instance.surfaces)
            if (pair.Key != null && pair.Key.transform.IsChildOf(root) && pair.Value.HasFrost(Time.time)) return true;
        return false;
    }

    public static bool BreakIce(Transform root)
    {
        if (instance == null || root == null) return false;
        bool broken = false;
        foreach (var pair in instance.surfaces)
            if (pair.Key != null && pair.Key.transform.IsChildOf(root))
            { broken |= pair.Value.HasFrost(Time.time); pair.Value.Clear(); }
        return broken;
    }

    public static void BreakIce(Renderer renderer)
    {
        if (instance != null && renderer != null && instance.surfaces.TryGetValue(renderer, out Surface surface)) surface.Clear();
    }

    public static void TrackRope(LineRenderer line)
    {
        if (instance != null && line != null && instance.surfaces.TryGetValue(line, out Surface surface)) surface.ropeCoordinates = true;
    }

    public static float LimitReelStep(LineRenderer line, float requested)
    {
        if (instance == null || line == null || !instance.surfaces.TryGetValue(line, out Surface surface)) return requested;
        surface.ropeCoordinates = true;
        return surface.LimitReelStep(requested, Time.time);
    }

    public static void Expose(Transform caster, Vector2 origin, Vector2 forward, float length, float halfArc, bool roundedFront = false)
    {
        if (!Application.isPlaying || !PlayerCharacter.IsPlaying || forward.sqrMagnitude < .000001f) return;
        if (instance == null) instance = new GameObject("Frost visual field").AddComponent<FrostField2D>();
        if (instance.coneFrame != Time.frameCount) { instance.cones.Clear(); instance.coneFrame = Time.frameCount; }
        instance.cones.Add(new Cone(caster, origin, forward, length, halfArc, roundedFront));
    }

    // Length/arc are authored in the effect's local space. Use the same matrix
    // as the rendered mesh, including map scale, mirroring and scaled parents.
    public static void ExposeTransformed(Transform caster, Transform effect, float length, float halfArc, bool roundedFront = false)
    {
        if (!Application.isPlaying || !PlayerCharacter.IsPlaying || effect == null
            || Mathf.Abs(effect.localToWorldMatrix.determinant) < 1e-12f) return;
        if (instance == null) instance = new GameObject("Frost visual field").AddComponent<FrostField2D>();
        if (instance.coneFrame != Time.frameCount) { instance.cones.Clear(); instance.coneFrame = Time.frameCount; }
        instance.cones.Add(new Cone(caster, effect.localToWorldMatrix, length, halfArc, roundedFront));
    }

    void LateUpdate()
    {
        if (coneFrame != Time.frameCount) cones.Clear();
        float now = Time.time;
        if (cones.Count > 0)
        {
            if (template == null) template = Resources.Load<FrostEffect>("Effects/FrostBody2D");
            if (template == null) return;
            if (candidates == null || now >= nextScan)
            {
                // Normal object searches omit HideAndDontSave components. Hook strokes use
                // that flag, so discover ordinary scene roots and include all their artwork.
                var found = new List<Renderer>();
                foreach (Transform root in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (root.parent == null) found.AddRange(root.GetComponentsInChildren<Renderer>(false));
                candidates = found.ToArray();
                nextScan = now + .1f;
            }
            foreach (Renderer candidate in candidates)
            {
                if (!Eligible(candidate) || surfaces.ContainsKey(candidate)) continue;
                foreach (Cone cone in cones)
                    if (cone.MayTouch(candidate))
                    {
                        surfaces.Add(candidate, new Surface(candidate, template)); break;
                    }
            }
        }
        remove.Clear();
        frostedMiners.Clear();
        foreach (var pair in surfaces)
        {
            if (pair.Key == null || Excluded(pair.Key) || !pair.Value.Tick(cones, now))
            {
                pair.Value.Dispose(); remove.Add(pair.Key);
                continue;
            }
            MinerAI miner = BodyMiner(pair.Key);
            if (miner != null && pair.Value.HasFrost(now))
            {
                frostedMiners.TryGetValue(miner, out float until);
                frostedMiners[miner] = Mathf.Max(until, pair.Value.FrostUntil);
            }
        }
        foreach (Renderer renderer in remove) surfaces.Remove(renderer);
        foreach (var pair in frostedMiners)
        {
            // Any actual frosted body patch covers the entire miner. Use its
            // existing expiry so propagation cannot keep refreshing itself.
            minerArtwork.Clear(); pair.Key.GetComponentsInChildren(false, minerArtwork);
            foreach (SpriteRenderer part in minerArtwork)
            {
                if (!Eligible(part) || BodyMiner(part) != pair.Key) continue;
                if (!surfaces.TryGetValue(part, out Surface surface))
                {
                    surface = new Surface(part, template); surfaces.Add(part, surface);
                }
                surface.CoverFully(pair.Value, now);
            }
        }
    }

    static MinerAI BodyMiner(Renderer renderer)
    {
        if (!(renderer is SpriteRenderer) || renderer.GetComponentInParent<Hook>(true) != null
            || renderer.GetComponentInParent<TelegraphLine>(true) != null) return null;
        return renderer.GetComponentInParent<MinerAI>(true);
    }

    static bool Excluded(Renderer renderer)
        => renderer.GetComponentInParent<FrostIgnore2D>(true) != null || renderer.GetComponentInParent<Canvas>(true) != null;

    static bool Eligible(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) return false;
        if (renderer.GetComponent<FrostArtOverlay2D>() != null) return false;
        if (Excluded(renderer)) return false;
        return renderer is LineRenderer || renderer is SpriteRenderer sprite
            && sprite.sprite != null && sprite.drawMode == SpriteDrawMode.Simple;
    }

    void OnDestroy()
    {
        foreach (Surface surface in surfaces.Values) surface.Dispose();
        surfaces.Clear();
        if (instance == this) instance = null;
    }

    readonly struct Cone
    {
        readonly Transform caster;
        readonly Vector2 origin, forward, right;
        readonly float length, slope;
        readonly bool roundedFront;
        readonly Bounds bounds;
        public Cone(Transform caster, Vector2 origin, Vector2 forward, float length, float halfArc)
            : this(caster, origin, forward, length, halfArc, false) { }
        public Cone(Transform caster, Vector2 origin, Vector2 forward, float length, float halfArc, bool roundedFront)
        {
            this.roundedFront = roundedFront;
            this.caster = caster; this.origin = origin; this.forward = forward.normalized;
            right = new Vector2(this.forward.y, -this.forward.x);
            this.length = Mathf.Max(.01f, length); slope = Mathf.Tan(Mathf.Clamp(halfArc, 1f, 89f) * Mathf.Deg2Rad);
            Vector2 end = origin + this.forward * this.length;
            Vector2 side = new Vector2(-this.forward.y, this.forward.x) * this.length * slope;
            bounds = new Bounds(origin, Vector3.zero);
            bounds.Encapsulate((Vector3)(end + side)); bounds.Encapsulate((Vector3)(end - side));
        }
        public Cone(Transform caster, Matrix4x4 localToWorld, float length, float halfArc, bool roundedFront)
        {
            this.caster = caster; this.roundedFront = roundedFront;
            this.length = Mathf.Max(.01f, length);
            slope = Mathf.Tan(Mathf.Clamp(halfArc, 1f, 89f) * Mathf.Deg2Rad);
            origin = localToWorld.MultiplyPoint3x4(Vector3.zero);
            Matrix4x4 inverse = localToWorld.inverse;
            // Covectors project world-space points/steps back onto the mesh's
            // local +Y (forward) and +X (sideways), even under nonuniform scale.
            forward = new Vector2(inverse.m10, inverse.m11);
            right = new Vector2(inverse.m00, inverse.m01);
            bounds = new Bounds(origin, Vector3.zero);
            bounds.Encapsulate(localToWorld.MultiplyPoint3x4(new Vector3(this.length * slope, this.length, 0f)));
            bounds.Encapsulate(localToWorld.MultiplyPoint3x4(new Vector3(-this.length * slope, this.length, 0f)));
        }
        public bool MayTouch(Renderer renderer)
        {
            if (caster != null && renderer.transform.IsChildOf(caster)) return false;
            Bounds b = renderer.bounds;
            // A hook updates its world-space strokes just before this pass; native renderer
            // bounds can lag until culling. Read the current points for the broad phase.
            if (renderer is LineRenderer line && line.positionCount > 0)
            {
                Vector3 Point(int i) => line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i));
                b = new Bounds(Point(0), Vector3.zero);
                for (int i = 1; i < line.positionCount; i++) b.Encapsulate(Point(i));
                b.Expand(Mathf.Max(.01f, line.widthMultiplier) * 2f);
            }
            return b.max.x >= bounds.min.x && b.min.x <= bounds.max.x
                && b.max.y >= bounds.min.y && b.min.y <= bounds.max.y;
        }
        public bool Contains(Vector2 point)
        {
            Vector2 delta = point - origin;
            float along = Vector2.Dot(delta, forward);
            float across = Mathf.Abs(Vector2.Dot(delta, right));
            return along >= 0f && along <= length && across <= along * slope
                && (!roundedFront || along * along + across * across / (slope * slope) <= length * length);
        }

        // Intersect each mask row with the cone's four half-planes. Requiring the
        // least-inside corner of a cell keeps the same fully-contained coverage
        // without transforming and testing four corners for every sprite pixel.
        public bool Stamp(float[] expires, int width, int height, Vector2 start, Vector2 dx, Vector2 dy, float until)
        {
            Vector2 side = right;
            Vector2 delta = start - origin;
            Vector2 frontStart = new Vector2(Vector2.Dot(delta, forward), Vector2.Dot(delta, side) / slope);
            Vector2 frontStep = new Vector2(Vector2.Dot(dx, forward), Vector2.Dot(dx, side) / slope);
            Vector2 frontUp = new Vector2(Vector2.Dot(dy, forward), Vector2.Dot(dy, side) / slope);
            bool touched = false;
            for (int y = 0; y < height; y++)
            {
                int first = 0, last = width - 1;
                Vector2 row = delta + dy * y;
                if (!ClipRow(forward, 0f, row, dx, dy, ref first, ref last)
                    || !ClipRow(-forward, length, row, dx, dy, ref first, ref last)
                    || !ClipRow(forward * slope + side, 0f, row, dx, dy, ref first, ref last)
                    || !ClipRow(forward * slope - side, 0f, row, dx, dy, ref first, ref last)) continue;
                if (roundedFront)
                {
                    Vector2 at = frontStart + frontUp * y;
                    if (!ClipFront(at, frontStep, length, ref first, ref last)
                        || !ClipFront(at + frontStep, frontStep, length, ref first, ref last)
                        || !ClipFront(at + frontUp, frontStep, length, ref first, ref last)
                        || !ClipFront(at + frontStep + frontUp, frontStep, length, ref first, ref last)) continue;
                }
                int offset = y * width;
                for (int x = first; x <= last; x++) expires[offset + x] = until;
                touched = true;
            }
            return touched;
        }

        static bool ClipFront(Vector2 at, Vector2 step, float radius, ref int first, ref int last)
        {
            float a = step.sqrMagnitude, b = Vector2.Dot(at, step);
            float c = at.sqrMagnitude - radius * radius;
            if (a < 1e-15f) return c <= 0f;
            float discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            first = Mathf.Max(first, Mathf.CeilToInt((-b - root) / a + .00001f));
            last = Mathf.Min(last, Mathf.FloorToInt((-b + root) / a - .00001f));
            return first <= last;
        }

        static bool ClipRow(Vector2 normal, float offset, Vector2 row, Vector2 dx, Vector2 dy, ref int first, ref int last)
        {
            float step = Vector2.Dot(normal, dx);
            float value = Vector2.Dot(normal, row) + offset
                + Mathf.Min(0f, step) + Mathf.Min(0f, Vector2.Dot(normal, dy));
            // A tiny inward bias avoids float rounding spilling over cone edges.
            value -= .000001f;
            if (Mathf.Abs(step) < 1e-10f) return value >= 0f;
            if (step > 0f) first = Mathf.Max(first, Mathf.CeilToInt(-value / step));
            else last = Mathf.Min(last, Mathf.FloorToInt(-value / step));
            return first <= last;
        }
    }

    sealed class Surface
    {
        readonly Renderer source;
        readonly SpriteRenderer sprite;
        readonly LineRenderer line;
        readonly FrostEffect template;
        readonly int width, height;
        readonly float[] expires;
        readonly Color32[] pixels;
        readonly List<Cone> touching = new List<Cone>();
        Texture2D coverage;
        FrostEffect spriteEffect;
        LineRenderer lineEffect;
        MaterialPropertyBlock properties;
        Vector3[] positions;
        float[] distances;
        AnimationCurve widthCurve;
        float widthMultiplier;
        float lineLength, activeUntil, started, fullUntil;
        public bool ropeCoordinates;
        float[] shiftedExpires;
        Vector4 coverageRect;
        Vector2 bottomLeft, stepX, stepY;

        public bool HasFrost(float now) => activeUntil > now;
        public float FrostUntil => activeUntil;
        public void CoverFully(float until, float now)
        {
            if (until <= fullUntil) return;
            fullUntil = until; activeUntil = Mathf.Max(activeUntil, until);
            Tick(NoCones, now);
        }
        public void Clear()
        {
            System.Array.Clear(expires, 0, expires.Length);
            activeUntil = fullUntil = 0f; Hide();
        }
        public float LimitReelStep(float requested, float now)
        {
            if (!HasFrost(now)) return requested;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (expires[y * width + x] > now) return Mathf.Min(requested, x * lineLength / width);
            return requested;
        }

        public Surface(Renderer source, FrostEffect template)
        {
            this.source = source; this.template = template;
            sprite = source as SpriteRenderer; line = source as LineRenderer;
            if (sprite != null)
            {
                Vector2 size = sprite.sprite.bounds.size;
                float longest = Mathf.Max(size.x, size.y, .001f);
                width = Mathf.Clamp(Mathf.CeilToInt(96 * size.x / longest), 8, 96);
                height = Mathf.Clamp(Mathf.CeilToInt(96 * size.y / longest), 8, 96);
            }
            else { width = 256; height = 12; }
            expires = new float[width * height]; pixels = new Color32[expires.Length];
        }

        public bool Tick(List<Cone> cones, float now)
        {
            touching.Clear();
            if (Eligible(source)) foreach (Cone cone in cones) if (cone.MayTouch(source)) touching.Add(cone);
            if (touching.Count == 0 && now >= activeUntil) return false;
            if (!PrepareGeometry()) { Hide(); return now < activeUntil; }
            if (sprite != null)
            {
                foreach (Cone cone in touching)
                    if (cone.Stamp(expires, width, height, bottomLeft, stepX, stepY, now + FrostDuration))
                        activeUntil = now + FrostDuration;
            }
            else for (int x = 0; x < width; x++)
            {
                Column(x, out Vector2 left, out Vector2 right, out Vector2 leftUp, out Vector2 rightUp);
                for (int y = 0; y < height; y++)
                {
                    int index = y * width + x;
                    foreach (Cone cone in touching)
                        // All four corners must be inside: the frost cannot spill over a cone edge.
                        if (cone.Contains(left) && cone.Contains(right)
                            && cone.Contains(left + leftUp) && cone.Contains(right + rightUp))
                        {
                            expires[index] = now + FrostDuration; activeUntil = expires[index]; break;
                        }
                    left += leftUp; right += rightUp;
                }
            }
            bool any = false;
            for (int index = 0; index < expires.Length; index++)
            {
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((Mathf.Max(expires[index], fullUntil) - now) / FrostDuration) * 255f);
                any |= alpha > 0;
                pixels[index] = new Color32(alpha, alpha, alpha, 255);
            }
            if (!any) return false;
            EnsureEffect(now);
            coverage.SetPixels32(pixels); coverage.Apply(false, false);
            if (spriteEffect != null)
            {
                spriteEffect.gameObject.SetActive(true);
                spriteEffect.coverageRect = coverageRect; spriteEffect.Refresh();
            }
            else UpdateLine(now);
            return true;
        }

        bool PrepareGeometry()
        {
            if (sprite != null)
            {
                if (sprite.sprite == null) return false;
                Bounds b = sprite.sprite.bounds;
                float sx = sprite.flipX ? -1 : 1, sy = sprite.flipY ? -1 : 1;
                float pad = template.edgeWidth * 1.6f;
                coverageRect = new Vector4(Mathf.Min(b.min.x * sx, b.max.x * sx) - pad,
                    Mathf.Min(b.min.y * sy, b.max.y * sy) - pad,
                    Mathf.Max(b.min.x * sx, b.max.x * sx) + pad,
                    Mathf.Max(b.min.y * sy, b.max.y * sy) + pad);
                Matrix4x4 m = sprite.transform.localToWorldMatrix;
                bottomLeft = m.MultiplyPoint3x4(new Vector3(coverageRect.x, coverageRect.y));
                stepX = m.MultiplyVector(Vector3.right * ((coverageRect.z - coverageRect.x) / width));
                stepY = m.MultiplyVector(Vector3.up * ((coverageRect.w - coverageRect.y) / height));
                return true;
            }
            if (line == null || line.positionCount < 2) return false;
            widthCurve = line.widthCurve; widthMultiplier = line.widthMultiplier;
            int count = line.positionCount;
            if (positions == null || positions.Length != count) positions = new Vector3[count];
            line.GetPositions(positions);
            if (!line.useWorldSpace)
                for (int i = 0; i < count; i++) positions[i] = line.transform.TransformPoint(positions[i]);
            int segments = count - 1 + (line.loop ? 1 : 0);
            if (distances == null || distances.Length != segments + 1) distances = new float[segments + 1];
            distances[0] = 0f;
            for (int i = 0; i < segments; i++) distances[i + 1] = distances[i] + Vector3.Distance(positions[i], positions[(i + 1) % count]);
            float previousLength = lineLength;
            lineLength = distances[segments];
            if (ropeCoordinates && previousLength > .00001f && lineLength > .00001f
                && Mathf.Abs(lineLength - previousLength) > .000001f)
            {
                // Rope pays out/reels in at the miner, rather than stretching icy material.
                if (shiftedExpires == null) shiftedExpires = new float[expires.Length];
                float paidOut = lineLength - previousLength;
                for (int x = 0; x < width; x++)
                {
                    float oldU = (((x + .5f) / width) * lineLength - paidOut) / previousLength;
                    int oldX = Mathf.FloorToInt(oldU * width);
                    for (int y = 0; y < height; y++)
                        shiftedExpires[y * width + x] = oldX >= 0 && oldX < width ? expires[y * width + oldX] : 0f;
                }
                System.Array.Copy(shiftedExpires, expires, expires.Length);
            }
            return lineLength > .00001f;
        }

        void Column(int x, out Vector2 left, out Vector2 right, out Vector2 leftUp, out Vector2 rightUp)
        {
            if (sprite != null)
            {
                left = bottomLeft + stepX * x; right = left + stepX; leftUp = rightUp = stepY; return;
            }
            LineEdge(x / (float)width, out left, out leftUp);
            LineEdge((x + 1f) / width, out right, out rightUp);
        }

        void LineEdge(float u, out Vector2 bottom, out Vector2 up)
        {
            float distance = u * lineLength;
            int segment = 0;
            while (segment < distances.Length - 2 && distances[segment + 1] < distance) segment++;
            Vector2 from = positions[segment], to = positions[(segment + 1) % positions.Length];
            float t = Mathf.InverseLerp(distances[segment], distances[segment + 1], distance);
            Vector2 direction = (to - from).normalized;
            float thickness = widthCurve.Evaluate(u) * widthMultiplier;
            // Native LineRenderer UVs run toward the right-hand side of an XY segment.
            Vector2 across = new Vector2(direction.y, -direction.x) * thickness;
            bottom = Vector2.Lerp(from, to, t) - across * .5f; up = across / height;
        }

        void EnsureEffect(float now)
        {
            if (coverage != null) return;
            started = now;
            coverage = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { name = "Three-second frost patches", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (sprite != null)
            {
                spriteEffect = Object.Instantiate(template, sprite.transform, false);
                spriteEffect.name = "Frost patches"; spriteEffect.target = sprite;
                spriteEffect.coverageMask = coverage; spriteEffect.coverageRect = coverageRect;
            }
            else
            {
                var go = new GameObject("Frost line patches");
                go.transform.SetParent(line.transform, false); go.AddComponent<FrostArtOverlay2D>();
                lineEffect = go.AddComponent<LineRenderer>();
                lineEffect.sharedMaterial = template.SharedFrostMaterial; properties = new MaterialPropertyBlock();
            }
        }

        void UpdateLine(float now)
        {
            lineEffect.enabled = Eligible(source); lineEffect.useWorldSpace = true; lineEffect.loop = line.loop;
            lineEffect.positionCount = positions.Length; lineEffect.SetPositions(positions);
            lineEffect.widthCurve = widthCurve; lineEffect.widthMultiplier = widthMultiplier;
            lineEffect.numCapVertices = line.numCapVertices; lineEffect.numCornerVertices = line.numCornerVertices;
            lineEffect.alignment = line.alignment; lineEffect.textureMode = LineTextureMode.Stretch;
            lineEffect.startColor = line.startColor; lineEffect.endColor = line.endColor;
            lineEffect.sortingLayerID = line.sortingLayerID; lineEffect.sortingOrder = Mathf.Min(short.MaxValue, line.sortingOrder + 1);
            float seconds = (now - started) * template.animationSpeed;
            properties.SetFloat("_StrokeMode", 1f); properties.SetFloat("_HasCoverage", 1f);
            properties.SetTexture("_CoverageTex", coverage);
            properties.SetColor("_FrostColor", FrostEffect.OriginalRimColor(seconds));
            properties.SetFloat("_EffectTime", seconds); properties.SetFloat("_Opacity", template.opacity);
            properties.SetFloat("_BodyFrost", template.bodyFrost); properties.SetFloat("_EdgeWidth", template.edgeWidth);
            properties.SetFloat("_TargetAlpha", 1f); lineEffect.SetPropertyBlock(properties);
        }

        void Hide()
        {
            if (spriteEffect != null) spriteEffect.gameObject.SetActive(false);
            if (lineEffect != null) lineEffect.enabled = false;
        }
        public void Dispose()
        {
            Hide();
            if (spriteEffect != null) Object.Destroy(spriteEffect.gameObject);
            if (lineEffect != null) Object.Destroy(lineEffect.gameObject);
            if (coverage != null) Object.Destroy(coverage);
        }
    }
}

// Generated line overlays must never become new frost targets themselves.
public sealed class FrostArtOverlay2D : MonoBehaviour { }
