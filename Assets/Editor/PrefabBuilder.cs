using System.IO;
using UnityEditor;
using UnityEngine;

// Editor-only tool: Tools > Build Prefabs creates the eight prefabs from the Alpha Build Guide, section 2.
// Existing prefabs are skipped, so running it again never overwrites someone's tuning.
public static class PrefabBuilder
{
    const string SpriteDir = "Assets/Sprites";
    const string PrefabDir = "Assets/Prefabs";

    [MenuItem("Tools/Build Prefabs")]
    public static void Build()
    {
        Sprite circle = FindSprite("Circle");
        Sprite hexagon = FindSprite("Hexagon");
        Sprite diamond = FindSprite("Diamond");
        if (circle == null || hexagon == null || diamond == null)
        {
            Debug.LogError("PrefabBuilder: missing sprite(s) in " + SpriteDir +
                           ". Needed: Circle, Hexagon Flat-Top, Isometric Diamond (Create > 2D > Sprites).");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        int made = 0;

        // Mr. Hyde (the pig)
        made += Save("MrHyde", circle, new Color(1f, 0.6f, 0.75f), 0.8f, 0, go =>
        {
            Rigidbody2D rb = AddBody(go, 1f, 0f);
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            AddCircle(go, circle, false);
            go.AddComponent<MrHydeController>().moveSpeed = 5f;
        });

        // Rock
        made += Save("Rock", hexagon, new Color(0.5f, 0.5f, 0.5f), 1f, 0, go =>
        {
            AddBody(go, 3f, 10f);
            AddCircle(go, hexagon, false);
            go.AddComponent<Rock>();
        });

        // Treasure
        made += SaveTreasure("Gold_Small", circle, new Color(1f, 0.85f, 0.1f), 0.4f, TreasureType.SmallGold, 50);
        made += SaveTreasure("Gold_Medium", circle, new Color(1f, 0.85f, 0.1f), 0.7f, TreasureType.MediumGold, 100);
        made += SaveTreasure("Gold_Large", circle, new Color(1f, 0.85f, 0.1f), 1.1f, TreasureType.LargeGold, 250);
        made += SaveTreasure("Diamond", diamond, new Color(0.5f, 0.85f, 1f), 0.5f, TreasureType.Diamond, 600);

        // Telegraph dot (no physics, no script)
        made += Save("TelegraphDot", circle, Color.white, 0.12f, 10, go => { });

        // Life (optional feature)
        made += Save("Life", circle, new Color(0.9f, 0.15f, 0.15f), 0.4f, 0, go =>
        {
            AddCircle(go, circle, true);
            go.AddComponent<LifePickup>();
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PrefabBuilder: created " + made + " prefab(s) in " + PrefabDir + " (existing ones were skipped).");
    }

    static int SaveTreasure(string name, Sprite sprite, Color color, float scale, TreasureType type, int value)
    {
        return Save(name, sprite, color, scale, 0, go =>
        {
            AddCircle(go, sprite, true);
            Treasure t = go.AddComponent<Treasure>();
            t.type = type;
            t.value = value;
        });
    }

    static int Save(string name, Sprite sprite, Color color, float scale, int sortingOrder, System.Action<GameObject> setup)
    {
        string path = PrefabDir + "/" + name + ".prefab";
        if (File.Exists(path))
        {
            Debug.Log("PrefabBuilder: skipped " + path + " (already exists).");
            return 0;
        }

        GameObject go = new GameObject(name);
        try
        {
            go.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            setup(go);

            PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
        return 1;
    }

    static Rigidbody2D AddBody(GameObject go, float mass, float damping)
    {
        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.mass = mass;
        rb.linearDamping = damping;
        rb.freezeRotation = true;
        return rb;
    }

    static void AddCircle(GameObject go, Sprite sprite, bool isTrigger)
    {
        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        Vector3 e = sprite.bounds.extents;
        col.radius = (e.x + e.y) * 0.5f;   // local units; the object's scale shrinks it with the sprite
        col.offset = Vector2.zero;
        col.isTrigger = isTrigger;
    }

    static Sprite FindSprite(string nameContains)
    {
        foreach (string guid in AssetDatabase.FindAssets(nameContains + " t:Sprite", new[] { SpriteDir }))
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) return s;
        }
        return null;
    }
}
