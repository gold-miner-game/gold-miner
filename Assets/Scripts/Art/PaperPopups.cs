using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the HUD's round cues (HUDController: the goal / target banner and the TARGET flag's swell) move
// on the stop-motion beat instead of gliding, and its Pause / end cards get their paper look
// (PaperPopupSkin) the frame they are built. The lost-life edge flash is left exactly as HUDController
// draws it (its paper redesign is parked for later). HUDController still runs every
// cue exactly as written (when, how long, how far); this only shows each value as it was at the last
// pose, 8 a second on real time like the HUD's other paper labels. Display only: the values HUDController
// wrote are put back before its next Update (PaperPopupsRestore), so it never reads a held value and a
// finished cue always ends on its own last value.
public class PaperPopups : MonoBehaviour
{
    static PaperPopups instance;

    HUDController hud;
    CanvasGroup banner;
    RectTransform flag;
    float nextFind;
    int shownFrame = -1;
    bool freshBanner, freshFlag;   // just found: take its value before holding anything
    Transform pauseCard, endCard;               // restyled once each (PaperPopupSkin)

    // what HUDController last wrote (put back each frame) and what is shown (the value at the last pose)
    static Vector3 bannerScaleLive, bannerScaleHeld, flagScaleLive, flagScaleHeld;
    static float bannerAlphaLive, bannerAlphaHeld;
    static bool holding;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { holding = false; }   // Play without a domain reload keeps statics

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Popups");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperPopups>();
        go.AddComponent<PaperPopupsRestore>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (instance == null) return;
        holding = false;
        instance.hud = null;
        instance.banner = null;
        instance.flag = null;
        instance.pauseCard = null;
        instance.endCard = null;
        instance.nextFind = 0f;
    }

    void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    // Before HUDController's Update: its own values back
    public static void RestoreNow()
    {
        if (!holding || instance == null) return;
        holding = false;
        PaperPopups p = instance;
        if (p.banner != null) { p.banner.transform.localScale = bannerScaleLive; p.banner.alpha = bannerAlphaLive; }
        if (p.flag != null) p.flag.localScale = flagScaleLive;
    }

    void Find()
    {
        if (hud == null)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 0.5f;
            hud = FindFirstObjectByType<HUDController>();
            if (hud == null) return;
        }
        if (hud.timerText == null || hud.timerText.canvas == null) return;
        Transform root = hud.timerText.canvas.transform;
        // built on demand by HUDController (on the clock's canvas, by these names; at runtime that is the
        // 16:9 frame inside the HUD canvas, so one level down is looked at too); looked up until they exist
        if (banner == null) { Transform t = Child(root, "RoundBanner"); if (t != null) { banner = t.GetComponent<CanvasGroup>(); freshBanner = true; } }
        // the Pause and end cards: restyled the frame they are built, before they are ever drawn
        if (pauseCard == null)
        {
            Transform card = Child(root, "PausePopup")?.Find("Card");
            if (card != null) { pauseCard = card; PaperPopupSkin.Card((RectTransform)card); }
        }
        if (endCard == null && hud.endPanel != null)
        {
            Transform card = hud.endPanel.transform.Find("EndCard");
            if (card != null) { endCard = card; PaperPopupSkin.Card((RectTransform)card); }
        }
        if (flag == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            foreach (RectTransform t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == "MoneyTarget") { flag = t; freshFlag = true; break; }
        }
    }

    static Transform Child(Transform root, string name)
    {
        Transform found = root.Find(name);
        if (found != null) return found;
        foreach (Transform c in root)
        {
            found = c.Find(name);
            if (found != null) return found;
        }
        return null;
    }

    void LateUpdate()
    {
        Find();
        if (hud == null) return;

        // stop-motion off (console): every frame is a pose, so the cues glide as HUDController wrote them
        bool beat = StopMotion.Enabled;
        int frame = Mathf.FloorToInt(Time.unscaledTime * StopMotion.Pose);
        bool pose = !beat || frame != shownFrame;
        shownFrame = frame;

        if (banner != null)
        {
            float before = bannerAlphaLive;
            bannerScaleLive = banner.transform.localScale;
            bannerAlphaLive = banner.alpha;
            if (pose || freshBanner) { bannerScaleHeld = bannerScaleLive; bannerAlphaHeld = bannerAlphaLive; }
            // fading in: a paper card is put down, not faded up, so it is there whole from its first pose
            // (and grows into place on the poses); fading out it thins away pose by pose
            if (beat && bannerAlphaLive > 0f && bannerAlphaLive >= before && bannerAlphaLive < 1f) bannerAlphaHeld = 1f;
            banner.transform.localScale = bannerScaleHeld;
            banner.alpha = bannerAlphaHeld;
            freshBanner = false;
        }
        if (flag != null)
        {
            flagScaleLive = flag.localScale;
            if (pose || freshFlag) flagScaleHeld = flagScaleLive;
            flag.localScale = flagScaleHeld;
            freshFlag = false;
        }
        holding = true;
    }
}
