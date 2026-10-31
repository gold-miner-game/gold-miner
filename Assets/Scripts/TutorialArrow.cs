using UnityEngine;

// Owner: Penny (Level map & tutorial)
// A bobbing arrow for wordless tutorials. Add it to a level scene with Tools > Gold Miner >
// Tutorial Arrow, then move it next to what it should point at and rotate it (Z) to aim: it
// points along its own "down". It draws above TutorialSpotlight's dark, so it shows in the dark,
// and by default it is part of the spotlight's intro: it fades out with it.
// It runs on unscaled time, so it keeps bobbing while the intro has the game frozen.
public class TutorialArrow : MonoBehaviour
{
    [Tooltip("Fade out and hide together with the level's TutorialSpotlight intro")]
    public bool withSpotlight = true;
    [Tooltip("Optional: hide the arrow once this is gone (eaten, picked up, destroyed)")]
    public Transform pointsAt;
    [Tooltip("Seconds before the arrow hides by itself. 0 = never")]
    public float hideAfter = 0f;
    [Tooltip("How far the arrow bobs toward what it points at, in world units")]
    public float bob = 0.25f;
    [Tooltip("Bobs per second")]
    public float speed = 2f;
    [Tooltip("The child that moves; the arrow's own position stays where you put it")]
    public Transform visual;

    float startedAt;
    bool targetSeen;   // the level's items start hidden, so "gone" only counts once it was there
    SpriteRenderer[] sprites;
    float[] alphas;    // each sprite's own alpha, scaled by the spotlight's fade
    bool spotlightSeen;

    void Awake()
    {
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        alphas = new float[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) alphas[i] = sprites[i].color.a;
    }

    void OnEnable()
    {
        startedAt = Time.unscaledTime;
    }

    void Update()
    {
        float t = Time.unscaledTime - startedAt;
        if (visual != null)
            visual.localPosition = Vector3.down * (bob * 0.5f * (1f - Mathf.Cos(t * speed * 2f * Mathf.PI)));

        if (hideAfter > 0f && t >= hideAfter) { gameObject.SetActive(false); return; }

        if (pointsAt != null && pointsAt.gameObject.activeInHierarchy) targetSeen = true;
        else if (targetSeen) { gameObject.SetActive(false); return; }   // destroyed (null) or switched off

        if (withSpotlight) FollowSpotlight();
    }

    void FollowSpotlight()
    {
        TutorialSpotlight spotlight = TutorialSpotlight.Active;
        if (spotlight == null)
        {
            // The intro already ended (it switches itself off), or there is none: nothing to follow
            if (spotlightSeen) gameObject.SetActive(false);
            return;
        }
        spotlightSeen = true;
        for (int i = 0; i < sprites.Length; i++)
        {
            Color c = sprites[i].color;
            c.a = alphas[i] * spotlight.Visibility;
            sprites[i].color = c;
        }
    }
}
