using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Owner: Butian (screen layout)
// Every scene plays in one 16:9 frame, centered on the screen with black bars around it, so a wide
// monitor, a 16:10 laptop, a browser canvas and a phone all show the same picture. It also makes
// the UI resolution independent:
//   - Cameras draw only inside the frame (GameplayCamera splits it further for the HUD).
//   - Screen-space canvases get their contents moved into a 16:9 child, and their CanvasScaler set
//     so that child is always the reference resolution. A canvas laid out at 1920x1080 is laid out
//     the same at 1280x720 or 3840x2160.
//   - OnGUI code calls BeginGUI() and lays out in GuiWidth x GuiHeight (1280 x 720), scaled to the
//     frame. Text then takes the same share of the screen on every monitor.
// It installs itself before the first scene loads; no scene needs to reference it.
public class ScreenFrame : MonoBehaviour
{
    public const float Aspect = 16f / 9f;
    public const float GuiWidth = 1280f;
    public const float GuiHeight = 720f;

    const string FrameName = "Frame16x9";

    // The 16:9 frame in screen pixels (origin bottom left, as Screen and Camera.pixelRect use)
    public static Rect Pixels
    {
        get
        {
            float w = Mathf.Max(1, Screen.width);
            float h = Mathf.Max(1, Screen.height);
            if (w / h > Aspect)
            {
                float fw = Mathf.Round(h * Aspect);
                return new Rect(Mathf.Round((w - fw) * 0.5f), 0f, fw, h);   // pillarbox
            }
            float fh = Mathf.Round(w / Aspect);
            return new Rect(0f, Mathf.Round((h - fh) * 0.5f), w, fh);       // letterbox
        }
    }

    // The frame as a Camera.rect (0..1 of the screen)
    public static Rect Viewport
    {
        get
        {
            Rect p = Pixels;
            float w = Mathf.Max(1, Screen.width);
            float h = Mathf.Max(1, Screen.height);
            return new Rect(p.x / w, p.y / h, p.width / w, p.height / h);
        }
    }

    // A rect given as 0..1 of the frame (e.g. HUDLayout.GameplayViewport), as 0..1 of the screen
    public static Rect ToViewport(Rect inFrame)
    {
        Rect v = Viewport;
        return new Rect(v.x + inFrame.x * v.width, v.y + inFrame.y * v.height,
                        inFrame.width * v.width, inFrame.height * v.height);
    }

    // Screen pixels per OnGUI unit
    public static float GuiScale => Pixels.height / GuiHeight;

    // Call first in OnGUI: from here on, (0,0)-(GuiWidth,GuiHeight) is the frame, top left origin.
    // Clicks and Event.current.mousePosition follow the same space
    public static void BeginGUI()
    {
        Rect p = Pixels;
        float s = GuiScale;
        GUI.matrix = Matrix4x4.TRS(new Vector3(p.x, Screen.height - p.yMax, 0f), Quaternion.identity, new Vector3(s, s, 1f));
    }

    // A screen point (Camera.WorldToScreenPoint, origin bottom left) in BeginGUI's space
    public static Vector2 ScreenToGui(Vector2 screen)
    {
        Rect p = Pixels;
        float s = GuiScale;
        return new Vector2((screen.x - p.x) / s, (p.yMax - screen.y) / s);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        var go = new GameObject("ScreenFrame");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenFrame>();
    }

    Camera bars;
    int lastWidth;
    int lastHeight;
    int scanFrames;   // frames left to look for new canvases after a scene load
    readonly List<RectTransform> frames = new List<RectTransform>();

    void Awake()
    {
        // Clears the whole screen to black under the scene's camera, which only draws in the frame.
        // Without it, the bars would keep whatever the last frame left there (WebGL, some GPUs)
        bars = gameObject.AddComponent<Camera>();
        bars.clearFlags = CameraClearFlags.SolidColor;
        bars.backgroundColor = Color.black;
        bars.cullingMask = 0;
        bars.orthographic = true;
        bars.depth = -100f;
        bars.useOcclusionCulling = false;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        lastWidth = -1;      // fit the new scene's cameras
        scanFrames = 3;      // canvases made in Start (the store) show up a frame later
    }

    void LateUpdate()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            FitCameras();
        }
        if (scanFrames > 0)
        {
            scanFrames--;
            FrameCanvases();
        }
        KeepChildrenInFrames();
    }

    void FitCameras()
    {
        Rect viewport = Viewport;
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam == bars || cam.targetTexture != null) continue;
            if (cam.GetComponent<GameplayCamera>() != null) continue;   // it splits the frame for the HUD itself
            cam.rect = viewport;
        }
    }

    void FrameCanvases()
    {
        frames.RemoveAll(f => f == null);
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.transform.Find(FrameName) != null) continue;
            frames.Add(Frame(canvas));
        }
    }

    // Puts a 16:9 child in the canvas, the size of the scaler's reference resolution, and moves
    // everything into it. The scaler's Expand mode makes the canvas at least that big both ways, so
    // the child covers exactly the camera's frame
    static RectTransform Frame(Canvas canvas)
    {
        var frame = new GameObject(FrameName, typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(canvas.transform, false);

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            // Keep the reference height the canvas was designed with; make it 16:9
            float height = scaler.referenceResolution.y;
            scaler.referenceResolution = new Vector2(height * Aspect, height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = scaler.referenceResolution;
            frame.anchoredPosition = Vector2.zero;
        }
        else
        {
            // Pixel-sized canvas: still keep its layout inside the frame
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.sizeDelta = Vector2.zero;
            AspectRatioFitter fitter = frame.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = Aspect;
        }

        MoveChildrenInto(frame);
        return frame;
    }

    // UI added to the canvas root later (the HUD's pause popup, score popups) goes into the frame too
    void KeepChildrenInFrames()
    {
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            RectTransform frame = frames[i];
            if (frame == null) { frames.RemoveAt(i); continue; }
            if (frame.parent.childCount > 1) MoveChildrenInto(frame);
        }
    }

    // Same anchors and offsets, now relative to the frame; order kept, so draw order is unchanged
    static void MoveChildrenInto(RectTransform frame)
    {
        Transform root = frame.parent;
        var children = new List<Transform>();
        foreach (Transform child in root)
            if (child != frame) children.Add(child);
        foreach (Transform child in children) child.SetParent(frame, false);
    }
}
