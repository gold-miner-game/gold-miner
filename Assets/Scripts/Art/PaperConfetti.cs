using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Art: a won round rains cut-paper confetti over the whole screen, result screen included. It lives on
// its own overlay canvas (just under the scene-change curtain) so the result dimmer doesn't hide it, and
// runs on unscaled time posed on twos, so it still falls while a result screen pauses the game.
// Read-only: it only watches GameManager.PlayerWon. No raycasts, so it never blocks a click.
public class PaperConfetti : MonoBehaviour
{
    const int Pieces = 70;
    static readonly Color[] Paper =
    {
        new Color(1f, 0.84f, 0.3f), new Color(0.98f, 0.95f, 0.86f), new Color(0.95f, 0.5f, 0.6f),
        new Color(0.4f, 0.75f, 0.72f), new Color(0.98f, 0.66f, 0.2f),
    };

    class Piece { public Image look; public Vector2 at, velocity; public float spin, angle, life, age, size, sway; }

    static PaperConfetti instance;
    readonly List<Piece> live = new List<Piece>();
    Canvas canvas;
    Sprite flake;
    bool sawWin;
    int shownFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        Sprite art = ArtLibrary.Get("Fx/Flake");
        if (art == null) return;
        var go = new GameObject("Paper Confetti");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperConfetti>();
        instance.flake = art;
        instance.canvas = go.AddComponent<Canvas>();
        instance.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        instance.canvas.sortingOrder = 31000;   // over the game's UI, under PaperCurtain (32000)
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        bool won = gm != null && gm.PlayerWon && !gm.IsPlaying;
        if (won && !sawWin) Celebrate();
        sawWin = won;
        if (live.Count == 0) return;

        float dt = Time.unscaledDeltaTime;
        float g = Screen.height * 0.55f;   // gravity, in pixels per second squared
        for (int i = live.Count - 1; i >= 0; i--)
        {
            Piece p = live[i];
            p.age += dt;
            if (p.age >= p.life) { Destroy(p.look.gameObject); live.RemoveAt(i); continue; }
            // paper falls slowly and sways: drag pulls it to a gentle terminal speed
            p.velocity += new Vector2(Mathf.Sin(p.age * 3f + p.sway) * g * 0.6f, -g) * dt;
            p.velocity *= Mathf.Exp(-1.8f * dt);
            p.at += p.velocity * dt;
            p.angle += p.spin * dt;
        }
        int frame = Mathf.FloorToInt(Time.unscaledTime * StopMotion.Twos);
        if (frame == shownFrame) return;   // hold the pose until the next twos
        shownFrame = frame;
        foreach (Piece p in live) Pose(p);
    }

    void Celebrate()
    {
        float w = Screen.width, h = Screen.height;
        for (int i = 0; i < Pieces; i++)
        {
            var go = new GameObject("Confetti", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var look = go.GetComponent<Image>();
            look.sprite = flake;
            look.raycastTarget = false;
            look.rectTransform.anchorMin = look.rectTransform.anchorMax = Vector2.zero;   // positions in screen pixels
            var p = new Piece
            {
                look = look,
                at = new Vector2(Random.Range(0f, w), h + Random.Range(0.02f, 0.4f) * h),
                velocity = new Vector2(Random.Range(-0.12f, 0.12f) * h, Random.Range(-0.4f, -0.05f) * h),
                spin = Random.Range(-540f, 540f), angle = Random.value * 360f,
                life = Random.Range(2.6f, 3.6f), size = Random.Range(0.022f, 0.036f) * h, sway = Random.value * 6f,
            };
            look.color = Paper[Random.Range(0, Paper.Length)];
            live.Add(p);
            Pose(p);
        }
    }

    static void Pose(Piece p)
    {
        RectTransform t = p.look.rectTransform;
        float k = Mathf.Clamp01(p.age / p.life);
        float flutter = Mathf.Max(0.25f, Mathf.Abs(Mathf.Cos(p.age * 14f + p.spin)));   // edge-on and back
        t.anchoredPosition = p.at;
        t.localRotation = Quaternion.Euler(0f, 0f, p.angle);
        t.sizeDelta = new Vector2(p.size * flutter, p.size);
        Color c = p.look.color;
        c.a = 1f - k * k;
        p.look.color = c;
    }
}
