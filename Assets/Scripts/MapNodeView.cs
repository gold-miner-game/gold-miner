using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// One card on the world map, a normal UI object in the Map scene: move it, resize it, restyle it or
// change its label in the Scene view. nodeId ties it to a node in WorldMap.asset, which still owns
// the gameplay side (type, level, prerequisites). At runtime WorldMapController colours it by
// state (cleared / available / locked) and wires the click.
[RequireComponent(typeof(RectTransform))]
public class MapNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Id of the WorldMap node this card opens, e.g. \"battle_03\". Must match WorldMap.asset.")]
    public string nodeId;
    public Button button;
    [Tooltip("Shown only on available nodes (the next steps). Optional.")]
    public Image glow;
    [Tooltip("Accent ring behind the face. Optional.")]
    public Image outline;
    public Image face;
    public TMP_Text label;

    public RectTransform Rect => (RectTransform)transform;

    WorldMapController owner;
    WorldMapController.NodeState state;
    string authoredLabel;
    bool hover;

    public void Show(WorldMapController controller, WorldMapController.NodeState nodeState)
    {
        owner = controller;
        state = nodeState;
        if (label != null && authoredLabel == null) authoredLabel = label.text;

        bool open = state != WorldMapController.NodeState.Locked;
        Color accent = owner.AccentFor(state);

        if (glow != null)
        {
            glow.gameObject.SetActive(state == WorldMapController.NodeState.Available);
            glow.color = new Color(accent.r, accent.g, accent.b, 0.16f);
        }
        if (outline != null) outline.color = open ? accent : new Color(accent.r, accent.g, accent.b, 0.55f);
        if (label != null)
        {
            label.color = open ? owner.textBright : owner.textDim;
            label.text = (state == WorldMapController.NodeState.Cleared ? "✓  " : "") + authoredLabel;
        }
        if (button != null) button.interactable = open;
        PaintFace();
    }

    void PaintFace()
    {
        if (face == null || owner == null) return;
        face.color = hover && state != WorldMapController.NodeState.Locked ? owner.cardHover : owner.CardFor(state);
    }

    public void OnPointerEnter(PointerEventData eventData) { hover = true; PaintFace(); }
    public void OnPointerExit(PointerEventData eventData) { hover = false; PaintFace(); }
}
