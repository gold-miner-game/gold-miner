using UnityEngine;

// Optional feature. Owner: E (Treasure & Docs)
// A Life to pick up: drawn as a red heart (a generated sprite, tinted by the renderer's colour), so it
// reads as a Life and not as treasure.
public class LifePickup : MonoBehaviour
{
    private bool taken;

    static Sprite heart;

    void Awake()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sprite = Heart();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryPickUp(other.GetComponent<PlayerCharacter>());
    }

    // Also called by the squirrel's tail. Returns true if the character picked it up
    public bool TryPickUp(PlayerCharacter player)
    {
        if (taken || player == null) return false;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return false;

        taken = true;   // the body and tail can touch it in the same frame: only one Life
        GameManager.Instance.AddLife();
        Destroy(gameObject);
        return true;
    }

    // A white heart, one unit wide, with a dark outline and a small shine; the renderer tints it red
    public static Sprite Heart()
    {
        if (heart != null) return heart;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // The classic heart curve (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0, in -1.3..1.3
                float u = (x + 0.5f) / size * 2.6f - 1.3f;
                float v = (y + 0.5f) / size * 2.6f - 1.25f;
                float f = Mathf.Pow(u * u + v * v - 1f, 3f) - u * u * v * v * v;
                if (f > 0f) continue;
                float inner = Mathf.Pow(u * u / 0.78f + v * v / 0.78f - 1f, 3f) - u * u * v * v * v / 0.69f;
                bool outline = inner > 0f;
                bool shine = (u + 0.45f) * (u + 0.45f) + (v - 0.35f) * (v - 0.35f) < 0.04f;
                Color32 c = outline ? new Color32(90, 90, 90, 255) : shine ? new Color32(255, 255, 255, 255) : new Color32(235, 235, 235, 255);
                pixels[y * size + x] = c;
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        heart = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return heart;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { heart = null; }   // Play without a domain reload keeps statics
}
