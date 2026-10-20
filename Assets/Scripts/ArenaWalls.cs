using UnityEngine;

// Owner: Butian (screen layout)
// Four invisible solid walls on the edges of what the camera shows, so the player stops at the edge
// of the screen instead of walking off it. LevelLoader adds one for the normal levels (survival has
// no edge and doesn't use it). The walls follow the camera's view, so they stay on the screen edges
// when the window is resized. Hooks pass through them (Hook only stops on treasure, rocks and the
// player), and treasure is a trigger, so only the player's body bumps into them.
public class ArenaWalls : MonoBehaviour
{
    [Tooltip("How thick each wall is, outside the view (world units). Thick, so a fast move can't tunnel through")]
    public float thickness = 2f;

    private Camera cam;
    private BoxCollider2D left, right, top, bottom;
    private Rect lastView;

    void Awake()
    {
        left = MakeWall("WallLeft");
        right = MakeWall("WallRight");
        top = MakeWall("WallTop");
        bottom = MakeWall("WallBottom");
    }

    BoxCollider2D MakeWall(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        // A static body: the player's dynamic Rigidbody2D collides with it and stops
        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        return go.AddComponent<BoxCollider2D>();
    }

    // LateUpdate: GameplayCamera fits the view to the screen before this reads it
    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        Vector2 c = cam.transform.position;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;   // the camera's own rect, so the HUD and the bars aren't counted
        Rect view = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
        if (view == lastView) return;
        lastView = view;

        float t = thickness;
        Place(left,   new Vector2(view.xMin - t * 0.5f, view.center.y), new Vector2(t, view.height + 2f * t));
        Place(right,  new Vector2(view.xMax + t * 0.5f, view.center.y), new Vector2(t, view.height + 2f * t));
        Place(bottom, new Vector2(view.center.x, view.yMin - t * 0.5f), new Vector2(view.width + 2f * t, t));
        Place(top,    new Vector2(view.center.x, view.yMax + t * 0.5f), new Vector2(view.width + 2f * t, t));
    }

    static void Place(BoxCollider2D wall, Vector2 center, Vector2 size)
    {
        wall.transform.position = center;
        wall.size = size;
    }
}
