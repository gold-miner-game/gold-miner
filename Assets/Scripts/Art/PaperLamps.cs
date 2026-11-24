using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: lamplight in the mine, like the title cover. Warm pools of light on the FLOOR only:
//   - (no lamp follows the player: the character stands in the same shade as every other cut-out; playtest)
//   - the mine lamp: one large, very soft glow from the top-left of the view (the cover's light)
//   - lanterns: every set-dressing lantern (PaperSet) casts its own small pool
// Each pool is a soft disc (Resources/Art/Fx/LampPool) drawn just above the floor and the props and
// below every game piece, so light can only brighten the ground: it never hides or tints a hook, rock,
// treasure or character, and nothing gets darker. Flames flicker on the boil clock (6 poses a second), like the
// boil: held poses, not jitter. Read-only: it follows positions, nothing else.
public class PaperLamps : MonoBehaviour
{
    static readonly Color Warm = new Color(1f, 0.74f, 0.38f);
    const int Order = short.MinValue + 3;   // floor MinValue, strata +1, set props +2: light falls on all of them
    static PaperLamps instance;

    class Lamp { public SpriteRenderer look; public Transform follow; public Vector3 offset; public float size, alpha; public int seed; }

    readonly List<Lamp> lamps = new List<Lamp>();
    Sprite pool;
    Lamp minerLamp, mineLamp;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Lamps");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperLamps>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null && m == LoadSceneMode.Single) instance.Clear(); };
    }

    void Start()
    {
        pool = ArtLibrary.Get("Fx/LampPool");
        if (pool == null) enabled = false;
    }

    void Clear()
    {
        foreach (Lamp l in lamps) if (l.look != null) Destroy(l.look.gameObject);
        lamps.Clear();
        minerLamp = mineLamp = null;
        nextScan = 0f;
    }

    // PaperSet calls this for each lantern it lays on the floor
    public static void Lantern(Transform prop)
    {
        if (instance == null || instance.pool == null || prop == null) return;
        instance.Add(prop, Vector3.zero, 2.6f * prop.lossyScale.x, 0.2f, prop.GetInstanceID());
    }

    Lamp Add(Transform follow, Vector3 offset, float size, float alpha, int seed)
    {
        var go = new GameObject("Lamp Pool");
        go.transform.SetParent(transform, false);
        var look = go.AddComponent<SpriteRenderer>();
        look.sprite = pool;
        look.sortingOrder = Order;
        var lamp = new Lamp { look = look, follow = follow, offset = offset, size = size, alpha = alpha, seed = seed };
        lamps.Add(lamp);
        return lamp;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        bool scene = cam != null && cam.orthographic && GameManager.Instance != null;   // gameplay scenes only
        if (scene && Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            PlayerCharacter player = FindAnyObjectByType<PlayerCharacter>();
            if (player != null && mineLamp == null) mineLamp = Add(cam.transform, Vector3.zero, 1f, 0.11f, 2);
        }

        int frame = StopMotion.BoilFrame;   // flicker on threes
        for (int i = lamps.Count - 1; i >= 0; i--)
        {
            Lamp l = lamps[i];
            if (l.follow == null || l.look == null)
            {
                if (l.look != null) Destroy(l.look.gameObject);
                if (l == minerLamp) minerLamp = null;
                if (l == mineLamp) mineLamp = null;
                lamps.RemoveAt(i);
                continue;
            }
            // a flame's held poses: three brightness / size steps, each lamp on its own phase
            int pose = Mathf.Abs(frame + l.seed) % 3;
            float flame = pose == 0 ? 1f : pose == 1 ? 0.94f : 1.04f;
            // Each lamp also breathes, glowing and dimming on its own period (1.6-3.6 s) and phase, so
            // lamps never pulse in step. Sampled on threes like the flame: held poses, not a smooth fade.
            uint hash = unchecked((uint)(l.seed * 2654435 + 977));   // unsigned: no Abs(int.MinValue) overflow
            float period = 1.6f + (hash % 1000) / 500f;
            float phase = (hash / 1000 % 628) / 100f;
            float breath = 0.8f + 0.2f * Mathf.Sin(frame / StopMotion.Boil * 2f * Mathf.PI / period + phase);
            float flicker = flame * breath;
            Vector3 at = l.follow.position + l.offset;
            float size = l.size;
            if (l == mineLamp && cam != null)
            {
                // the cover's light: up and left of the view, wide enough to wash its quarter of the floor
                float h = cam.orthographicSize, w = h * cam.aspect;
                at = cam.transform.position + new Vector3(-w * 0.75f, h * 0.8f, 0f);
                size = h * 3.2f;
            }
            l.look.transform.position = new Vector3(at.x, at.y, 0f);
            l.look.transform.localScale = Vector3.one * size * (0.97f + 0.03f * flicker);
            l.look.color = new Color(Warm.r, Warm.g, Warm.b, l.alpha * flicker);
            l.look.enabled = scene;
        }
    }
}
