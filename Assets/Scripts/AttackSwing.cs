using UnityEngine;

// Owner: Butian (attack mode)
// The pig's attack (left click in attack mode, MrHydeController): a white blade that sweeps a sixth
// of a circle (60 degrees) round the player, fast, centred on the way it attacks. It follows the
// player while it sweeps, fades at the end and is destroyed. A generated sprite, no art of its own.
public class AttackSwing : MonoBehaviour
{
    float duration, arc, startAngle, age;
    SpriteRenderer sr;

    static Sprite blade;

    // `dir`: the middle of the sweep. `radius`: world units from the player's centre to the blade's tip
    public static void Show(Transform player, Vector2 dir, float radius, float duration, float arcDegrees = 60f)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        var go = new GameObject("AttackSwing");
        go.transform.SetParent(player, false);
        // Keep its own size whatever the player's scale
        Vector3 s = player.lossyScale;
        go.transform.localScale = new Vector3(radius / NonZero(s.x), radius / NonZero(s.y), 1f);

        AttackSwing swing = go.AddComponent<AttackSwing>();
        swing.sr = go.AddComponent<SpriteRenderer>();
        swing.sr.sprite = Blade();
        swing.sr.color = Color.white;
        swing.sr.sortingOrder = 30;   // over the player and the enemies
        swing.duration = Mathf.Max(0.02f, duration);
        swing.arc = arcDegrees;
        swing.startAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - arcDegrees * 0.5f;
        swing.Place(0f);
        Destroy(go, swing.duration + 0.15f);   // gone even if something stops its Update
    }

    // Signed, so a player mirrored with a negative scale doesn't mirror the blade
    static float NonZero(float v) => Mathf.Abs(v) < 0.0001f ? 0.0001f : v;

    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / duration);
        Place(1f - (1f - k) * (1f - k));   // fast out, settling at the end
        // Solid while it sweeps, then a quick fade
        float fade = Mathf.Clamp01((age - duration) / 0.12f);
        sr.color = new Color(1f, 1f, 1f, 1f - fade);
    }

    // Leading edge at startAngle + arc * t (counterclockwise), in world angles whatever the player's rotation
    void Place(float t)
    {
        float angle = startAngle + arc * t;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // A band of a ring (inner 0.55, outer 1 of the radius) from angle 0 back to -50 degrees: bright at
    // the leading edge (angle 0) and fading toward the tail, so it reads as a slash. Pivot = ring centre
    static Sprite Blade()
    {
        if (blade != null) return blade;
        const int size = 256;
        const float tail = 50f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - r, y + 0.5f - r);
                float d = p.magnitude / r;
                float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;   // -180..180; the band is -tail..0
                float alpha = 0f;
                if (a <= 0f && a >= -tail && d >= 0.55f && d <= 1f)
                {
                    float along = 1f - (-a / tail);                     // 1 at the leading edge
                    float across = 1f - Mathf.Abs(d - 0.8f) / 0.25f;    // thickest in the middle of the band
                    float edge = Mathf.Clamp01((1f - d) * r * 0.5f) * Mathf.Clamp01((d - 0.55f) * r * 0.5f);
                    alpha = Mathf.Clamp01(along * along * (0.4f + 0.6f * across)) * edge;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        blade = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), r);   // 1 world unit = the radius
        return blade;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { blade = null; }   // Play without a domain reload keeps statics
}
