using UnityEngine;

// Owner: Doeun (UI)
// Draws the arena only in HUDLayout's gameplay area, so the top bar and sidebar never cover it.
// The levels are balanced for a 16:9 arena (tools/sim: ARENA_X = 8.89 at ortho size 5), so the
// camera zooms out until all of it fits instead of cropping the edges
[RequireComponent(typeof(Camera))]
public class GameplayCamera : MonoBehaviour
{
    public Vector2 arenaSize = new Vector2(17.78f, 10f);   // world units, centered on the camera

    private Camera cam;
    private int lastWidth;
    private int lastHeight;

    // The HUD calls this, so any scene with a HUD gets the split without editing the scene
    public static void Ensure(Camera camera)
    {
        if (camera != null && camera.GetComponent<GameplayCamera>() == null)
            camera.gameObject.AddComponent<GameplayCamera>();
    }

    // Grid tutorial levels are taller/wider than the sim arena; LevelLoader sets this after parse.
    public static void SetArenaSize(Vector2 size)
    {
        GameplayCamera cam = FindFirstObjectByType<GameplayCamera>();
        if (cam == null)
        {
            Camera main = Camera.main;
            if (main != null) Ensure(main);
            cam = FindFirstObjectByType<GameplayCamera>();
        }
        if (cam == null) return;
        cam.arenaSize = size;
        cam.Fit();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        Fit();
    }

    // The WebGL canvas follows the browser window, so refit whenever the screen size changes
    void LateUpdate()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight) Fit();
    }

    public void Fit()
    {
        if (cam == null) cam = GetComponent<Camera>();
        lastWidth = Screen.width;
        lastHeight = Screen.height;

        Rect viewport = HUDLayout.GameplayViewport;
        cam.rect = viewport;

        // Big enough for the arena's height, and for its width at this area's aspect
        float aspect = Screen.width * viewport.width / Mathf.Max(1f, Screen.height * viewport.height);
        cam.orthographicSize = Mathf.Max(arenaSize.y * 0.5f, arenaSize.x * 0.5f / aspect);
    }
}
