using UnityEngine;

// Owner: Butian (Survival mode)
// The survival mode's hook "miner": a hole in the ground that shoots a red hook at the player, with
// no miner body. SurvivalLoader makes them around the player; LevelLoader makes one per hook origin
// for levels with "hook_holes": 1 (the dodge tutorial), so the tutorial teaches the real thing.
public static class HookHole
{
    public static readonly Color HoleColor = new Color(0.07f, 0.05f, 0.04f);
    public static readonly Color RimColor = new Color(0.42f, 0.3f, 0.18f);

    // Built inactive, so every Awake/OnEnable sees its references already set: the caller adds an
    // aimer or changes the AI's timing if it wants, then calls root.SetActive(true).
    // The hole is ai.transform; the hook and the telegraph are its siblings under root.
    public static SurvivalHookAI Build(string name, Vector2 at, Transform target, GameObject telegraphDot,
        Sprite holeSprite, Material spriteMaterial, float extendSpeed, float retractSpeed)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.position = at;

        var holeObject = new GameObject("Hole");
        holeObject.transform.SetParent(root.transform, false);
        SpriteRenderer holeRenderer = holeObject.AddComponent<SpriteRenderer>();
        holeRenderer.sprite = holeSprite;
        if (spriteMaterial != null) holeRenderer.sharedMaterial = spriteMaterial;
        holeRenderer.sortingOrder = -50;   // under treasure and the player

        // The hook's own sprite is hidden: it only lends the material and sorting to the rope
        var hookObject = new GameObject("Hook");
        hookObject.transform.SetParent(root.transform, false);
        SpriteRenderer hookRenderer = hookObject.AddComponent<SpriteRenderer>();
        if (spriteMaterial != null) hookRenderer.sharedMaterial = spriteMaterial;
        hookRenderer.sortingOrder = 5;
        Hook hook = hookObject.AddComponent<Hook>();
        hook.extendSpeed = extendSpeed;
        hook.retractSpeed = retractSpeed;
        hook.hitsPig = true;
        hook.grabsTreasure = false;
        hook.defaultColor = Color.red;

        var telegraphObject = new GameObject("Telegraph");
        telegraphObject.transform.SetParent(root.transform, false);
        TelegraphLine telegraph = telegraphObject.AddComponent<TelegraphLine>();
        telegraph.dotPrefab = telegraphDot;

        // Every shot is a red one at the player, none at treasure
        SurvivalHookAI ai = holeObject.AddComponent<SurvivalHookAI>();
        ai.hook = hook;
        ai.telegraph = telegraph;
        ai.hookOrigin = holeObject.transform;
        ai.target = target;
        return ai;
    }

    // A dark round hole with a lighter rim, one world unit across.
    // Art (papercraft): the rim is a torn paper ring with fibre grain and a darker cut edge; the hole
    // gets deeper toward its centre and its top-left wall is in shadow (light from the top left).
    // Same colours, size and hole radius (72%) as before.
    public static Sprite CreateSprite(Color hole, Color rim)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - r, y + 0.5f - r) / r;
                float d = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x);
                // torn outer edge and a slightly ragged hole edge
                float outer = 0.93f + 0.06f * Mathf.PerlinNoise(Mathf.Cos(angle) * 2.3f + 5f, Mathf.Sin(angle) * 2.3f + 5f);
                float inner = 0.72f + 0.025f * (Mathf.PerlinNoise(Mathf.Cos(angle) * 3f + 11f, Mathf.Sin(angle) * 3f + 11f) - 0.5f);
                float grain = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.6f + Mathf.PerlinNoise(x * 0.37f + 50f, y * 0.37f) * 0.4f;
                Color c;
                if (d < inner)
                {
                    float depth = Mathf.Pow(d / inner, 1.6f);                                   // 0 centre .. 1 edge
                    float wall = Mathf.Clamp01(Vector2.Dot(p.normalized, new Vector2(-0.7f, 0.7f))) * depth;
                    c = Color.Lerp(hole * 0.55f, hole * 1.25f, depth) * (1f - 0.35f * wall);
                    // the far (bottom-right) wall catches the light: a lit inner lip in the rim colour
                    float lip = Mathf.Clamp01(1f - (inner - d) / 0.09f) * Mathf.Clamp01(Vector2.Dot(p.normalized, new Vector2(0.7f, -0.7f)));
                    c = Color.Lerp(c, rim * 0.5f, lip * 0.65f);
                    c.a = hole.a;
                }
                else
                {
                    float fibre = Mathf.PerlinNoise(angle * 9f + 3f, d * 24f);              // torn fibres run around the ring
                    float lit = 0.78f + 0.24f * grain + 0.14f * (fibre - 0.5f);
                    float edge = Mathf.Min(d - inner, outer - d) / 0.07f;                     // darker cut edges
                    lit *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(edge));
                    lit *= 1f + 0.22f * Vector2.Dot(p, new Vector2(-0.6f, 0.6f));              // top-left light
                    c = rim * lit;
                    c.a = rim.a;
                }
                c.a *= Mathf.Clamp01((outer - d) * r * 0.9f);   // soft edge
                pixels[y * size + x] = c;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
