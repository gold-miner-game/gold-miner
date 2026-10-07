using System.Collections;
using UnityEngine;

// Owner: B (Player & Rocks)
public class Rock : MonoBehaviour
{
    public Color blockedColor = Color.white;
    public float flashTime = 0.15f;

    private SpriteRenderer sr;
    private Color baseColor;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    // Called by Hook when this rock blocks it
    public void OnHookBlocked()
    {
        if (sr == null) return;
        StopAllCoroutines();
        StartCoroutine(Flash());
    }

    IEnumerator Flash()
    {
        sr.color = blockedColor;
        yield return new WaitForSeconds(flashTime);
        sr.color = baseColor;
    }
}
