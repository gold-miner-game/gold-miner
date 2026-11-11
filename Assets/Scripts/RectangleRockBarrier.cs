using System.Collections.Generic;
using UnityEngine;

// Resize the whole enclosure in Play mode without stretching its individual rocks.
// Runs after LevelLayout hides Items and before LevelLoader reads and shows them.
[DefaultExecutionOrder(-1500)]
[RequireComponent(typeof(LevelLayout))]
public class RectangleRockBarrier : MonoBehaviour
{
    [Tooltip("Total outside width of the rectangular barrier, left to right, in world units.")]
    [Min(1f)] public float width = 11f;
    [Tooltip("Total outside length of the rectangular barrier, bottom to top, in world units.")]
    [Min(1f)] public float length = 6.5f;

    readonly List<Rock> rocks = new List<Rock>();
    Transform itemRoot;
    Vector3 center;
    Vector3 rockScale;
    Vector2 rockSize;
    float spacing;
    float appliedWidth = -1f;
    float appliedLength = -1f;

    void Awake()
    {
        itemRoot = GetComponent<LevelLayout>().items;
        if (itemRoot == null) return;
        rocks.AddRange(itemRoot.GetComponentsInChildren<Rock>(true));
        if (rocks.Count == 0) return;

        Vector3 min = rocks[0].transform.localPosition;
        Vector3 max = min;
        foreach (Rock rock in rocks)
        {
            min = Vector3.Min(min, rock.transform.localPosition);
            max = Vector3.Max(max, rock.transform.localPosition);
        }
        center = (min + max) * 0.5f;
        rockScale = rocks[0].transform.localScale;
        SpriteRenderer renderer = rocks[0].GetComponent<SpriteRenderer>();
        Vector3 spriteSize = renderer != null && renderer.sprite != null ? renderer.sprite.bounds.size : Vector3.one;
        rockSize = new Vector2(Mathf.Abs(spriteSize.x * rockScale.x), Mathf.Abs(spriteSize.y * rockScale.y));
        // Overlap neighboring rocks so stretching the enclosure never opens gaps in its edges.
        spacing = Mathf.Max(0.05f, Mathf.Min(rockSize.x, rockSize.y) * 0.7f);
        Resize();
    }

    void Update()
    {
        if (rocks.Count == 0) return;
        if (width != appliedWidth || length != appliedLength) Resize();
    }

    void OnValidate()
    {
        width = Mathf.Max(1f, width);
        length = Mathf.Max(1f, length);
    }

    void Resize()
    {
        width = Mathf.Max(1f, width);
        length = Mathf.Max(1f, length);
        float spanX = Mathf.Max(0f, width - rockSize.x);
        float spanY = Mathf.Max(0f, length - rockSize.y);
        int columns = Mathf.Max(1, Mathf.CeilToInt(spanX / spacing));
        int rows = Mathf.Max(1, Mathf.CeilToInt(spanY / spacing));
        int used = 0;
        for (int x = 0; x <= columns; x++)
        {
            float atX = Mathf.Lerp(-spanX * 0.5f, spanX * 0.5f, (float)x / columns);
            Place(ref used, atX, -spanY * 0.5f);
            Place(ref used, atX, spanY * 0.5f);
        }
        // Corners already belong to the horizontal edges.
        for (int y = 1; y < rows; y++)
        {
            float atY = Mathf.Lerp(-spanY * 0.5f, spanY * 0.5f, (float)y / rows);
            Place(ref used, -spanX * 0.5f, atY);
            Place(ref used, spanX * 0.5f, atY);
        }
        for (int i = used; i < rocks.Count; i++) rocks[i].gameObject.SetActive(false);
        appliedWidth = width;
        appliedLength = length;
        Physics2D.SyncTransforms();
    }

    void Place(ref int index, float x, float y)
    {
        if (index >= rocks.Count)
        {
            Rock extra = Instantiate(rocks[0], itemRoot);
            extra.name = "Rock";
            rocks.Add(extra);
        }
        Rock rock = rocks[index++];
        rock.transform.localPosition = center + new Vector3(x, y, 0f);
        rock.transform.localScale = rockScale;
        rock.MakeWall();
        rock.gameObject.SetActive(true);
    }
}
