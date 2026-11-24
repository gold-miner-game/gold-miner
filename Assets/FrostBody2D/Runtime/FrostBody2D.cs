using UnityEngine;

namespace FrostBody2D
{
    /// <summary>
    /// Portable 2D adaptation of Common_ColdBuff00. This draws over the current
    /// sprite silhouette; it is not a port of the original 3D particle simulation.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FrostBody2D : MonoBehaviour
    {
        [Tooltip("Your body sprite. If empty, the nearest parent SpriteRenderer is used.")]
        public SpriteRenderer target;
        [Min(0.001f), Tooltip("Frost width in the body sprite's local units.")]
        public float edgeWidth = 0.045f;
        [Range(0, 1)] public float opacity = 1f;
        [Range(0, 1), Tooltip("Thin blue frost over the inside of the body.")]
        public float bodyFrost = 0.18f;
        [Min(0)] public float animationSpeed = 1f;
        public bool animateInEditor = true;
        [Min(0)] public float previewTime = 1.5f;
        [System.NonSerialized] public Texture coverageMask;
        [System.NonSerialized] public Vector4 coverageRect;
        public Material SharedFrostMaterial => frostMaterial;
        [SerializeField, HideInInspector] Material frostMaterial;
        [System.NonSerialized] GameObject overlay;
        Mesh mesh;
        MeshRenderer overlayRenderer;
        MaterialPropertyBlock properties;
        Sprite lastSprite;
        SpriteRenderer lastTarget;
        bool lastFlipX, lastFlipY;
        float lastWidth = -1;
        double started;
        bool wasPlaying;
        Vector2 uvOrigin, uvX, uvY;
        Vector4 bodyRect;

        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int UVOrigin = Shader.PropertyToID("_UVOrigin");
        static readonly int UVX = Shader.PropertyToID("_UVX");
        static readonly int UVY = Shader.PropertyToID("_UVY");
        static readonly int BodyRect = Shader.PropertyToID("_BodyRect");
        static readonly int FrostColor = Shader.PropertyToID("_FrostColor");
        static readonly int EdgeWidth = Shader.PropertyToID("_EdgeWidth");
        static readonly int EffectTime = Shader.PropertyToID("_EffectTime");
        static readonly int Opacity = Shader.PropertyToID("_Opacity");
        static readonly int BodyFrost = Shader.PropertyToID("_BodyFrost");
        static readonly int TargetAlpha = Shader.PropertyToID("_TargetAlpha");

        void OnEnable()
        {
            started = Clock(); wasPlaying = Application.isPlaying;
            Refresh();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            Dispose(overlay); Dispose(mesh);
            overlay = null; mesh = null; overlayRenderer = null;
            lastSprite = null; lastTarget = null;
        }
        void OnValidate() { edgeWidth = Mathf.Max(.001f, edgeWidth); lastWidth = -1; }
        void LateUpdate() { Refresh(); }
#if UNITY_EDITOR
        double lastEditorTick;
        void EditorTick()
        {
            if (this == null || !isActiveAndEnabled || Application.isPlaying) return;
            if (Clock() - lastEditorTick < 1.0 / 24) return;
            lastEditorTick = Clock(); Refresh();
            if (animateInEditor) UnityEditor.SceneView.RepaintAll();
        }
#endif
        static void Dispose(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
        static double Clock()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.timeAsDouble;
        }
        /// <summary>Restart the frost fade-in when applying the status again.</summary>
        public void Replay() { started = Clock(); Refresh(); }
        /// <summary>Refresh after assigning target from code; normal animation updates automatically.</summary>
        public void Refresh()
        {
            if (!isActiveAndEnabled) return;
            if (wasPlaying != Application.isPlaying) { wasPlaying = Application.isPlaying; started = Clock(); }
            SpriteRenderer current = target != null ? target : GetComponentInParent<SpriteRenderer>();
            if (current == null || current.sprite == null || frostMaterial == null)
            { if (overlayRenderer != null) overlayRenderer.enabled = false; return; }
            // Tiled/sliced SpriteRenderers deform their geometry beyond sprite bounds.
            // Explicitly suppress these instead of silently showing a misplaced effect.
            if (current.drawMode != SpriteDrawMode.Simple)
            { if (overlayRenderer != null) overlayRenderer.enabled = false; return; }
            if (overlay == null)
            {
                overlay = new GameObject("Frost Body 2D (generated)");
                overlay.hideFlags = HideFlags.HideAndDontSave;
                mesh = new Mesh { name = "Frost silhouette overlay", hideFlags = HideFlags.HideAndDontSave };
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                overlayRenderer = overlay.AddComponent<MeshRenderer>();
                overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                overlayRenderer.receiveShadows = false;
                properties = new MaterialPropertyBlock();
            }
            if (overlay.transform.parent != current.transform)
            {
                overlay.transform.SetParent(current.transform, false);
                overlay.transform.localPosition = new Vector3(0, 0, -.0001f);
                overlay.transform.localRotation = Quaternion.identity;
                overlay.transform.localScale = Vector3.one;
            }
            overlayRenderer.sharedMaterial = frostMaterial;
            overlayRenderer.sortingLayerID = current.sortingLayerID;
            overlayRenderer.sortingOrder = current.sortingOrder == short.MaxValue ? current.sortingOrder : current.sortingOrder + 1;
            overlayRenderer.enabled = current.enabled && !current.forceRenderingOff && current.gameObject.activeInHierarchy;
            if (lastSprite != current.sprite || lastTarget != current || lastFlipX != current.flipX || lastFlipY != current.flipY || lastWidth != edgeWidth)
            {
                if (!BuildMesh(current)) { overlayRenderer.enabled = false; return; }
                lastSprite = current.sprite; lastTarget = current;
                lastFlipX = current.flipX; lastFlipY = current.flipY; lastWidth = edgeWidth;
            }
            float seconds = Application.isPlaying || animateInEditor ? (float)(Clock() - started) * animationSpeed : previewTime;
            Color rim = OriginalRimColor(seconds);
            properties.Clear();
            properties.SetTexture(MainTex, current.sprite.texture);
            properties.SetVector(UVOrigin, uvOrigin); properties.SetVector(UVX, uvX); properties.SetVector(UVY, uvY);
            properties.SetVector(BodyRect, bodyRect); properties.SetColor(FrostColor, rim);
            properties.SetFloat(EdgeWidth, edgeWidth); properties.SetFloat(EffectTime, PaperFrost(properties, seconds));
            properties.SetFloat(Opacity, opacity); properties.SetFloat(BodyFrost, bodyFrost);
            properties.SetFloat(TargetAlpha, current.color.a);
            properties.SetFloat("_HasCoverage", coverageMask != null ? 1f : 0f);
            if (coverageMask != null)
            {
                properties.SetTexture("_CoverageTex", coverageMask);
                properties.SetVector("_CoverageRect", coverageRect);
            }
            overlayRenderer.SetPropertyBlock(properties);
        }
        bool BuildMesh(SpriteRenderer renderer)
        {
            Sprite sprite = renderer.sprite;
            Vector2[] vertices = sprite.vertices, uv = sprite.uv;
            if (vertices.Length < 3) return false;
            bool solved = false;
            for (int i = 1; i < vertices.Length - 1; i++)
            {
                Vector2 a = vertices[i] - vertices[0], b = vertices[i + 1] - vertices[0];
                float det = a.x * b.y - a.y * b.x;
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector2 du = uv[i] - uv[0], dv = uv[i + 1] - uv[0];
                uvX = (du * b.y - dv * a.y) / det;
                uvY = (dv * a.x - du * b.x) / det;
                uvOrigin = uv[0] - uvX * vertices[0].x - uvY * vertices[0].y;
                solved = true; break;
            }
            if (!solved) return false;
            float sx = renderer.flipX ? -1 : 1, sy = renderer.flipY ? -1 : 1;
            uvX *= sx; uvY *= sy;
            Bounds bounds = sprite.bounds;
            float x0 = Mathf.Min(bounds.min.x * sx, bounds.max.x * sx), x1 = Mathf.Max(bounds.min.x * sx, bounds.max.x * sx);
            float y0 = Mathf.Min(bounds.min.y * sy, bounds.max.y * sy), y1 = Mathf.Max(bounds.min.y * sy, bounds.max.y * sy);
            bodyRect = new Vector4(x0, y0, x1, y1);
            float padding = edgeWidth * 1.6f;
            x0 -= padding; x1 += padding; y0 -= padding; y1 += padding;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(x0,y0), new Vector3(x1,y0), new Vector3(x0,y1), new Vector3(x1,y1) };
            mesh.uv = new[] { new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x0,y1), new Vector2(x1,y1) };
            mesh.triangles = new[] { 0,2,1, 2,3,1 };
            mesh.RecalculateBounds(); return true;
        }
        // Common_ColdBuff00/Fresnel: first 3 seconds fade from white-blue to
        // blue, then a persistent blue rim starts at 3.03 s. Values are copied
        // from RimLightingAnimationSetup, rather than sampled from screenshots.
        // Art (papercraft pass): the frost rim's noise is torn paper and fibre (Resources/Art/Tiles/
        // PaperFrostNoise, same mean and contrast as the original noise), and its drift and twinkle step on
        // twos like every other art motion. Look only: the rim colour timeline and all frost gameplay
        // (coverage, expiry, freezing) keep their own unsnapped time. No art: the original look.
        internal static float PaperFrost(MaterialPropertyBlock block, float seconds)
        {
            Sprite noise = ArtLibrary.Get("Tiles/PaperFrostNoise");
            if (noise == null) return seconds;
            block.SetTexture("_FrostNoise", noise.texture);
            return StopMotion.Snap(seconds, StopMotion.Pose);
        }

        internal static Color OriginalRimColor(float seconds)
        {
            if (seconds >= 3.03f) return new Color(.5450981f, .682353f, 1, .7058824f);
            if (seconds > 3f) return Color.clear;
            float time = Mathf.Max(0, seconds) / 3;
            Color white = new Color(.8980392f,.9333333f,1,1), pale = new Color(.772549f,.8705882f,1,1), blue = new Color(.5450981f,.682353f,1,1);
            Color c = time < 5012f/65535 ? Color.Lerp(white,pale,time/(5012f/65535)) : Color.Lerp(pale,blue,(time-5012f/65535)/((41827f-5012)/65535));
            c.a = time < 1735f/65535 ? time/(1735f/65535) : time < 15998f/65535 ? 1 : Mathf.Lerp(1,.7058824f,(time-15998f/65535)/(1-15998f/65535));
            return c;
        }
    }
}
