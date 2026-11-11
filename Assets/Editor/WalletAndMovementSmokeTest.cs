using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class WalletAndMovementSmokeTest
{
    public static void Run()
    {
        bool hadWallet = PlayerPrefs.HasKey("gm.wallet");
        int savedWallet = PlayerPrefs.GetInt("gm.wallet");
        CrossLevelStatus previousRecord = CrossLevelStatus.Active;
        GameManager previousRound = GameManager.Instance;
        Keyboard previousKeyboard = Keyboard.current;
        var background = InputSystem.settings.backgroundBehavior;
        var inputMode = InputSystem.settings.editorInputBehaviorInPlayMode;
        var objects = new List<GameObject>();
        Keyboard keyboard = null;
        int result = 0;
        try
        {
            var host = new GameObject("Wallet test record");
            objects.Add(host);
            var record = host.AddComponent<PigStatus>();
            typeof(CrossLevelStatus).GetProperty("Active").SetValue(null, record);
            record.setWallet(100);

            GameManager first = Round(objects, 500, 450, true);
            Set(first, "IsPlaying", true);
            Require(first.BankLevelEarnings() == 0, "active rounds are not settled early");
            Set(first, "IsPlaying", false);
            Require(first.BankLevelEarnings() == 500 && record.getWallet() == 600, "full collected gold is credited, not the margin");
            Require(first.BankLevelEarnings() == 0 && record.getWallet() == 600, "same round cannot pay twice");
            Require(Round(objects, 500, 0, true).BankLevelEarnings() == 500, "replaying for the same earnings pays again");
            Require(Round(objects, 100, 500, false).BankLevelEarnings() == 100, "a loss still banks collected gold");
            Require(Round(objects, 0, 0, false).BankLevelEarnings() == 0, "empty rounds do not add money");
            Require(record.getWallet() == 1200 && PlayerPrefs.GetInt("gm.wallet") == 1200, "wallet total is persisted");

            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            MethodInfo read = typeof(PlayerCharacter).GetMethod("ReadInput", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string name in new[] { "MrHyde", "Cat", "Squirrel", "Frog" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/" + name + ".prefab");
                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                objects.Add(instance);
                PlayerCharacter player = instance.GetComponent<PlayerCharacter>();
                CheckInput(player, read, keyboard, Vector2.right, Key.RightArrow);
                CheckInput(player, read, keyboard, Vector2.left, Key.LeftArrow);
                CheckInput(player, read, keyboard, Vector2.up, Key.UpArrow);
                CheckInput(player, read, keyboard, Vector2.down, Key.DownArrow);
                CheckInput(player, read, keyboard, Vector2.right, Key.D, Key.RightArrow);
                CheckInput(player, read, keyboard, Vector2.zero, Key.A, Key.RightArrow);
                CheckInput(player, read, keyboard, new Vector2(1f, 1f), Key.D, Key.UpArrow);
            }
            Debug.Log("WALLET_MOVEMENT_PASS: full earnings, replay and loss credits, no double payment, persistence, and arrow/WASD input on all four characters.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            result = 1;
        }
        finally
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null) previousKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = inputMode;
            foreach (GameObject instance in objects) if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            typeof(CrossLevelStatus).GetProperty("Active").SetValue(null, previousRecord);
            GameManager.Instance = previousRound;
            if (hadWallet) PlayerPrefs.SetInt("gm.wallet", savedWallet); else PlayerPrefs.DeleteKey("gm.wallet");
            PlayerPrefs.Save();
        }
        EditorApplication.Exit(result);
    }

    static GameManager Round(List<GameObject> objects, int money, int minerMoney, bool won)
    {
        var host = new GameObject("Test round");
        objects.Add(host);
        GameManager round = host.AddComponent<GameManager>();
        Set(round, "IsPlaying", false);
        Set(round, "PlayerMoney", money);
        Set(round, "MinerMoney", minerMoney);
        Set(round, "PlayerWon", won);
        return round;
    }

    static void Set(GameManager round, string name, object value)
    {
        typeof(GameManager).GetProperty(name).SetValue(round, value);
    }

    static void CheckInput(PlayerCharacter player, MethodInfo read, Keyboard keyboard, Vector2 expected, params Key[] keys)
    {
        // Set the device state directly: edit-mode tests do not have a running Game view.
        InputState.Change(keyboard, new KeyboardState(keys));
        keyboard.MakeCurrent();
        read.Invoke(player, null);
        Require(player.MoveInput == expected, player.name + " movement input " + string.Join(",", keys));
    }

    static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Wallet/movement: " + description);
    }
}
