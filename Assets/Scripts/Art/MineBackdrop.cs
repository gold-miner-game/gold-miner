using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the felt-and-paper mine floor behind every orthographic scene. It follows the main camera and
// covers its view, so no scene needs editing. During the store's slow time it cools toward blue-green,
// the same cue the camera's background colour gave before (TimeMovesWithPlayer.Slowness).
[DefaultExecutionOrder(1000)]
public class MineBackdrop : MonoBehaviour
{
    const string FloorArt = "MineFloor";
    static readonly Color Floor = new Color(0.92f, 0.9f, 0.88f);
    static readonly Color SlowFloor = new Color(0.55f, 0.75f, 0.72f);

    SpriteRenderer look;
    Camera cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    static void Ensure()
    {
        if (FindAnyObjectByType<MineBackdrop>() != null) return;
        Camera main = Camera.main;
        if (main == null || !main.orthographic) return;
        Sprite floor = ArtLibrary.Get(FloorArt);
        if (floor == null) return;
        var go = new GameObject("Mine Backdrop");
        SceneManager.MoveGameObjectToScene(go, main.gameObject.scene);
        var backdrop = go.AddComponent<MineBackdrop>();
        backdrop.cam = main;
        backdrop.look = go.AddComponent<SpriteRenderer>();
        backdrop.look.sprite = floor;
        backdrop.look.drawMode = SpriteDrawMode.Tiled;
        backdrop.look.tileMode = SpriteTileMode.Continuous;
        backdrop.look.sortingOrder = short.MinValue;
        backdrop.look.color = Floor;
        backdrop.LateUpdate();
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || look == null) return;
        float height = cam.orthographicSize * 2f + 1f;
        float width = height * Mathf.Max(cam.aspect, 0.1f) + 1f;
        Vector3 at = cam.transform.position;
        transform.position = new Vector3(at.x, at.y, at.z + cam.farClipPlane * 0.9f);
        look.size = new Vector2(width, height);
        look.color = Color.Lerp(Floor, SlowFloor, TimeMovesWithPlayer.Slowness);
    }
}
