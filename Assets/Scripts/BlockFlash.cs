using UnityEngine;

// Owner: Butian (block skill look)
// The flash of a successful block: a white half disc on the player's surface, facing the hook it
// stopped. It shows solid white, swells a little, fades and is destroyed. Hook makes one when it
// bounces off a blocking player. A plain generated sprite, no material of its own.
public class BlockFlash : MonoBehaviour
{
    public float lifetime = 0.6f;
    [Tooltip("The share of the lifetime it stays solid white before fading")]
    [Range(0f, 1f)] public float hold = 0.5f;
    [Tooltip("Never narrower than this, in world units, however small the player is")]
    public float minWidth = 1.4f;
    public float startScale = 0.85f;
    public float endScale = 1.2f;

    SpriteRenderer sr;
    Vector3 size;
    float age;

    static Sprite dome;

    // `surface` is where the hook touched the player; `towardHook` points out of the player at the
    // hook. `width` is the dome's width (about the player's)
    public static void Show(Vector2 surface, Vector2 towardHook, float width, int order = 30)
    {
        if (towardHook.sqrMagnitude < 0.0001f) towardHook = Vector2.up;
        // The sprite's flat side is its bottom edge; its round side points along local up
        var go = new GameObject("BlockFlash");
        go.transform.SetPositionAndRotation(surface, Quaternion.FromToRotation(Vector3.up, towardHook.normalized));

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Dome();
        sr.color = Color.white;
        sr.sortingOrder = order;   // over the player and the hook

        BlockFlash flash = go.AddComponent<BlockFlash>();
        flash.sr = sr;
        flash.size = Vector3.one * Mathf.Max(width, flash.minWidth);
        go.transform.localScale = flash.size * flash.startScale;
        Destroy(go, flash.lifetime + 0.1f);   // gone even if something stops its Update
        if (TutorialEvents.Log)
            Debug.Log("Block flash at " + surface + ", " + flash.size.x.ToString("0.00") + " wide, facing " + towardHook.normalized);
    }

    void Update()
    {
        age += Time.deltaTime;   // a frozen game holds the flash too
        float k = Mathf.Clamp01(age / lifetime);
        float grow = 1f - (1f - k) * (1f - k);   // fast out, settling
        transform.localScale = size * Mathf.Lerp(startScale, endScale, grow);
        float fade = hold < 1f ? Mathf.Clamp01((k - hold) / (1f - hold)) : 0f;
        sr.color = new Color(1f, 1f, 1f, 1f - fade);
    }

    // A solid white half disc, one unit wide, flat side down, with a slightly soft round edge
    static Sprite Dome()
    {
        if (dome != null) return dome;
        const int w = 128, h = 64;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        float r = w * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, 0f)) / r;
                float a = Mathf.Clamp01((1f - d) * r * 0.5f);   // solid inside, two pixels of soft edge
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        dome = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
        return dome;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { dome = null; }   // Play without a domain reload keeps statics
}
