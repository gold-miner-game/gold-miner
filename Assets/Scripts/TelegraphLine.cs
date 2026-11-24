using System.Collections.Generic;
using UnityEngine;

// Owner: C (Miner AI)
public class TelegraphLine : MonoBehaviour
{
    public GameObject dotPrefab;     // TelegraphDot prefab
    public float spacing = 0.35f;
    public int maxDots = 60;

    private readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();

    // Art: the aim is a running stitch of twisted thread (Resources/Art/Fx/Stitch) instead of dots, in
    // the same colour (yellow = treasure, red = you). The whole line shows at once, as before; the
    // stitches only march toward the target on the pose clock, so the line also says which way it will go.
    // Without the art, the dots are drawn as before.
    static readonly Vector2 StitchSize = new Vector2(0.21f, 0.09f);   // world units: thread length and thickness
    const int MarchSteps = 3;   // one stitch spacing every 3 poses
    Sprite stitch;
    bool shown;
    Vector2 shownFrom, shownDir;
    float shownLength;
    Color shownColor;
    // stop-motion mode: the aim is held between poses (the gameplay aim keeps tracking underneath)
    int heldFrame = int.MinValue;
    Vector2 heldFrom, heldDir;
    float heldLength;

    void Awake()
    {
        stitch = ArtLibrary.Get("Fx/Stitch");
        for (int i = 0; i < maxDots; i++)
        {
            GameObject dot = Instantiate(dotPrefab, transform);
            dot.SetActive(false);
            SpriteRenderer look = dot.GetComponent<SpriteRenderer>();
            if (stitch != null && look != null)
            {
                look.sprite = stitch;
                Vector2 art = stitch.bounds.size;
                dot.transform.localScale = new Vector3(StitchSize.x / art.x, StitchSize.y / art.y, 1f);
            }
            dots.Add(look);
        }
    }

    void LateUpdate()
    {
        if (!shown || stitch == null || spacing <= 0f) return;
        if (!StopMotion.Enabled || StopMotion.Frame != heldFrame)
        {
            heldFrame = StopMotion.Frame;
            heldFrom = shownFrom; heldDir = shownDir; heldLength = shownLength;
        }
        float phase = (StopMotion.Frame % MarchSteps) / (float)MarchSteps;
        int count = Mathf.Min(maxDots, Mathf.FloorToInt(heldLength / spacing - phase) + 1);
        float angle = Mathf.Atan2(heldDir.y, heldDir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < dots.Count; i++)
        {
            bool on = i < count;
            if (dots[i].gameObject.activeSelf != on) dots[i].gameObject.SetActive(on);
            if (!on) continue;
            dots[i].transform.SetPositionAndRotation(heldFrom + heldDir * (spacing * (i + phase)), Quaternion.Euler(0f, 0f, angle));
            dots[i].color = shownColor;
        }
    }

    public void Show(Vector2 from, Vector2 to, Color color)
    {
        if (!shown) heldFrame = int.MinValue;   // a fresh aim shows at once, never the last one held
        shown = true;
        shownFrom = from;
        shownDir = (to - from).normalized;
        shownLength = Vector2.Distance(from, to);
        shownColor = color;

        Vector2 dir = (to - from).normalized;
        float length = Vector2.Distance(from, to);
        int count = Mathf.Min(maxDots, Mathf.FloorToInt(length / spacing) + 1);

        for (int i = 0; i < dots.Count; i++)
        {
            bool on = i < count;
            dots[i].gameObject.SetActive(on);
            if (!on) continue;

            dots[i].transform.position = from + dir * (spacing * i);
            dots[i].color = color;
        }
    }

    public void Hide()
    {
        shown = false;
        foreach (SpriteRenderer dot in dots) dot.gameObject.SetActive(false);
    }
}
