using UnityEngine;

// One access policy shared by portraits, selection, and gameplay entry.
public static class CharacterUnlocks
{
    public const int Count = 6;
    // Unreleased characters stay unavailable, including under the console override.
    public static bool IsHidden(int index) => index == 2 || index == 3;
    public static int Price(int index) => index == 4 ? 3000 : index == 5 ? 1500 : 0;
    static string Key(int index) => "character_" + index;
    public static bool IsUnlocked(int index) => index >= 0 && index < Count && !IsHidden(index)
        && GameUnlocks.IsUnlocked(index < 2 || ProgressStore.IsCompleted(Key(index)));

    public static bool TryPurchase(int index, out string message)
    {
        message = "";
        if (index < 0 || index >= Count) return false;
        if (IsUnlocked(index)) return true; // Repeat clicks and the console override never charge.
        int price = Price(index);
        if (price <= 0) { message = "This character is not available yet."; return false; }
        CrossLevelStatus wallet = CrossLevelStatus.Ensure();
        int balance = wallet.getWallet();
        if (balance < price)
        {
            message = "Need $" + (price - balance).ToString("N0") + " more to unlock this character.";
            return false;
        }
        wallet.addWallet(-price);
        ProgressStore.MarkCompleted(Key(index));
        message = "Character unlocked! Select them to play.";
        return true;
    }
}
