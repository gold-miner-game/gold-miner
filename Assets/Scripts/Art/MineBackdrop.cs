using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the felt-and-paper mine floor behind every orthographic scene. It follows the main camera and
// covers its view, so no scene needs editing. Slow time (the time machine) no longer tints it: PaperSkillFx
// bleaches the whole floor layer toward white paper instead.
// Each battle level tints the felt a little differently (by level file name), so every level has its own
// ground without new art: warmer clay near the top of the mine, cooler charcoal deeper down. The hub and
// other scenes keep the plain floor.
[DefaultExecutionOrder(1000)]
public class MineBackdrop : MonoBehaviour
{
    const string FloorArt = "MineFloor";
    static readonly Color Floor = new Color(0.92f, 0.9f, 0.88f);

    SpriteRenderer look;
    Camera cam;
    Color floor = Floor;
    LevelLoader level;
    float nextLook;

    static Color LevelFloor(string name)
    {
        switch (name)
        {
            case "level_01a_dodge": return new Color(0.98f, 0.9f, 0.84f);   // warm clay
            case "level_01b_block": return new Color(0.88f, 0.88f, 0.9f);   // cool grey-brown
            case "level_02": return Floor;                                  // level 3: the strata do the talking
            case "level_03": return new Color(1f, 0.86f, 0.8f);             // red earth
            case "level_04": return new Color(0.9f, 0.9f, 0.8f);            // olive umber
            case "level_05": return new Color(0.82f, 0.84f, 0.9f);          // cold charcoal
            case "level_06": return new Color(0.98f, 0.92f, 0.78f);         // ochre
            case "level_07": return new Color(0.78f, 0.8f, 0.86f);          // deep slate
            case "level_08": return new Color(0.74f, 0.72f, 0.76f);         // the deepest, darkest gallery
            default: return Floor;
        }
    }

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
        if (Time.unscaledTime >= nextLook)   // the level's name is a fresh string each read: twice a second
        {
            nextLook = Time.unscaledTime + 0.5f;
            level = MapHub.Active == null ? FindAnyObjectByType<LevelLoader>() : null;
            floor = level != null && FindAnyObjectByType<SurvivalLoader>() == null ? LevelFloor(level.LevelName()) : Floor;
        }
        look.color = floor;
    }
}
