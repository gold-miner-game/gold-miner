using UnityEngine;

// Owner: Butian (HUD)
// The player's Lives as a bar over their head: one segment per Life, red while it's left, dark once
// lost. Three Lives = three segments, and every hit (GameManager.PlayerHit) empties one. A Life picked
// up fills one again (or adds a segment past the start). Hidden where Lives don't count: no round
// (the character pick), no Lives (one hit ends it), or so many they can't run out (the block
// tutorial's 999). PlayerCharacter adds it; nothing needs it in a scene.
public class PlayerLifeBar : MonoBehaviour
{
    [Tooltip("More Lives than this (e.g. the block tutorial's 999): no bar")]
    public int maxSegments = 10;
    public float segmentWidth = 0.22f;
    public float segmentHeight = 0.12f;
    public float gap = 0.05f;
    [Tooltip("Above the top of the player's body, in world units")]
    public float lift = 0.22f;
    public Color full = new Color(0.92f, 0.16f, 0.2f);
    // Brace bonus pips: orange (#FF8C14), same cue as the sidebar LIFE +N.
    public Color braceFull = new Color(1f, 0.549f, 0.078f, 1f);
    public Color empty = new Color(0.12f, 0.12f, 0.14f, 0.85f);
    public Color frame = new Color(0f, 0f, 0f, 0.7f);

    Transform bar;
    SpriteRenderer back;
    SpriteRenderer[] segments = new SpriteRenderer[0];
    int most;   // the most Lives seen this round: the bar's length
    GameManager seenFor;
    Collider2D body;

    static Sprite pixel;

    void LateUpdate()
    {
        GameManager gm = GameManager.Instance;
        if (gm != seenFor) { seenFor = gm; most = gm != null ? Mathf.Max(gm.Lives, gm.startingLives) : 0; }

        int lives = gm != null ? gm.Lives : 0;
        int bonus = gm != null ? gm.BonusLives : 0;
        if (bonus <= 0) bonus = LevelLoader.PendingBraceBonusLives();
        // Armed Brace on the hub may not be in gm.Lives yet — grow the bar so the orange pip appears.
        int filled = lives;
        if (bonus > 0 && gm != null && gm.BonusLives <= 0) filled = lives + bonus;

        most = Mathf.Max(most, filled, gm != null ? gm.startingLives : 0);
        bool show = gm != null && most > 0 && most <= maxSegments && isActiveAndEnabled;
        if (!show) { if (bar != null) bar.gameObject.SetActive(false); return; }

        if (bar == null) Make();
        bar.gameObject.SetActive(true);
        if (segments.Length != most) Build(most);

        if (body == null) body = GetComponent<Collider2D>();
        Vector2 at = body != null ? new Vector2(body.bounds.center.x, body.bounds.max.y) : (Vector2)transform.position + Vector2.up * 0.5f;
        bar.position = new Vector3(at.x, at.y + lift, 0f);

        // Rightmost filled segments are Brace bonus Lives (orange); the rest stay bright red.
        bonus = Mathf.Clamp(bonus, 0, filled);
        int normalFilled = filled - bonus;
        for (int i = 0; i < segments.Length; i++)
        {
            if (i >= filled) segments[i].color = empty;
            else if (i >= normalFilled) segments[i].color = braceFull;
            else segments[i].color = full;
        }
    }

    void Make()
    {
        if (pixel == null)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        bar = new GameObject(name + " Lives").transform;   // not a child: the player's flip and scale leave it alone
        back = Part("Frame", frame, 62);
    }

    void Build(int count)
    {
        foreach (SpriteRenderer s in segments) if (s != null) Destroy(s.gameObject);
        segments = new SpriteRenderer[count];
        float width = count * segmentWidth + (count - 1) * gap;
        back.transform.localScale = new Vector3(width + gap * 2f, segmentHeight + gap * 2f, 1f);
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer s = Part("Life " + (i + 1), full, 63);
            s.transform.localScale = new Vector3(segmentWidth, segmentHeight, 1f);
            s.transform.localPosition = new Vector3(-width * 0.5f + segmentWidth * 0.5f + i * (segmentWidth + gap), 0f, 0f);
            segments[i] = s;
        }
    }

    SpriteRenderer Part(string partName, Color color, int order)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(bar, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = pixel;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    void OnDisable() { if (bar != null) bar.gameObject.SetActive(false); }
    void OnDestroy() { if (bar != null) Destroy(bar.gameObject); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pixel = null; }   // Play without a domain reload keeps statics
}
