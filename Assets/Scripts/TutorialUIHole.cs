using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Lights up a UI element (the HUD's timer, lives, wallet, ...) while its TutorialStep shows: the
// step's dark covers the UI too, except a rounded box around this target. Put it inside a step
// with Tools > Gold Miner > Tutorial UI Hole.
// The HUD lives in the Game scene and a level scene can't point at another scene's objects, so the
// target is kept by name. In the Inspector, drag the UI object onto "Pick UI object" to fill it in.
public class TutorialUIHole : MonoBehaviour
{
    [Tooltip("Name of the UI object to light, e.g. Timer, Lives, WalletValue, Sidebar, TopBar")]
    public string target;
    [Tooltip("Space kept around the target, in 1080p pixels")]
    public float padding = 10f;
    [Tooltip("Roundness of the lit box's corners, in 1080p pixels")]
    public float cornerRadius = 14f;
    [Tooltip("Width of the soft edge, in 1080p pixels (0 = sharp)")]
    public float feather = 8f;

    RectTransform found;

    // The target in the loaded scenes, or null
    public RectTransform Find()
    {
        if (found != null && found.gameObject.activeInHierarchy) return found;
        found = null;
        if (string.IsNullOrEmpty(target)) return null;
        foreach (RectTransform rect in FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            if (rect.name == target && rect.GetComponentInParent<Canvas>() != null)
            {
                found = rect;
                break;
            }
        return found;
    }
}
