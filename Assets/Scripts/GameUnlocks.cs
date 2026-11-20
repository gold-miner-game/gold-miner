using UnityEngine;

// All feature/progression locks must go through this policy, including future features.
// IsUnlocked(normalCondition) preserves normal play and honors the saved developer override.
public static class GameUnlocks
{
    const string OverrideKey = "gm.dev.unlockEverything";
    public static bool EverythingUnlocked => PlayerPrefs.GetInt(OverrideKey, 0) == 1;
    public static bool IsUnlocked(bool normalCondition) => EverythingUnlocked || normalCondition;

    public static void UnlockEverything()
    {
        PlayerPrefs.SetInt(OverrideKey, 1);
        ProgressStore.MarkCompleted(ProgressStore.TutorialDoneId);
        PlayerPrefs.Save();
    }

    public static void ResetOverride()
    {
        PlayerPrefs.DeleteKey(OverrideKey);
        PlayerPrefs.Save();
    }
}
