using UnityEngine;
using UnityEngine.EventSystems;

// Art: a felt button pressed down: while it is held, what is sewn on it (label, key cap, tab) sits a little
// lower, one held pose, and comes back up on release. The button itself, its click and its hit area
// never move.
public class PaperPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    const float Depth = 3f;
    bool down;

    public void OnPointerDown(PointerEventData e) => Press(true);
    public void OnPointerUp(PointerEventData e) => Press(false);
    public void OnPointerExit(PointerEventData e) => Press(false);
    void OnDisable() => Press(false);

    void Press(bool value)
    {
        if (down == value) return;
        down = value;
        float dy = value ? -Depth : Depth;
        foreach (Transform child in transform)
            if (child is RectTransform r) r.anchoredPosition += new Vector2(0f, dy);
    }
}
