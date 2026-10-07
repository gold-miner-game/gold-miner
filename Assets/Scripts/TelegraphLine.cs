using System.Collections.Generic;
using UnityEngine;

// Owner: C (Miner AI)
public class TelegraphLine : MonoBehaviour
{
    public GameObject dotPrefab;     // TelegraphDot prefab
    public float spacing = 0.35f;
    public int maxDots = 60;

    private readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();

    void Awake()
    {
        for (int i = 0; i < maxDots; i++)
        {
            GameObject dot = Instantiate(dotPrefab, transform);
            dot.SetActive(false);
            dots.Add(dot.GetComponent<SpriteRenderer>());
        }
    }

    public void Show(Vector2 from, Vector2 to, Color color)
    {
        Vector2 dir = (to - from).normalized;
        float length = Vector2.Distance(from, to);
        int count = Mathf.Min(maxDots, Mathf.FloorToInt(length / spacing) + 1);

        for (int i = 0; i < dots.Count; i++)
        {
            bool on = i < count;
            dots[i].gameObject.SetActive(on);
            if (!on) continue;

            dots[i].transform.position = from + dir * (spacing * i);
            dots[i].color = color;
        }
    }

    public void Hide()
    {
        foreach (SpriteRenderer dot in dots) dot.gameObject.SetActive(false);
    }
}
