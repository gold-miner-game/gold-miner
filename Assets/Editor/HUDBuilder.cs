using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Owner: D (Hook & UI)
// Tools > Gold Miner > Create HUD: builds the HUD canvas in the open scene and wires HUDController.
public static class HUDBuilder
{
    [MenuItem("Tools/Gold Miner/Create HUD")]
    static void CreateHUD()
    {
        HUDController existing = Object.FindFirstObjectByType<HUDController>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.LogWarning("HUDBuilder: this scene already has a HUD (" + existing.name + "). Delete it first to rebuild.");
            return;
        }

        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("HUDBuilder: no default TMP font. Import it via Window > TextMeshPro > Import TMP Essential Resources, then try again.");
            return;
        }

        // Canvas that scales with the window
        GameObject root = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root, "Create HUD");

        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvas = root.transform;
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);
        Vector2 center = new Vector2(0.5f, 0.5f);

        // Top bar: the miner sits at top center, so the timer goes on the right
        TMP_Text playerMoney = CreateText(canvas, "PlayerMoney", "Player: $0", topLeft, new Vector2(40f, -30f), new Vector2(500f, 60f), 44f, TextAlignmentOptions.Left);
        TMP_Text lives = CreateText(canvas, "Lives", "Life: 0", topLeft, new Vector2(40f, -95f), new Vector2(300f, 50f), 34f, TextAlignmentOptions.Left);
        TMP_Text minerMoney = CreateText(canvas, "MinerMoney", "Miner: $0", topRight, new Vector2(-40f, -30f), new Vector2(500f, 60f), 44f, TextAlignmentOptions.Right);
        TMP_Text timer = CreateText(canvas, "Timer", "1:30", topRight, new Vector2(-40f, -95f), new Vector2(300f, 70f), 52f, TextAlignmentOptions.Right);

        // End panel: full-screen dark overlay with the result in the middle
        GameObject endPanel = new GameObject("EndPanel", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = endPanel.GetComponent<RectTransform>();
        panelRect.SetParent(canvas, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        endPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        TMP_Text result = CreateText(endPanel.transform, "Result", "YOU WIN", center, new Vector2(0f, 90f), new Vector2(1200f, 140f), 110f, TextAlignmentOptions.Center);
        TMP_Text detail = CreateText(endPanel.transform, "Detail", "Time's up!\nPress R to restart", center, new Vector2(0f, -80f), new Vector2(1400f, 300f), 40f, TextAlignmentOptions.Center);

        // HUDController hides the panel on Start; keep it hidden in the editor too
        endPanel.SetActive(false);

        HUDController hud = root.AddComponent<HUDController>();
        hud.playerMoneyText = playerMoney;
        hud.minerMoneyText = minerMoney;
        hud.timerText = timer;
        hud.livesText = lives;
        hud.endPanel = endPanel;
        hud.resultText = result;
        hud.detailText = detail;

        EditorUtility.SetDirty(hud);
        Selection.activeGameObject = root;
        Debug.Log("HUDBuilder: HUD created. Save the scene to keep it.");
    }

    // A white TMP label anchored and pivoted at the same corner, so position is the margin from it
    static TMP_Text CreateText(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
}
