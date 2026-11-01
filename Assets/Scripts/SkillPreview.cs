using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Owner: Penny (character pick)
// On a character's picture in the Select ("WHO ARE U") scene: while the mouse is over the picture,
// its name plate or its Select button, it acts out its skill, over and over, like in a level:
//   Block   Mr. Hyde turns blue and a white flash bounces off his head
//   Roll    JK. Rolling dashes and spins twice, then slides back
//   Tail    Scrat's tail goes see-through, firms up and sweeps a coin in
//   Tongue  Rero Rero's mouth opens and the tongue shoots out, grabs a coin and brings it back
// The picture is only a drawing (no PlayerCharacter), so this only moves its parts. A go that has
// begun finishes after the mouse leaves; then everything is put back as it was.
// Mystery: a character not to be explored yet. The picture is a black silhouette with a "?" on it,
// its name and remark read "???", and its Select button is locked (ChampionSelect skips it).
public class SkillPreview : MonoBehaviour
{
    public enum Skill { Block, Roll, Tail, Tongue }

    public Skill skill;
    [Tooltip("Not explored yet: a black silhouette with a \"?\", \"???\" for its name and remark, and it can't be picked")]
    public bool mystery;
    [Tooltip("Its name plate (hovering it also plays the skill)")]
    public RectTransform namePlate;
    [Tooltip("The line about it under the picture")]
    public RectTransform remark;
    [Tooltip("Its Select button (hovering it also plays the skill)")]
    public RectTransform selectButton;
    [Tooltip("How much bigger than the picture the hover area is, in world units on each side")]
    public float hoverMargin = 0.2f;
    [Tooltip("Seconds of rest between two goes")]
    public float rest = 0.6f;

    [Header("Block")]
    public Color blockTint = new Color(0.45f, 0.75f, 1f);
    public float blockTime = 0.8f;

    [Header("Roll")]
    public float rollTime = 0.5f;
    public int rollTurns = 2;
    [Tooltip("How far the dash goes, in world units (negative: to the left)")]
    public float rollDistance = 1f;

    [Header("Tail")]
    [Range(0f, 1f)] public float tailIdleOpacity = 0.3f;
    public float tailSwing = 30f;

    [Header("Tongue")]
    [Tooltip("How far the tongue reaches, times the frog's width")]
    public float tongueReach = 1.6f;
    public float tongueOut = 0.35f;
    public float tongueBack = 0.45f;

    static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
    static readonly Color Shadow = new Color(0.05f, 0.05f, 0.07f);

    // The Select button's character is a mystery: ChampionSelect neither labels nor picks it
    public static bool IsLocked(Button button)
    {
        foreach (SkillPreview p in FindObjectsByType<SkillPreview>(FindObjectsSortMode.None))
            if (p.mystery && p.selectButton != null && p.selectButton.gameObject == button.gameObject) return true;
        return false;
    }

    Bounds area;            // the picture at rest, in world space
    Vector3 restPos;
    Quaternion restRot;
    Vector3 restScale;
    float t = -1f;          // time into the current go; < 0: resting

    float Length => rest + (mystery ? 0.8f :
                            skill == Skill.Block ? blockTime :
                            skill == Skill.Roll ? rollTime + 0.6f :
                            skill == Skill.Tail ? 1.4f : tongueOut + tongueBack + 0.1f);

    SpriteRenderer body;          // Block
    Color bodyColor;
    Transform tailPivot;          // Tail
    SpriteRenderer[] tailParts;
    float[] tailAlpha;
    Quaternion tailRest;
    Transform mouth, mouthLine;   // Tongue
    Vector3 mouthScale, lineScale;
    SpriteRenderer tongue;
    SpriteRenderer coin;
    TextMeshPro question;         // Mystery

    void Awake()
    {
        restPos = transform.position;
        restRot = transform.rotation;
        restScale = transform.localScale;
        Renderer[] all = GetComponentsInChildren<Renderer>();
        area = all.Length > 0 ? all[0].bounds : new Bounds(transform.position, Vector3.one);
        foreach (Renderer r in all) area.Encapsulate(r.bounds);

        body = GetComponent<SpriteRenderer>();
        if (body != null) bodyColor = body.color;

        tailPivot = Find(transform, "TailPivot");
        if (tailPivot != null)
        {
            tailRest = tailPivot.localRotation;
            tailParts = tailPivot.GetComponentsInChildren<SpriteRenderer>();
            tailAlpha = new float[tailParts.Length];
            for (int i = 0; i < tailParts.Length; i++) tailAlpha[i] = tailParts[i].color.a;
        }

        mouth = Find(transform, "Mouth Red");
        mouthLine = Find(transform, "Mouth Outline");
        if (mouth != null) mouthScale = mouth.localScale;
        if (mouthLine != null) lineScale = mouthLine.localScale;

        if (mystery) Hide();
    }

    void Update()
    {
        if (t < 0f)
        {
            if (!Hovered()) return;
            t = 0f;
        }
        t += Time.unscaledDeltaTime;
        if (t >= Length) t = Hovered() ? 0f : -1f;   // again while still hovered, else rest
        if (t < 0f) { ResetLook(); return; }

        if (mystery)
        {
            // Only the "?" bobs: nothing to see yet
            if (question != null) question.transform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(t / Length * Mathf.PI * 2f));
            return;
        }
        switch (skill)
        {
            case Skill.Block: Block(); break;
            case Skill.Roll: Roll(); break;
            case Skill.Tail: Tail(); break;
            case Skill.Tongue: Tongue(); break;
        }
    }

    bool Hovered()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return false;
        Vector2 screen = mouse.position.ReadValue();
        foreach (RectTransform rect in new[] { namePlate, selectButton })
            if (rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null))
                return true;
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
        Bounds b = area;
        b.Expand(hoverMargin * 2f);
        return b.Contains(new Vector3(world.x, world.y, b.center.z));
    }

    // --- mystery ----------------------------------------------------------------------------------

    // Black silhouette, a "?" over it, "???" for the words, the button locked
    void Hide()
    {
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
            sr.color = new Color(Shadow.r, Shadow.g, Shadow.b, sr.color.a);
        if (body != null) bodyColor = body.color;

        var go = new GameObject("Mystery ?");
        go.transform.position = new Vector3(area.center.x, area.center.y, 0f);
        question = go.AddComponent<TextMeshPro>();
        question.text = "?";
        question.fontSize = Mathf.Max(4f, area.size.y * 6f);
        question.fontStyle = FontStyles.Bold;
        question.alignment = TextAlignmentOptions.Center;
        question.textWrappingMode = TextWrappingModes.NoWrap;
        question.rectTransform.sizeDelta = new Vector2(area.size.x + 2f, area.size.y + 2f);
        question.color = new Color(1f, 1f, 1f, 0.9f);
        question.sortingOrder = 60;

        SetWords(namePlate, "???");
        SetWords(remark, "???");
        if (selectButton != null)
        {
            Button button = selectButton.GetComponent<Button>();
            if (button != null) button.interactable = false;
            SetWords(selectButton, "Locked");
        }
    }

    static void SetWords(RectTransform rect, string words)
    {
        if (rect == null) return;
        TMP_Text text = rect.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = words;
    }

    // --- the skills -------------------------------------------------------------------------------

    // Blue for blockTime, with the block flash on top at the start
    void Block()
    {
        bool blocking = t < blockTime;
        if (body != null) body.color = blocking ? blockTint : bodyColor;
        if (t - Time.unscaledDeltaTime <= 0f)
            BlockFlash.Show(new Vector2(area.center.x, area.max.y - area.size.y * 0.15f), Vector2.up, area.size.x * 0.9f,
                            body != null ? body.sortingOrder + 30 : 30);
        float bump = blocking ? 1f + 0.06f * Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI) : 1f;
        transform.localScale = new Vector3(restScale.x * bump, restScale.y / bump, restScale.z);
    }

    // Dash rollDistance while spinning rollTurns times round the picture's middle, then slide back
    void Roll()
    {
        float dash, angle;
        if (t < rollTime)
        {
            float u = t / rollTime;
            dash = rollDistance * (1f - (1f - u) * (1f - u));
            angle = -360f * rollTurns * u * (rollDistance < 0f ? -1f : 1f);
        }
        else
        {
            float u = Mathf.Clamp01((t - rollTime) / 0.6f);
            dash = rollDistance * (1f - u * u * (3f - 2f * u));
            angle = 0f;
        }
        Vector3 centre = area.center + Vector3.right * dash;
        Quaternion spin = Quaternion.Euler(0f, 0f, angle);
        transform.SetPositionAndRotation(centre + spin * (restPos - area.center), spin * restRot);
    }

    // See-through, then firm (solid) while the tail sweeps a coin at his feet into it
    void Tail()
    {
        if (tailPivot == null) return;
        float alpha = Mathf.Lerp(tailIdleOpacity, 1f, Mathf.Clamp01((t - 0.15f) / 0.15f));   // firm from 0.3 s on
        for (int i = 0; i < tailParts.Length; i++)
        {
            Color c = tailParts[i].color;
            c.a = tailAlpha[i] * alpha;
            tailParts[i].color = c;
        }
        float sweep = t < 0.3f || t > 1.1f ? 0f : Mathf.Sin((t - 0.3f) / 0.8f * Mathf.PI * 2f) * tailSwing;
        tailPivot.localRotation = tailRest * Quaternion.Euler(0f, 0f, sweep);

        // The coin: by his feet, swept up into the tail and gone
        Vector3 feet = new Vector3(area.min.x - 0.2f, area.min.y + area.size.y * 0.15f, 0f);
        Vector3 tail = new Vector3(area.center.x, area.center.y + area.size.y * 0.25f, 0f);
        Coin(t < 1.0f, t < 0.45f ? feet : Vector3.Lerp(feet, tail, (t - 0.45f) / 0.4f),
             t < 0.85f ? 1f : Mathf.Clamp01(1f - (t - 0.85f) / 0.15f));
    }

    // Mouth open, tongue out to tongueReach, back with the coin, mouth shut
    void Tongue()
    {
        if (mouth == null) return;
        if (tongue == null) tongue = MakeTongue();
        float total = tongueOut + tongueBack;
        float open = t < total ? 1.4f : 1f;
        mouth.localScale = mouthScale * open;
        if (mouthLine != null) mouthLine.localScale = lineScale * open;

        float reach = tongueReach * (area.size.x / Mathf.Max(0.01f, transform.lossyScale.x));   // in the frog's local units
        float u = t < tongueOut ? t / tongueOut : t < total ? 1f - (t - tongueOut) / tongueBack : 0f;
        float length = reach * u;
        tongue.enabled = length > 0.001f;
        tongue.transform.localPosition = mouth.localPosition;
        tongue.transform.localScale = new Vector3(mouthScale.x * open * 0.8f, length, 1f);

        Vector3 tip = transform.TransformPoint(mouth.localPosition + Vector3.up * length);
        Vector3 far = transform.TransformPoint(mouth.localPosition + Vector3.up * reach);
        Coin(t < total, t >= tongueOut ? tip : far, 1f);
    }

    // --- parts ------------------------------------------------------------------------------------

    void Coin(bool show, Vector3 at, float size)
    {
        if (!show || size <= 0f) { if (coin != null) coin.enabled = false; return; }
        if (coin == null)
        {
            var go = new GameObject("Preview Coin");
            coin = go.AddComponent<SpriteRenderer>();
            coin.sprite = Disc();
            coin.color = Gold;
            coin.sortingOrder = 40;
        }
        coin.enabled = true;
        coin.transform.position = new Vector3(at.x, at.y, 0f);
        coin.transform.localScale = Vector3.one * 0.45f * size;
    }

    SpriteRenderer MakeTongue()
    {
        var go = new GameObject("Preview Tongue");
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Bar();
        SpriteRenderer red = mouth.GetComponent<SpriteRenderer>();
        sr.color = red != null ? red.color : new Color(0.85f, 0.35f, 0.4f);
        if (red != null) { sr.sortingLayerID = red.sortingLayerID; sr.sortingOrder = red.sortingOrder; }
        sr.enabled = false;
        return sr;
    }

    void ResetLook()
    {
        if (body != null) body.color = bodyColor;
        transform.localScale = restScale;
        transform.SetPositionAndRotation(restPos, restRot);
        if (tailPivot != null)
        {
            tailPivot.localRotation = tailRest;
            for (int i = 0; i < tailParts.Length; i++)
            {
                Color c = tailParts[i].color;
                c.a = tailAlpha[i];
                tailParts[i].color = c;
            }
        }
        if (mouth != null) mouth.localScale = mouthScale;
        if (mouthLine != null) mouthLine.localScale = lineScale;
        if (tongue != null) tongue.enabled = false;
        if (coin != null) coin.enabled = false;
        if (question != null) question.transform.localScale = Vector3.one;
    }

    void OnDisable()
    {
        if (t >= 0f) { t = -1f; ResetLook(); }
    }

    void OnDestroy()
    {
        if (coin != null) Destroy(coin.gameObject);
        if (question != null) Destroy(question.gameObject);
    }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    // --- generated sprites ------------------------------------------------------------------------

    static Sprite disc, bar;

    // A round coin, one unit across, with a darker rim
    static Sprite Disc()
    {
        if (disc != null) return disc;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01((1f - d) * r * 0.5f);
                float shade = d > 0.78f ? 0.75f : 1f;
                pixels[y * size + x] = new Color(shade, shade, shade, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return disc;
    }

    // A white bar one unit wide and one long, pivot at the bottom middle
    static Sprite Bar()
    {
        if (bar != null) return bar;
        const int w = 16, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        bar = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
        return bar;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { disc = null; bar = null; }   // Play without a domain reload keeps statics
}
