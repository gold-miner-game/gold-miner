using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// One pose control for Elsa's SpriteRenderer character and Image-based UI portrait.
[ExecuteAlways]
public class ElsaMouthPose : MonoBehaviour
{
    [Tooltip("Show the blue ice piece in Elsa's mouth.")]
    public bool showIce = true;
    public Color iceTint = new Color(.80f, .86f, .90f, 1f);
    [Tooltip("Extra opening of EACH beak half from the original portrait pose, in degrees.")]
    [Range(0f, 110f)] public float mouthHalfArc = 0f;
    public Transform upperBeak;
    public Transform lowerBeak;
    public GameObject ice;

    [System.Serializable]
    struct Jaw
    {
        public Transform part;
        public Vector3 basePoint;
        public Vector3 hinge;
        public Quaternion rotation;
        public float openingSign;
    }
    [SerializeField, HideInInspector] bool initialized;
    [SerializeField, HideInInspector] Jaw upperRest;
    [SerializeField, HideInInspector] Jaw lowerRest;
    [SerializeField, HideInInspector] GameObject[] iceParts;
    [SerializeField, HideInInspector] SpriteRenderer[] iceSprites;
    [SerializeField, HideInInspector] Image[] iceImages;
    [SerializeField, HideInInspector] bool iceLookCached;

    void OnEnable() { ApplyPose(); }
    void LateUpdate() { ApplyPose(); }
    void OnValidate() { mouthHalfArc = Mathf.Clamp(mouthHalfArc, 0f, 110f); }

    public void SetPose(bool withIce, float halfArc)
    {
        showIce = withIce;
        mouthHalfArc = Mathf.Clamp(halfArc, 0f, 110f);
        ApplyPose();
    }

    public void ApplyPose()
    {
        if (!initialized)
        {
            foreach (Transform part in GetComponentsInChildren<Transform>(true))
            {
                if (upperBeak == null && part.name == "Upper Beak") upperBeak = part;
                if (lowerBeak == null && part.name == "Lower Beak") lowerBeak = part;
                if (ice == null && part.name == "Blue Tongue") ice = part.gameObject;
            }
            if (upperBeak == null || lowerBeak == null) return;
            if (ice != null) iceParts = new[] { ice };
            else
            {
                // The UI converter flattens the ice capsule into three separately named Images.
                var pieces = new List<GameObject>();
                foreach (Image image in GetComponentsInChildren<Image>(true))
                    if (IsIceColor(image.color))
                        pieces.Add(image.gameObject);
                iceParts = pieces.ToArray();
                if (iceParts.Length > 0) ice = iceParts[0];
            }
            upperRest = Capture(upperBeak, true);
            lowerRest = Capture(lowerBeak, false);
            initialized = true;
        }
        if (iceParts != null)
            foreach (GameObject piece in iceParts)
                if (piece != null && piece.activeSelf != showIce) piece.SetActive(showIce);
        TintIce();
        Apply(upperRest, Mathf.Clamp(mouthHalfArc, 0f, 110f));
        Apply(lowerRest, Mathf.Clamp(mouthHalfArc, 0f, 110f));
    }

    static bool IsIceColor(Color color)
    {
        Color frozen = new Color(.80f, .86f, .90f, 1f);
        return color.b > .9f && color.g > .4f && color.g < .8f && color.r < .25f
            || Mathf.Abs(color.r - frozen.r) < .025f && Mathf.Abs(color.g - frozen.g) < .025f
                && Mathf.Abs(color.b - frozen.b) < .025f;
    }

    void TintIce()
    {
        if (!iceLookCached || iceSprites == null || iceImages == null)
        {
            var sprites = new List<SpriteRenderer>(); var images = new List<Image>();
            if (iceParts != null)
                foreach (GameObject piece in iceParts)
                {
                    if (piece == null) continue;
                    sprites.AddRange(piece.GetComponentsInChildren<SpriteRenderer>(true));
                    images.AddRange(piece.GetComponentsInChildren<Image>(true));
                }
            iceSprites = sprites.ToArray(); iceImages = images.ToArray();
            iceLookCached = true;
        }
        foreach (SpriteRenderer sprite in iceSprites)
            if (sprite != null) sprite.color = new Color(iceTint.r, iceTint.g, iceTint.b, sprite.color.a);
        foreach (Image image in iceImages)
            if (image != null) image.color = new Color(iceTint.r, iceTint.g, iceTint.b, image.color.a);
    }

    // Convert a cone's actual half-angle into extra opening from the authored beak pose.
    public float OpeningForHalfArc(float halfArc)
    {
        ApplyPose();
        if (!initialized || upperRest.part == null) return mouthHalfArc;
        Vector3 direction = upperRest.rotation * Vector3.Scale(Vector3.up, upperRest.part.localScale);
        float originalElevation = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
        return Mathf.Clamp(halfArc - originalElevation, 0f, 110f);
    }

    public bool TryGetMouthFrame(out Vector3 position, out Vector2 forward)
    {
        ApplyPose(); position = transform.position; forward = transform.right;
        if (!initialized || upperRest.part == null || lowerRest.part == null) return false;
        position = (upperRest.part.TransformPoint(upperRest.basePoint)
            + lowerRest.part.TransformPoint(lowerRest.basePoint)) * .5f;
        Vector3 restForward = upperRest.rotation * Vector3.Scale(Vector3.up, upperRest.part.localScale);
        Vector3 parentForward = new Vector3(Mathf.Sign(restForward.x), 0f, 0f);
        forward = upperRest.part.parent != null ? (Vector2)upperRest.part.parent.TransformVector(parentForward) : (Vector2)parentForward;
        forward.Normalize();
        // Art: Elsa's artwork is drawn at 80% (Penguin.prefab "Artwork", so she matches the pig and the cat).
        // The breath must still start where it did at full size, so the mouth point is scaled back out from
        // the artwork's centre by its drawn scale: the freeze cone, its reach and every freeze rule stay
        // exactly as before. A full-size artwork (scale 1) leaves the point untouched.
        Transform art = upperRest.part;
        while (art != null && art.parent != null && art.parent != transform) art = art.parent;
        float drawn = art != null && art.parent == transform ? Mathf.Abs(art.localScale.y) : 1f;
        if (drawn > 0.01f && Mathf.Abs(drawn - 1f) > 0.001f)
            position = art.position + (position - art.position) / drawn;
        return true;
    }

    static Jaw Capture(Transform part, bool upper)
    {
        Vector3 basePoint = Vector3.zero;
        var sprite = part.GetComponent<SpriteRenderer>();
        var image = part.GetComponent<Image>();
        if (sprite != null && sprite.sprite != null)
        {
            Bounds bounds = sprite.sprite.bounds;
            basePoint = new Vector3(bounds.center.x, bounds.min.y, 0f);
        }
        else if (image != null)
        {
            Rect rect = image.rectTransform.rect;
            basePoint = new Vector3(rect.center.x, rect.yMin, 0f);
        }
        Vector3 origin = part is RectTransform rt ? rt.anchoredPosition3D : part.localPosition;
        Vector3 forward = part.localRotation * Vector3.Scale(Vector3.up, part.localScale);
        float upperSign = forward.x < 0f ? -1f : 1f;
        return new Jaw {
            part = part, basePoint = basePoint, rotation = part.localRotation,
            hinge = origin + part.localRotation * Vector3.Scale(basePoint, part.localScale),
            openingSign = upper ? upperSign : -upperSign
        };
    }

    static void Apply(Jaw rest, float halfArc)
    {
        if (rest.part == null) return;
        Quaternion rotation = Quaternion.Euler(0f, 0f, rest.openingSign * halfArc) * rest.rotation;
        Vector3 origin = rest.hinge - rotation * Vector3.Scale(rest.basePoint, rest.part.localScale);
        rest.part.localRotation = rotation;
        if (rest.part is RectTransform rt) rt.anchoredPosition3D = origin;
        else rest.part.localPosition = origin;
    }
}
