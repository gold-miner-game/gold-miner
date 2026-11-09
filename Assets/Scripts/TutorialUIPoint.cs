using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Keeps a UI callout (the arrow) where you put it next to its target UI object, wherever that
// object ends up on screen. The store's parts are pinned to the screen's edges, and at Play the
// game fits everything into a 16:9 frame (ScreenFrame), so an edge part moves when the Game view
// isn't 16:9; a callout placed from the screen's middle would be left behind.
// In the editor, dragging the callout updates Offset, and typing an Offset moves the callout;
// while playing it keeps that offset from the target. Tools > Gold Miner > Tutorial Pointer adds it.
[ExecuteAlways]
public class TutorialUIPoint : MonoBehaviour
{
    [Tooltip("Name of the UI object the arrow points at. Empty = the step's UI Hole target")]
    public string target;
    [Tooltip("The callout's place from the target's centre, in this canvas's units (1080 tall). Drag the callout or type it here")]
    public Vector2 offset;

    RectTransform found;
    // What the editor last saw, to tell a drag (position changed) from a typed offset
    [System.NonSerialized] bool seen;
    [System.NonSerialized] Vector2 lastPosition, lastOffset;
    [System.NonSerialized] string lastTarget;

    // The name followed: its own, or the step's UI Hole's
    public string TargetName
    {
        get
        {
            if (!string.IsNullOrEmpty(target)) return target;
            TutorialStep step = GetComponentInParent<TutorialStep>(true);
            TutorialUIHole hole = step != null ? step.GetComponentInChildren<TutorialUIHole>(true) : null;
            return hole != null ? hole.target : null;
        }
    }

    void LateUpdate()
    {
        if (Application.isPlaying) Follow();
        else EditorTrack();
    }

    void EditorTrack()
    {
        if (!TargetPoint(out Vector2 at)) return;
        Vector2 position = transform.localPosition;
        string name = TargetName;
        if (seen && name == lastTarget && (position - lastPosition).sqrMagnitude < 0.01f
            && (offset - lastOffset).sqrMagnitude >= 0.0001f)
        {
            Place(at);   // a typed offset: move the callout there
        }
        else if (!seen || name != lastTarget || (position - lastPosition).sqrMagnitude >= 0.01f)
        {
            Vector2 now = position - at;   // dragged, a new target, or first look: remember where it is
            if ((now - offset).sqrMagnitude >= 0.01f)
            {
                offset = now;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);   // saved with the scene
#endif
            }
        }
        seen = true;
        lastTarget = name;
        lastPosition = transform.localPosition;
        lastOffset = offset;
    }

    void Follow()
    {
        if (TargetPoint(out Vector2 at)) Place(at);
    }

    void Place(Vector2 at)
    {
        Vector2 to = at + offset;
        transform.localPosition = new Vector3(to.x, to.y, transform.localPosition.z);
    }

    // The target's centre in the callout's parent's space
    bool TargetPoint(out Vector2 at)
    {
        at = Vector2.zero;
        var parent = transform.parent as RectTransform;
        RectTransform t = Find();
        if (parent == null || t == null) return false;

        Canvas owner = t.GetComponentInParent<Canvas>();
        Camera cam = owner != null && owner.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? owner.rootCanvas.worldCamera : null;
        var corners = new Vector3[4];
        t.GetWorldCorners(corners);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f);

        Canvas mine = GetComponentInParent<Canvas>();
        Camera myCam = mine != null && mine.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? mine.rootCanvas.worldCamera : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, myCam, out at);
    }

    RectTransform Find()
    {
        string name = TargetName;
        if (found != null && found.name == name && found.gameObject.activeInHierarchy) return found;
        found = null;
        if (string.IsNullOrEmpty(name)) return null;
        foreach (RectTransform rect in FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            if (rect.name == name && rect != transform && rect.GetComponentInParent<Canvas>() != null)
            {
                found = rect;
                break;
            }
        return found;
    }
}
