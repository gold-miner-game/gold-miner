using UnityEngine;

// Owner: Butian (HUD)
// The player's Lives as a bar over their head: one segment per Life, red while it's left, dark once
// lost. Three Lives = three segments, and every hit (GameManager.PlayerHit) empties one. A Life picked
// up fills one again (or adds a segment past the start). Hidden where Lives don't count: no round
// (the character pick), no Lives (one hit ends it), or so many they can't run out (the block
// tutorial's 999). PlayerCharacter adds it; nothing needs it in a scene.
[DefaultExecutionOrder(100)]
public class PlayerLifeBar : MonoBehaviour
{
    [Tooltip("More Lives than this (e.g. the block tutorial's 999): no bar")]
    public int maxSegments = 10;
    public float segmentWidth = 0.22f;
    public float segmentHeight = 0.12f;
    public float gap = 0.05f;
    [Tooltip("Above the top of the player's body, in world units")]
    public float lift = 0.22f;
    SpriteRenderer helmet;   // art: the shield's paper box, looked up once
    bool helmetLooked;
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

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm != seenFor) { seenFor = gm; most = gm != null ? Mathf.Max(gm.Lives, gm.startingLives) : 0; }

        int lives = gm != null ? gm.Lives : 0;
        int bonus = gm != null ? gm.BonusLives : 0;
        // Only the hub previews a pending pip. In a battle, zero means it was spent.
        if (bonus <= 0 && (gm == null || LevelLoader.Tutorial)) bonus = LevelLoader.PendingBraceBonusLives();
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
        Vector2 at = (Vector2)transform.position + Vector2.up * 0.5f;
        if (body != null)
        {
            // Bounds belong to the physics pose. Apply only their head offset to the rendered
            // player pose, so Rigidbody2D interpolation cannot separate the pig and its bar.
            Rigidbody2D rigidbody = body.attachedRigidbody;
            Vector2 physicsPosition = rigidbody != null ? rigidbody.position : (Vector2)transform.position;
            Vector2 headOffset = new Vector2(body.bounds.center.x, body.bounds.max.y) - physicsPosition;
            at = (Vector2)transform.position + headOffset;
        }
        float y = at.y + lift;
        // Art: while the paper box (the shield) is worn, the pips rest just above it instead of on it.
        if (!helmetLooked) { helmetLooked = true; foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(true)) if (r.name == "ShieldBubble") { helmet = r; break; } }
        if (helmet != null && helmet.enabled && helmet.gameObject.activeInHierarchy) y = Mathf.Max(y, helmet.bounds.max.y + 0.18f);   // clear of the box
        bar.position = new Vector3(at.x, y, 0f);

        // Rightmost filled segments are Brace bonus Lives (orange); the rest stay bright red.
        bonus = Mathf.Clamp(bonus, 0, filled);
        int normalFilled = filled - bonus;
        for (int i = 0; i < segments.Length; i++)
        {
            Color c;
            if (i >= filled) c = empty;
            else if (i >= normalFilled) c = braceFull;
            else c = full;
            // Art: a pip that empties or fills pops (big, smaller, settled) on twos, like a stamped label.
            if (primed && segments[i].color != c && pipArt != null) popAt[i] = StopMotion.Time;
            segments[i].color = c;
            if (pipArt != null) segments[i].transform.localScale = pipScale * PopScale(popAt[i]);
        }
        primed = true;
    }

    // Art: felt pips sewn on a dark felt strip (Resources/Art/Fx/LifePip, Slices/FeltStrip). The pip art
    // is light felt, so the colours above still say red Life / orange Brace / dark lost. Without the art,
    // the old flat bar is drawn.
    static Sprite pipArt, stripArt;
    static readonly float[] Pop = { 1.35f, 1.12f, 0.96f, 1f };
    float[] popAt = new float[0];
    Vector3 pipScale = Vector3.one;
    bool primed;   // false right after Build: the first colours are a setup, not a change

    static float PopScale(float at)
    {
        int step = Mathf.FloorToInt((StopMotion.Time - at) * StopMotion.Pose + 0.001f);
        return step >= 0 && step < Pop.Length ? Pop[step] : 1f;
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
        if (pipArt == null) { pipArt = ArtLibrary.Get("Fx/LifePip"); stripArt = ArtLibrary.Get("Slices/FeltStrip"); }
        bar = new GameObject(name + " Lives").transform;   // not a child: the player's flip and scale leave it alone
        bar.gameObject.AddComponent<FrostIgnore2D>();
        back = Part("Frame", frame, 62);
        if (stripArt != null && pipArt != null)
        {
            back.sprite = stripArt;
            back.drawMode = SpriteDrawMode.Sliced;
        }
    }

    void Build(int count)
    {
        foreach (SpriteRenderer s in segments) if (s != null) Destroy(s.gameObject);
        segments = new SpriteRenderer[count];
        popAt = new float[count];
        for (int i = 0; i < count; i++) popAt[i] = -100f;
        primed = false;
        float width = count * segmentWidth + (count - 1) * gap;
        if (back.drawMode == SpriteDrawMode.Sliced)
        {
            back.transform.localScale = Vector3.one;
            back.size = new Vector2(width + gap * 2f, segmentHeight + gap * 2f);
        }
        else back.transform.localScale = new Vector3(width + gap * 2f, segmentHeight + gap * 2f, 1f);
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer s = Part("Life " + (i + 1), full, 63);
            if (pipArt != null)
            {
                s.sprite = pipArt;
                Vector2 art = pipArt.bounds.size;   // the art's own size; fitted to the segment fields
                pipScale = new Vector3(segmentWidth / art.x, segmentHeight / art.y, 1f);
                s.transform.localScale = pipScale;
            }
            else s.transform.localScale = new Vector3(segmentWidth, segmentHeight, 1f);
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
    static void ResetStatics() { pixel = null; pipArt = null; stripArt = null; }   // Play without a domain reload keeps statics
}
