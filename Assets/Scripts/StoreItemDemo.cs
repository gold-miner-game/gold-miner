using UnityEngine;
using UnityEngine.UI;

// Plays frames recorded from the real prefabs and gameplay scripts by StoreDemoCaptureDriver.
// No gameplay objects or global game state are created while browsing the shop.
public class StoreItemDemo : MonoBehaviour
{
    public const int Columns = 10;
    public const int Rows = 9;
    public const int Frames = 90;
    public const float Duration = 4.5f;

    RawImage screen;
    Texture2D recording;
    float started;
    string current;

    public void Play(string id)
    {
        if (screen == null)
        {
            screen = gameObject.AddComponent<RawImage>();
            screen.raycastTarget = false;
        }
        if (current != id)
        {
            Release();
            recording = Resources.Load<Texture2D>("StoreDemos/" + id);
            current = id;
        }
        screen.texture = recording;
        screen.enabled = recording != null;
        started = Time.unscaledTime;
        DrawFrame(0);
    }

    void Update()
    {
        if (recording == null) return;
        int frame = (int)((Time.unscaledTime - started) % Duration / Duration * Frames);
        DrawFrame(frame);
    }

    void DrawFrame(int frame)
    {
        screen.uvRect = new Rect((frame % Columns) / (float)Columns,
            1f - (frame / Columns + 1f) / Rows, 1f / Columns, 1f / Rows);
    }

    void Release()
    {
        if (screen != null) screen.texture = null;
        if (recording != null) Resources.UnloadAsset(recording);
        recording = null;
        current = null;
    }

    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
}
