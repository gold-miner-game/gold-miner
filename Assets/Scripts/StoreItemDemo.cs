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
    public const float RevealDuration = 0.25f;

    RawImage screen;
    Image revealMask;
    Texture2D recording;
    float started;
    string current;
    RectTransform revealRoot;
    Vector3 revealScale;
    float revealStarted;
    bool revealing;

    public void Play(string id, RectTransform revealPanel = null)
    {
        ResetReveal();
        if (screen == null)
        {
            screen = gameObject.AddComponent<RawImage>();
            screen.raycastTarget = false;
            var mask = new GameObject("RevealMask", typeof(RectTransform));
            mask.transform.SetParent(transform, false);
            revealMask = mask.AddComponent<Image>();
            revealMask.raycastTarget = false;
            RectTransform rect = revealMask.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        if (current != id)
        {
            Release();
            recording = Resources.Load<Texture2D>(id.Contains("/") ? id : "StoreDemos/" + id);
            current = id;
        }
        screen.texture = recording;
        screen.enabled = recording != null;
        revealMask.color = Color.black;
        revealMask.enabled = revealPanel != null;
        started = Time.unscaledTime;
        DrawFrame(0);
        if (revealPanel != null)
        {
            revealRoot = revealPanel;
            revealScale = revealRoot.localScale;
            revealStarted = Time.unscaledTime;
            revealing = true;
            revealRoot.localScale = new Vector3(revealScale.x, 0f, revealScale.z);
        }
    }

    void Update()
    {
        if (revealing)
        {
            float progress = Mathf.Clamp01((Time.unscaledTime - revealStarted) / RevealDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            revealRoot.localScale = new Vector3(revealScale.x,
                revealScale.y * eased, revealScale.z);
            revealMask.color = new Color(0f, 0f, 0f, 1f - eased);
            if (progress < 1f) return; // Fade in the still first frame before starting playback.
            ResetReveal();
            started = Time.unscaledTime;
            screen.enabled = recording != null;
        }
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
        ResetReveal();
        if (screen != null) screen.texture = null;
        if (recording != null) Resources.UnloadAsset(recording);
        recording = null;
        current = null;
    }

    void ResetReveal()
    {
        if (revealRoot != null) revealRoot.localScale = revealScale;
        if (revealMask != null) revealMask.enabled = false;
        revealRoot = null;
        revealing = false;
    }

    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
}
