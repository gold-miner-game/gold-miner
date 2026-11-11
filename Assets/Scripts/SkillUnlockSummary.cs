using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Owner: Butian (skill unlocks)
// The summary after winning a level whose file says "unlock_block": 1 (Level 2 on the world map). It
// hands Mr. Hyde his block for the levels after it:
//   the screen darkens over the level, and a big copy of the pig shows with a few words about the skill;
//   a red hook shoots at him and stops just short; "Press K to block" pulses until K is pressed;
//   then the pig turns blue and bobs, the block's white flash shows on his head and the hook snaps
//   back (the Select scene's Block preview, SkillPreview), and the unlock is saved.
// Until then CanBlock stays off in world map battles (MrHydeController). It only plays while the block
// is still locked; ResetUnlock (or resetting all progress) makes it play again.
// LevelLoader adds it; GameFlow and LevelLoader wait while Playing before leaving the level.
public class SkillUnlockSummary : MonoBehaviour
{
    public const string BlockUnlockId = "skill_block";   // saved with the cleared nodes (ProgressStore)
    public static bool BlockUnlocked => ProgressStore.IsCompleted(BlockUnlockId);
    public static bool Playing { get; private set; }

    public PlayerCharacter player;
    [Tooltip("Seconds the win result shows before the summary starts")]
    public float startDelay = 1.5f;
    public Color blockTint = new Color(0.45f, 0.75f, 1f);
    public Color hookColor = Color.red;
    public Color ropeColor = new Color(0.3f, 0.22f, 0.15f);

    const int Order = 1000;   // over the level and its sprites
    static Sprite pixel;

    bool started;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Playing = false; }

    public static void ResetUnlock()
    {
        ProgressStore.ClearCompleted(BlockUnlockId);
    }

    void Update()
    {
        if (started) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.IsPlaying || !gm.PlayerWon) return;
        started = true;
        if (BlockUnlocked) return;   // already handed over on an earlier win
        Playing = true;
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (started) Playing = false;
        RestoreCanvases();
    }

    IEnumerator Run()
    {
        yield return Wait(startDelay);

        Camera cam = Camera.main;
        Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
        c.z = 0f;
        float h = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float w = h * (cam != null ? cam.aspect : 16f / 9f);

        HideCanvases();
        var root = new GameObject("Skill Unlock Summary").transform;
        root.position = c;

        SpriteRenderer dark = Box(root, "Dark", c, new Vector2(w * 2.4f, h * 2.4f), new Color(0f, 0f, 0f, 0f), Order);

        // The pig, big, a little below the middle
        Transform pig = CopyPig(root, c + new Vector3(0f, -0.12f * h, 0f), h * 0.42f, out SpriteRenderer pigBody, out Bounds pigBox);
        Vector3 pigRest = pig != null ? pig.localScale : Vector3.one;

        TextMeshPro title = Text(root, "NEW SKILL: BLOCK", c + new Vector3(0f, 0.74f * h, 0f), h * 1.5f, new Color(1f, 0.85f, 0.3f), w * 1.8f);
        TextMeshPro intro = Text(root, "Mr. Hyde can block. A hook that reaches him while he blocks\nbounces back empty, and gold on a passing hook is knocked off.",
                                 c + new Vector3(0f, 0.5f * h, 0f), h * 0.62f, Color.white, w * 1.8f);
        TextMeshPro prompt = Text(root, "Press  <color=#FFD84D>K</color>  to block", c + new Vector3(0f, -0.66f * h, 0f), h * 0.95f, Color.white, w * 1.8f);
        SetAlpha(prompt, 0f);

        // Fade in
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            float a = t / 0.4f;
            dark.color = new Color(0f, 0f, 0f, 0.88f * a);
            SetAlpha(title, a); SetAlpha(intro, a);
            SetPigAlpha(pig, a);
            yield return null;
        }
        dark.color = new Color(0f, 0f, 0f, 0.88f);
        SetAlpha(title, 1f); SetAlpha(intro, 1f); SetPigAlpha(pig, 1f);
        yield return Wait(0.4f);

        // The hook shoots in from the top right and stops just short of the pig's head
        Vector2 head = new Vector2(pigBox.center.x, pigBox.max.y - pigBox.size.y * 0.15f);
        Vector2 from = new Vector2(c.x + w * 1.05f, c.y + h * 0.95f);
        Vector2 dir = (head - from).normalized;
        Vector2 stop = head - dir * (pigBox.size.x * 0.55f + 0.35f);
        LineRenderer rope = Line(root, "Rope", ropeColor, 0.06f, Order + 5, 2);
        LineRenderer claw = Line(root, "Claw", hookColor, 0.09f, Order + 6, 3);
        for (float t = 0f; t < 0.55f; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / 0.55f, 3f);   // fast, then easing in
            DrawHook(rope, claw, from, Vector2.Lerp(from, stop, k), dir, 0.35f);
            yield return null;
        }

        // Wait for K, the hook trembling at the pig
        float since = 0f;
        while (Keyboard.current == null || !Keyboard.current.kKey.wasPressedThisFrame)
        {
            since += Time.unscaledDeltaTime;
            Vector2 shake = new Vector2(Mathf.Sin(since * 47f), Mathf.Cos(since * 39f)) * 0.025f;
            DrawHook(rope, claw, from, stop + shake, dir, 0.35f);
            SetAlpha(prompt, Mathf.Min(1f, since * 3f) * (0.65f + 0.35f * Mathf.Sin(since * 6f)));
            if (prompt != null) prompt.transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(since * 6f));
            yield return null;
        }

        // Blocked: the pig turns blue and bobs, the flash shows on his head, the hook snaps back
        ProgressStore.MarkCompleted(BlockUnlockId);
        TutorialEvents.Fire("block_unlocked");
        Color bodyColor = pigBody != null ? pigBody.color : Color.white;
        if (pigBody != null) pigBody.color = blockTint;
        BlockFlash.Show(head, -dir, pigBox.size.x * 0.9f, Order + 30);
        prompt.text = "Blocked!";
        prompt.transform.localScale = Vector3.one;
        SetAlpha(prompt, 1f);
        Vector2 headAtHit = stop;
        for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
        {
            if (pig != null)
            {
                float bump = 1f + 0.06f * Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI);
                pig.localScale = new Vector3(pigRest.x * bump, pigRest.y / bump, pigRest.z);
            }
            float back = Mathf.Clamp01(t / 0.35f);
            // A small knock away from the pig, then reeled out fast
            Vector2 knock = headAtHit - dir * 0.4f * Mathf.Sin(Mathf.Clamp01(t / 0.12f) * Mathf.PI * 0.5f);
            DrawHook(rope, claw, from, Vector2.Lerp(knock, from, back * back), dir, 0.35f * (1f - back * 0.5f));
            yield return null;
        }
        if (pig != null) pig.localScale = pigRest;
        if (pigBody != null) pigBody.color = bodyColor;
        rope.enabled = claw.enabled = false;

        prompt.text = "The block is yours from the next level on:  <color=#FFD84D>[K]</color>";
        prompt.fontSize = h * 0.7f;
        yield return Wait(2.4f);

        // Fade out, then the level's result comes back and the game moves on
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            float a = 1f - t / 0.4f;
            dark.color = new Color(0f, 0f, 0f, 0.88f * a);
            SetAlpha(title, a); SetAlpha(intro, a); SetAlpha(prompt, a);
            SetPigAlpha(pig, a);
            yield return null;
        }
        Destroy(root.gameObject);
        RestoreCanvases();
        Playing = false;
    }

    static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    // The HUD (YOU WIN, the score) is drawn over every sprite: hide it while the summary shows
    void HideCanvases()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.enabled || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            canvas.enabled = false;
            hiddenCanvases.Add(canvas);
        }
    }

    void RestoreCanvases()
    {
        foreach (Canvas canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
        hiddenCanvases.Clear();
    }

    // A still copy of the pig's drawing (sprites only, no scripts), `height` world units tall at `at`.
    // `body` is its biggest part, the one that turns blue
    Transform CopyPig(Transform parent, Vector3 at, float height, out SpriteRenderer body, out Bounds box)
    {
        body = null;
        box = new Bounds(at, Vector3.one);
        if (player == null) return null;
        var parts = new List<SpriteRenderer>();
        Bounds src = new Bounds();
        bool any = false;
        foreach (SpriteRenderer r in player.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!r.enabled || r.sprite == null || r.color.a < 0.05f || !r.gameObject.activeInHierarchy) continue;
            if (r.maskInteraction != SpriteMaskInteraction.None) continue;   // the digest tint and such overlays
            parts.Add(r);
            if (!any) { src = r.bounds; any = true; } else src.Encapsulate(r.bounds);
        }
        if (!any) return null;

        float scale = height / Mathf.Max(0.01f, src.size.y);
        var pig = new GameObject("Pig").transform;
        pig.SetParent(parent, false);
        pig.position = at;
        var group = pig.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
        group.sortingOrder = Order + 10;
        float biggest = -1f;
        foreach (SpriteRenderer r in parts)
        {
            var go = new GameObject(r.name);
            go.transform.SetParent(pig, false);
            go.transform.localPosition = (r.transform.position - src.center) * scale;
            go.transform.rotation = r.transform.rotation;
            go.transform.localScale = r.transform.lossyScale * scale;
            SpriteRenderer copy = go.AddComponent<SpriteRenderer>();
            copy.sprite = r.sprite;
            copy.color = r.color;
            copy.flipX = r.flipX;
            copy.flipY = r.flipY;
            copy.sortingOrder = r.sortingOrder;
            float size = r.bounds.size.x * r.bounds.size.y;
            if (size > biggest) { biggest = size; body = copy; }
        }
        box = new Bounds(at, src.size * scale);
        return pig;
    }

    static void SetPigAlpha(Transform pig, float a)
    {
        if (pig == null) return;
        foreach (SpriteRenderer r in pig.GetComponentsInChildren<SpriteRenderer>())
        {
            Color c = r.color; c.a = a; r.color = c;
        }
    }

    static SpriteRenderer Box(Transform parent, string name, Vector3 at, Vector2 size, Color color, int order)
    {
        if (pixel == null)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = pixel;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static TextMeshPro Text(Transform parent, string words, Vector3 at, float size, Color color, float width)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = words;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(width, size * 0.5f);
        text.sortingOrder = Order + 20;
        SetAlpha(text, 0f);
        return text;
    }

    static void SetAlpha(TextMeshPro text, float a)
    {
        if (text == null) return;
        Color c = text.color; c.a = a; text.color = c;
    }

    static LineRenderer Line(Transform parent, string name, Color color, float width, int order, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = color;
        line.startWidth = line.endWidth = width;
        line.positionCount = points;
        line.useWorldSpace = true;
        line.numCapVertices = 2;
        line.sortingOrder = order;
        return line;
    }

    // The rope from `from` to the head, and an open claw (a V) at the head pointing along `dir`
    static void DrawHook(LineRenderer rope, LineRenderer claw, Vector2 from, Vector2 headAt, Vector2 dir, float size)
    {
        rope.SetPosition(0, from);
        rope.SetPosition(1, headAt);
        Vector2 side = new Vector2(-dir.y, dir.x);
        claw.SetPosition(0, headAt + (dir * 0.8f + side * 0.6f) * size);
        claw.SetPosition(1, headAt);
        claw.SetPosition(2, headAt + (dir * 0.8f - side * 0.6f) * size);
    }
}
