using UnityEngine;
using UnityEngine.InputSystem;

// The between-level and mid-round shop. Buying and using only change CrossLevelStatus.
// Whatever skin is in the scene reads that record.
public class StoreController : MonoBehaviour
{
    static StoreController active;
    static bool continueRequested;

    bool open;
    bool betweenLevels;
    float savedTimeScale = 1f;
    GUIStyle boxStyle;
    GUIStyle labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Ensure();
    }

    public static StoreController Ensure()
    {
        CrossLevelStatus.Ensure();
        if (active != null) return active;
        var go = new GameObject("Store");
        DontDestroyOnLoad(go);
        return go.AddComponent<StoreController>();
    }

    public static void OpenAfterWin()
    {
        StoreController store = Ensure();
        store.betweenLevels = true;
        store.open = true;
        LevelLoader.WaitToAdvance = true;
        StoreView.Show(true, store.Leave);
    }

    public static bool ConsumeContinue()
    {
        if (!continueRequested) return false;
        continueRequested = false;
        return true;
    }

    void Awake()
    {
        if (active != null && active != this)
        {
            Destroy(gameObject);
            return;
        }

        active = this;
        DontDestroyOnLoad(gameObject);
        LevelLoader.LevelWon += OpenAfterWin;
    }

    void OnDestroy()
    {
        if (active == this)
        {
            active = null;
            LevelLoader.LevelWon -= OpenAfterWin;
        }
        if (betweenLevels) LevelLoader.WaitToAdvance = false;
        if (Time.timeScale == 0f) Time.timeScale = savedTimeScale;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || CrossLevelStatus.Active == null) return;

        if (keyboard.bKey.wasPressedThisFrame && !open && !betweenLevels
            && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
        {
            OpenDuringPlay();
        }

        if (!open) TryHotkeys(keyboard);
    }

    void OpenDuringPlay()
    {
        open = true;
        betweenLevels = false;
        savedTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
        Time.timeScale = 0f;
        StoreView.Show(false, Leave);
    }

    void Leave()
    {
        bool advance = betweenLevels;
        Close();
        StoreView.Hide();
        if (advance)
        {
            continueRequested = true;
            LevelLoader.WaitToAdvance = false;
            LevelLoader.AdvanceNow = true;
        }
    }

    void Close()
    {
        open = false;
        if (!betweenLevels) Time.timeScale = savedTimeScale;
        betweenLevels = false;
        LevelLoader.WaitToAdvance = false;
    }

    void TryHotkeys(Keyboard keyboard)
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        CrossLevelStatus status = CrossLevelStatus.Active;
        if (keyboard.digit1Key.wasPressedThisFrame) status.useSkill("swift");
        else if (keyboard.digit2Key.wasPressedThisFrame) status.useSkill("ration");
        else if (keyboard.digit3Key.wasPressedThisFrame) status.useSkill("shield");
        else if (keyboard.digit4Key.wasPressedThisFrame) status.useSkill("brace");
    }

    void OnGUI()
    {
        CrossLevelStatus status = CrossLevelStatus.Active;
        if (status == null) return;

        EnsureStyles();
        if (open || GameManager.Instance == null) return;
        DrawStatus(status);
    }

    void DrawStatus(CrossLevelStatus status)
    {
        PlayerSkill swift = status.getSkill("swift");
        PlayerSkill ration = status.getSkill("ration");
        PlayerSkill shield = status.getSkill("shield");
        PlayerSkill brace = status.getSkill("brace");
        string line = "Wallet $" + status.getWallet().ToString("N0")
            + "   [1] Water " + (swift == null ? 0 : swift.stock)
            + "   [2] Ration " + (ration == null ? 0 : ration.stock)
            + "   [3] Shield " + (shield == null ? 0 : shield.stock)
            + "   [4] Brace " + (brace == null ? 0 : brace.stock)
            + "   [B] store";

        if (swift != null && swift.active) line += "   Swift this level";
        else if (swift != null && swift.armed) line += "   Swift next level";
        if (ration != null && ration.active) line += "   No digest";
        else if (ration != null && ration.armed) line += "   No digest next level";
        if (shield != null && shield.secondsLeft > 0f) line += "   Shield " + Mathf.CeilToInt(shield.secondsLeft) + "s";
        else if (shield != null && shield.armed) line += "   Shield next level";
        if (brace != null && brace.active) line += "   Blocking this level";
        else if (brace != null && brace.armed) line += "   Blocking next level";

        GUI.Label(new Rect(10, Screen.height - 78, 1100, 22), line, labelStyle);
    }

    void EnsureStyles()
    {
        if (labelStyle != null) return;
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        labelStyle.normal.textColor = Color.white;
        boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperCenter };
        boxStyle.normal.textColor = Color.white;
    }
}
