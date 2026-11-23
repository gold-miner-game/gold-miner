using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Art: papercraft typography, applied by rule at runtime so no scene, prefab or builder changes.
//   - Fredoka SemiBold (SIL OFL): soft rounded letters for HUD, buttons, dialogue and body text
//   - Luckiest Guy (Apache 2.0): chunky hand-cut display letters for big titles
// Only texts still on the stock fonts (LiberationSans SDF / the built-in legacy font) are switched, so
// any font a teammate picked on purpose is kept. The stock font stays as a fallback for glyphs the new
// fonts lack. IMGUI screens use the shared GUI skin font. Fonts and licences: Resources/Art/Fonts.
[DefaultExecutionOrder(-500)]
public class PaperType : MonoBehaviour
{
    const float TitleSize = 56f;   // at or above this size a text is a title

    public static Font Body { get; private set; }
    public static Font Display { get; private set; }
    static TMP_FontAsset bodyTmp, displayTmp;
    static PaperType instance;
    readonly HashSet<int> done = new HashSet<int>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        Body = Resources.Load<Font>("Art/Fonts/Fredoka-SemiBold");
        Display = Resources.Load<Font>("Art/Fonts/LuckiestGuy-Regular");
        if (Body == null || Display == null) return;
        var go = new GameObject("Paper Type");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperType>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    static TMP_FontAsset Tmp(Font font, TMP_FontAsset fallback)
    {
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
        asset.name = font.name + " SDF (runtime)";
        if (fallback != null) asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        return asset;
    }

    static bool StockTmp(TMP_FontAsset font) => font == null || font.name.StartsWith("LiberationSans");
    static bool StockLegacy(Font font) => font == null || font.name == "LegacyRuntime" || font.name == "Arial";

    void LateUpdate()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;
        if (bodyTmp == null)
        {
            TMP_FontAsset stock = TMP_Settings.defaultFontAsset;
            bodyTmp = Tmp(Body, stock);
            displayTmp = Tmp(Display, stock);
        }
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
        {
            if (!done.Add(text.GetInstanceID()) || !StockTmp(text.font)) continue;
            text.font = text.fontSize >= TitleSize ? displayTmp : bodyTmp;
        }
        foreach (Text text in FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (!done.Add(text.GetInstanceID()) || !StockLegacy(text.font)) continue;
            text.font = text.fontSize >= TitleSize ? Display : Body;
        }
    }

    void OnGUI()
    {
        // The built-in skin is shared by every OnGUI: styles without their own font use this one.
        if (GUI.skin.font != Body) GUI.skin.font = Body;
    }
}
