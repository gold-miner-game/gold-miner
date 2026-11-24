using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Plays the original 30-foot cold-cone capture as a 24-fps 2D flipbook.
/// Length and Half Arc resize the baked image; they do not resimulate particles.
/// Local +Y is forward, local XY is the effect plane, and the origin is its mouth.
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ColdBreathFlipbook2D : MonoBehaviour
{
    [Header("Shape")]
    [Min(0.01f), Tooltip("Nominal length in scene units. The original 30-foot capture is 9.144 units.")]
    public float length = 9.144f;
    [Range(1f, 89f), Tooltip("Half of the cone's full arc. 45 means a 90-degree cone. Widens/narrows the captured image around its mouth.")]
    public float halfArc = 45f;

    [Header("Playback")]
    public bool loop = true;
    [Tooltip("An owning skill supplies playback time through ShowTime instead of the real-time clock.")]
    public bool externalPlayback = false;
    public static float LastFrameTime => (Frames - 1) / Fps;
    public static float MainWaveEndTime => 36f / Fps;
    public static float PlaybackDuration => Frames / Fps;
    // Conservative visible front, measured from each baked frame (visible alpha
    // threshold 32/255, 98th percentile in 10-degree sectors). No texture reads
    // are needed during play. Distances use the original 45-degree shape;
    // widening the effect stretches this front sideways with the artwork.
    static readonly float[] ContactFront = {
        0f,.0314f,.1311f,.1559f,.1787f,.2053f,.2469f,.2867f,
        .3232f,.3601f,.3962f,.4313f,.4643f,.5118f,.5417f,.5702f,
        .5971f,.6226f,.6465f,.6693f,.6909f,.7104f,.7288f,.7462f,
        .7684f,.7813f,.7927f,.8014f,.8096f,.8167f,.8218f,.823f,
        .8202f,.8149f,.8043f,.7963f,.8623f,.8727f,.8789f,.7334f,
        .7404f,.7498f,.7573f,.7645f,.7731f,.78f,.7884f,.7879f
    };
    public float ContactLength => previousFrame >= 0 && previousFrame < 36 ? length * ContactFront[previousFrame] : 0f;
    [Tooltip("Animate in the Scene view without entering Play mode.")]
    public bool animateInEditor = false;
    [Range(0f, 1.958333f), Tooltip("Shown when not playing/animating. Default is a visible frame, so the prefab is never an empty preview.")]
    public float previewTime = 0.666667f;

    [SerializeField, HideInInspector] Texture2D atlas;
    [SerializeField, HideInInspector] Material animationMaterial;
    Mesh mesh;
    MaterialPropertyBlock properties;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    float previousLength=-1, previousArc=-1;
    double startedAt;
    bool wasPlaying;
    int previousFrame=-1;
    // ContactLength keeps using the true 24 fps frame. Only the atlas has a held cache:
    // changing the contact frame would change Elsa's frost reach and Editor test queries.
    int displayedFrame=-1, heldFrame=-1, heldTick=int.MinValue;
    bool wasStepping, hasPlaybackTime;
    float playbackTime;
#if UNITY_EDITOR
    double lastEditorTick;
#endif
    const int Columns=8, Rows=6, Frames=48;
    const float Fps=24f;
    const int FrameWidth=912, FrameHeight=560, Gutter=2;
    const int AtlasWidth=7328, AtlasHeight=3384;
    const float PivotX=0.473684211f, PivotY=0.011540716f;
    const float WidthPerLength=1.672000000f, HeightPerLength=1.026666667f;

    void OnEnable()
    {
        wasPlaying=Application.isPlaying;
        startedAt=Clock();
        EnsureObjects();
        RefreshShape();
        ResetHeldFrame();
        SetFrame(Mathf.FloorToInt(previewTime*Fps));
#if UNITY_EDITOR
        EditorApplication.update-=EditorTick;
        EditorApplication.update+=EditorTick;
#endif
    }
    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update-=EditorTick;
#endif
        if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        mesh=null;
        ResetHeldFrame();
    }
    void OnValidate()
    {
        length=Mathf.Max(.01f,length);halfArc=Mathf.Clamp(halfArc,1,89);
        previousLength=-1;previousFrame=-1;
        displayedFrame=-1;ResetHeldFrame();
    }
    void Update()
    {
        EnsureObjects();RefreshShape();
        // ExecuteAlways can be enabled before Unity flips isPlaying. Reset the
        // clock at that transition so editor time never becomes runtime time.
        if(wasPlaying!=Application.isPlaying){wasPlaying=Application.isPlaying;startedAt=Clock();ResetHeldFrame();}
        if(Application.isPlaying && !externalPlayback)ShowTime((float)(Clock()-startedAt));
        else if(!Application.isPlaying && !animateInEditor)ShowTime(previewTime);
        // External playback can pause its calls. A console toggle must still release/reseed
        // the drawing immediately on the next Update, even while the shared clock is paused.
        else if(hasPlaybackTime && wasStepping!=(Application.isPlaying && StopMotion.Enabled))ShowTime(playbackTime);
    }
    static double Clock()
    {
#if UNITY_EDITOR
        if(!Application.isPlaying)return EditorApplication.timeSinceStartup;
#endif
        return Time.unscaledTime;
    }
#if UNITY_EDITOR
    void EditorTick()
    {
        if(this==null||!isActiveAndEnabled||Application.isPlaying||!animateInEditor)return;
        double now=EditorApplication.timeSinceStartup;
        if(now-lastEditorTick<1.0/Fps)return;
        lastEditorTick=now;EnsureObjects();RefreshShape();ShowTime((float)(now-startedAt));
        EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
    }
#endif
    [ContextMenu("Replay")]
    public void Replay(){startedAt=Clock();previousFrame=-1;displayedFrame=-1;ResetHeldFrame();ShowTime(0);}

    void ResetHeldFrame(){heldTick=int.MinValue;heldFrame=-1;wasStepping=false;hasPlaybackTime=false;}

    /// <summary>Useful for a Timeline/event hook or choosing a fixed animation moment.</summary>
    public void ShowTime(float seconds)
    {
        EnsureObjects();RefreshShape();
        int frame=Mathf.FloorToInt(Mathf.Max(0,seconds)*Fps);
        frame=loop?frame%Frames:Mathf.Min(frame,Frames-1);
        bool stepping=Application.isPlaying && StopMotion.Enabled;
        int shown=frame;
        if(stepping)
        {
            int tick=StopMotion.Frame;
            // Latch to the shared scaled clock as well as selecting even atlas frames.
            // Scalar caches introduce no per-frame allocations; backwards seeks/replays
            // and toggles reseed instead of leaving a stale drawing from another playback.
            if(!wasStepping || heldTick!=tick || !hasPlaybackTime || seconds<playbackTime)
            {
                heldTick=tick;
                heldFrame=(frame/2)*2;
            }
            shown=heldFrame;
        }
        wasStepping=stepping;hasPlaybackTime=true;playbackTime=seconds;
        SetFrame(frame,shown);
    }
    void EnsureObjects()
    {
        if(meshFilter==null)meshFilter=GetComponent<MeshFilter>();
        if(meshRenderer==null)meshRenderer=GetComponent<MeshRenderer>();
        if(properties==null)properties=new MaterialPropertyBlock();
        if(mesh==null)
        {
            mesh=new Mesh {name="ColdBreath2D generated quad",hideFlags=HideFlags.HideAndDontSave};
            mesh.vertices=new Vector3[4];
            mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
            mesh.triangles=new[]{0,2,1,2,3,1};
            previousLength=-1;
        }
        if(meshFilter.sharedMesh!=mesh)meshFilter.sharedMesh=mesh;
        if(animationMaterial!=null&&meshRenderer.sharedMaterial!=animationMaterial)meshRenderer.sharedMaterial=animationMaterial;
        meshRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows=false;
    }
    void RefreshShape()
    {
        if(mesh==null||Mathf.Approximately(length,previousLength)&&Mathf.Approximately(halfArc,previousArc))return;
        float height=HeightPerLength*length;
        float width=WidthPerLength*length*Mathf.Tan(halfArc*Mathf.Deg2Rad);
        mesh.vertices=new[]{new Vector3(-PivotX*width,-PivotY*height,0),new Vector3((1-PivotX)*width,-PivotY*height,0),new Vector3(-PivotX*width,(1-PivotY)*height,0),new Vector3((1-PivotX)*width,(1-PivotY)*height,0)};
        mesh.RecalculateBounds();previousLength=length;previousArc=halfArc;
    }
    void SetFrame(int frame, int shown=-1)
    {
        frame=Mathf.Clamp(frame,0,Frames-1);
        if(meshRenderer==null||atlas==null)return;
        previousFrame=frame; // True contact advances even when the displayed atlas pose holds.
        if(shown<0)shown=frame;
        if(shown==displayedFrame)return;
        int col=shown%Columns,row=shown/Columns;
        float x=(col*(FrameWidth+2*Gutter)+Gutter)/(float)AtlasWidth;
        float y=(AtlasHeight-row*(FrameHeight+2*Gutter)-Gutter-FrameHeight)/(float)AtlasHeight;
        properties.SetTexture("_MainTex",atlas);
        properties.SetVector("_FrameRect",new Vector4(x,y,FrameWidth/(float)AtlasWidth,FrameHeight/(float)AtlasHeight));
        meshRenderer.SetPropertyBlock(properties);displayedFrame=shown;
    }
}
