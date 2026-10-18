using System;
using System.Collections.Generic;

// Owner: Penny (shell) · filled by Yukun (characters)
// The list of pickable characters shown on the RolePick screen. A character self-registers from
// its own CrossLevelStatus subclass, so adding one is a new file with zero merge conflicts:
//
//     public class FrogStatus : CrossLevelStatus
//     {
//         [UnityEngine.RuntimeInitializeOnLoadMethod(
//             UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
//         static void Register() => CharacterRegistry.Add(new CharacterInfo(
//             "frog", "Frog", "Long tongue, high jump", () => Install<FrogStatus>()));
//         ...
//     }
public class CharacterInfo
{
    public readonly string id;
    public readonly string displayName;
    public readonly string description;
    public readonly Action activate;   // make this character the live CrossLevelStatus

    public CharacterInfo(string id, string displayName, string description, Action activate)
    {
        this.id = id;
        this.displayName = displayName;
        this.description = description;
        this.activate = activate;
    }
}

public static class CharacterRegistry
{
    static readonly List<CharacterInfo> list = new List<CharacterInfo>();

    // The default pig is always available, even before any other character registers.
    static CharacterRegistry()
    {
        list.Add(new CharacterInfo("pig", "Pig", "The default miner-dodging pig.",
            () => CrossLevelStatus.Install<PigStatus>()));
    }

    public static IReadOnlyList<CharacterInfo> All => list;

    public static void Add(CharacterInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.id)) return;
        if (Find(info.id) != null) return;   // ignore duplicate ids
        list.Add(info);
    }

    public static CharacterInfo Find(string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].id == id) return list[i];
        return null;
    }

    // Activate the saved choice (or the pig). Called by GameFlow at boot.
    public static void ActivateSaved()
    {
        CharacterInfo info = Find(ProgressStore.SelectedCharacter) ?? Find("pig");
        info?.activate?.Invoke();
    }
}
