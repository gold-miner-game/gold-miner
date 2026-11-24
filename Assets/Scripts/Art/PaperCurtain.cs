using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Art: scene changes reveal like a stop-motion set. A felt sheet with a torn bottom edge covers the
// new scene, holds a beat, dips a little (anticipation), then is pulled up and away, accelerating
// like a hand pulling paper (StopMotion.Pull), re-posed on twos. Never blocks input (no raycasts),
// never delays loading: the scene is already running underneath. Unscaled time, so it also plays
// over a paused or slowed game.
public class PaperCurtain : MonoBehaviour
{
    const float Duration = 0.5f;

    static PaperCurtain instance;
    RectTransform sheet;
    CanvasGroup group;
    float startedAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        Sprite floor = ArtLibrary.Get("MineFloor"), edge = ArtLibrary.Get("Fx/TornEdge");
        if (floor == null || edge == null) return;

        var go = new GameObject("Paper Curtain");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperCurtain>();
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        instance.group = go.AddComponent<CanvasGroup>();
        instance.group.blocksRaycasts = false;
        instance.group.interactable = false;

        var sheetGo = new GameObject("Sheet", typeof(RectTransform));
        instance.sheet = (RectTransform)sheetGo.transform;
        instance.sheet.SetParent(go.transform, false);
        instance.sheet.anchorMin = Vector2.zero;
        instance.sheet.anchorMax = Vector2.one;
        instance.sheet.offsetMin = instance.sheet.offsetMax = Vector2.zero;
        Image body = sheetGo.AddComponent<Image>();
        body.sprite = floor;
        body.type = Image.Type.Tiled;
        body.pixelsPerUnitMultiplier = 0.6f;
        body.color = new Color(0.8f, 0.76f, 0.72f);
        body.raycastTarget = false;

        var edgeGo = new GameObject("TornEdge", typeof(RectTransform));
        var edgeRect = (RectTransform)edgeGo.transform;
        edgeRect.SetParent(instance.sheet, false);
        edgeRect.anchorMin = new Vector2(0f, 0f);
        edgeRect.anchorMax = new Vector2(1f, 0f);
        edgeRect.pivot = new Vector2(0.5f, 1f);
        edgeRect.sizeDelta = new Vector2(0f, 70f);
        Image torn = edgeGo.AddComponent<Image>();
        torn.sprite = edge;
        torn.color = body.color;
        torn.raycastTarget = false;

        SceneManager.sceneLoaded += (s, m) => { if (instance != null && m == LoadSceneMode.Single) instance.Play(); };
        instance.Play();
    }

    void Play()
    {
        startedAt = Time.unscaledTime;
        group.alpha = 1f;
        sheet.anchoredPosition = Vector2.zero;
        sheet.localRotation = Quaternion.identity;
    }

    void Update()
    {
        if (startedAt < 0f) return;
        float t = StopMotion.UnscaledSnap(startedAt) / Duration;   // posed on twos
        if (t >= 1f)
        {
            startedAt = -1f;
            group.alpha = 0f;
            return;
        }
        float height = ((RectTransform)transform).rect.height + 90f;
        sheet.anchoredPosition = new Vector2(0f, StopMotion.Pull(t) * height);
        sheet.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 1.8f);   // a hand tilts it
    }
}
