using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// Run in an isolated editor project: creates only temporary objects, restores saved overrides.
public static class CharacterSkillSmokeTest
{
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly MethodInfo skillInput = typeof(PlayerCharacter).GetMethod("HandleSkillInput", BindingFlags.Instance | BindingFlags.NonPublic);
    static int checks;
    static bool wasHeld;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Input(PlayerCharacter player, params Key[] keys)
    {
        bool held = Array.IndexOf(keys, Key.K) >= 0;
        typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(player,
            Array.IndexOf(keys, Key.D) >= 0 ? Vector2.right : Vector2.zero);
        skillInput.Invoke(player, new object[] { held && !wasHeld, held });
        wasHeld = held;
    }
    static T Make<T>() where T : PlayerCharacter
    {
        var host = new GameObject(typeof(T).Name);
        objects.Add(host);
        var player = host.AddComponent<T>();
        if (player is PenguinController elsa) elsa.InitializeSkills();
        return player;
    }
    static void Set(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    public static void Run()
    {
        int exit = 0;
        bool hadOverride = PlayerPrefs.HasKey("gm.dev.unlockEverything");
        int savedOverride = PlayerPrefs.GetInt("gm.dev.unlockEverything");
        string savedCompleted = PlayerPrefs.GetString("gm.progress.completed", "");
        bool hadCompleted = PlayerPrefs.HasKey("gm.progress.completed");
        int previousChoice = ChampionSpawner.Chosen;
        bool hadWallet = PlayerPrefs.HasKey("gm.wallet");
        int savedWallet = PlayerPrefs.GetInt("gm.wallet");
        float scale = Time.timeScale;
        var oldRound = GameManager.Instance;
        var record = CrossLevelStatus.Active;
        try
        {
            GameManager.Instance = null;
            typeof(CrossLevelStatus).GetProperty("Active").SetValue(null, null);
            Time.timeScale = 1f;
            PlayerPrefs.SetInt("gm.dev.unlockEverything", 1);
            var pig = Make<MrHydeController>();
            Input(pig, Key.K);
            Check(pig.IsBlocking, "Mr. Hyde K must block");
            Check(pig.SkillActive && pig.SkillReadyIn > 0f, "block HUD state");

            var cat = Make<CatController>();
            Input(cat); Input(cat, Key.K, Key.D);
            Check(cat.IsRolling && !cat.IsBlocking, "cat K must roll, never block");
            Check(cat.SkillActive && cat.SkillReadyIn > 0f, "roll HUD state");
            float remaining = cat.CooldownRemaining;
            Input(cat); Input(cat, Key.K);
            Check(cat.CooldownRemaining == remaining, "repeated K must respect roll cooldown");

            var squirrel = Make<SquirrelController>();
            var tail = squirrel.gameObject.AddComponent<SquirrelTailCollector>();
            Set(squirrel, "tail", tail);
            Input(squirrel); Input(squirrel, Key.K);
            Check(squirrel.TailFirm && squirrel.SkillActive && !squirrel.IsBlocking, "hold K firms tail");
            Input(squirrel);
            Check(!squirrel.TailFirm, "release K clears tail");
            Input(squirrel, Key.K);
            Time.timeScale = 0f; Input(squirrel, Key.K);
            Check(!squirrel.TailFirm, "pause clears held skill");
            Time.timeScale = 1f;

            var frog = Make<FrogController>();
            var tongue = frog.gameObject.AddComponent<FrogTongue>();
            Set(frog, "tongue", tongue);
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f, 4);
            foreach (string name in new[] { "mouth", "frogBodyArtwork", "tongue" })
            {
                var child = new GameObject(name); child.transform.SetParent(frog.transform);
                var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                typeof(FrogTongue).GetField(name).SetValue(tongue, renderer);
            }
            tongue.tongueTip = new GameObject("Tip").transform;
            tongue.tongueTip.SetParent(frog.transform);
            Input(frog); Input(frog, Key.K);
            Check(frog.TongueOut && frog.SkillActive && !frog.IsBlocking, "frog K shoots tongue");
            Check(!tongue.Fire(), "cannot fire a second tongue while busy");
            UnityEngine.Object.DestroyImmediate(sprite);

            var elsa = Make<PenguinController>();
            Input(elsa); Input(elsa, Key.K);
            Check(elsa.HasSkill && elsa.SkillActive && !elsa.IsBlocking, "Elsa K uses her ice skill rather than block");
            // Either available Level 1 character earns both permanent starting skills.
            PlayerPrefs.DeleteKey("gm.dev.unlockEverything");
            ProgressStore.ClearCompleted(SkillUnlockSummary.BlockUnlockId);
            ProgressStore.ClearCompleted(SkillUnlockSummary.RollUnlockId);
            ProgressStore.ClearCompleted("battle_06");
            pig.canBlock = true; // stale prefab/record flags must not bypass progression.
            Check(!pig.CanBlock && !cat.HasSkill, "both starting skills stay locked before Level 1");
            SkillUnlockSummary.UnlockForWin("level_02");
            Check(!pig.CanBlock, "other levels cannot grant block");
            for (int chosen = 0; chosen < 2; chosen++)
            {
                ProgressStore.ClearCompleted(SkillUnlockSummary.BlockUnlockId);
                ProgressStore.ClearCompleted(SkillUnlockSummary.RollUnlockId);
                ChampionSpawner.Chosen = chosen;
                SkillUnlockSummary.UnlockForWin("level_01");
                Check(pig.CanBlock && cat.HasSkill, "Level 1 won as champion " + chosen + " unlocks both starting skills");
                Check(!cat.CanBlock && !squirrel.CanBlock && !frog.CanBlock,
                    "block reward cannot replace other characters' signature skills");
                Input(cat); Input(cat, Key.K);
                Check(cat.IsRolling && !cat.IsBlocking, "cat retains roll after Level 1 reward");
                Input(squirrel); Input(squirrel, Key.K);
                Check(squirrel.TailFirm && !squirrel.IsBlocking, "squirrel retains tail after reward");
            }
            var spawnerHost = new GameObject("Reward preview roster"); objects.Add(spawnerHost);
            var spawner = spawnerHost.AddComponent<ChampionSpawner>();
            spawner.champions = new[] { pig.gameObject, cat.gameObject };
            var summaryHost = new GameObject("Reward summary"); objects.Add(summaryHost);
            var summary = summaryHost.AddComponent<SkillUnlockSummary>(); summary.player = cat;
            var source = typeof(SkillUnlockSummary).GetMethod("RewardPreviewSource", BindingFlags.Instance | BindingFlags.NonPublic);
            Check((PlayerCharacter)source.Invoke(summary, null) == cat, "cat win must preview cat roll");
            Time.timeScale = 0f;
            var pausedCat = Make<CatController>();
            Input(pausedCat); Input(pausedCat, Key.K);
            Check(!pausedCat.IsRolling, "K cannot activate while paused");
            Time.timeScale = 1f;
            var roundHost = new GameObject("Ended round"); objects.Add(roundHost);
            var round = roundHost.AddComponent<GameManager>(); GameManager.Instance = round;
            typeof(GameManager).GetProperty("IsPlaying").SetValue(round, false);
            Input(pausedCat); Input(pausedCat, Key.K);
            Check(!pausedCat.IsRolling, "K cannot activate after round end");
            // Eye highlights follow world movement even when the cat is mirrored or rolling.
            GameManager.Instance = null;
            Time.timeScale = 1f;
            var gazeCat = Make<CatController>();
            var eyeObject = new GameObject("EyeL"); eyeObject.transform.SetParent(gazeCat.transform);
            var eye = eyeObject.AddComponent<SpriteRenderer>();
            var eyeSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f, 4);
            eye.sprite = eyeSprite;
            eyeObject.transform.localScale = new Vector3(.16f, .12f, 1f);
            var glint = new GameObject("Catchlight").transform; glint.SetParent(eyeObject.transform);
            glint.localPosition = new Vector3(.18f, -.20f, .03f);
            var cacheGaze = typeof(CatController).GetMethod("CacheEyeHighlights", BindingFlags.Instance | BindingFlags.NonPublic);
            var updateGaze = typeof(CatController).GetMethod("UpdateEyeHighlights", BindingFlags.Instance | BindingFlags.NonPublic);
            cacheGaze.Invoke(gazeCat, null);
            updateGaze.Invoke(gazeCat, null);
            Check(glint.localPosition.x > 0f && glint.localPosition.y < 0f, "cat's default gaze is down-right");
            foreach (float mirror in new[] { 1f, -1f })
            foreach (float angle in new[] { 0f, 90f, 225f })
            foreach (Vector2 direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                gazeCat.transform.localScale = new Vector3(mirror, 1f, 1f);
                eyeObject.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(gazeCat, direction);
                updateGaze.Invoke(gazeCat, null);
                Vector2 offset = glint.position - eye.transform.TransformPoint(eyeSprite.bounds.center);
                Check(Vector2.Dot(offset.normalized, direction.normalized) > .98f, "gaze follows movement after mirror/spin");
                Check(Mathf.Abs(glint.localPosition.x) < .38f && Mathf.Abs(glint.localPosition.y) < .38f
                    && Mathf.Approximately(glint.localPosition.z, .03f), "glint stays inside eye and preserves depth");
            }
            Vector3 lastGaze = glint.localPosition;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(gazeCat, Vector2.zero);
            updateGaze.Invoke(gazeCat, null);
            Check(glint.localPosition == lastGaze, "idle preserves last gaze");
            Time.timeScale = 0f;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(gazeCat, Vector2.right);
            updateGaze.Invoke(gazeCat, null);
            Check(glint.localPosition == lastGaze, "pause preserves gaze");
            Time.timeScale = 1f;
            UnityEngine.Object.DestroyImmediate(eyeSprite);

            var hulk = Make<RhinoController>();
            var accelerate = typeof(RhinoController).GetMethod("Accelerate", BindingFlags.Instance | BindingFlags.NonPublic);
            hulk.acceleration = 10f; hulk.maximumChargeSpeed = 20f;
            Vector2 speedOne = (Vector2)accelerate.Invoke(hulk, new object[] { Vector2.zero, Vector2.right, .1f });
            Vector2 speedTwo = (Vector2)accelerate.Invoke(hulk, new object[] { speedOne, Vector2.right, .1f });
            Check(Mathf.Approximately(speedOne.x, 1f) && Mathf.Approximately(speedTwo.x, 2f), "Hulk accelerates rather than jumping to fixed speed");
            Vector2 diagonalCharge = (Vector2)accelerate.Invoke(hulk, new object[] { Vector2.zero, Vector2.one, .1f });
            Check(Mathf.Approximately(diagonalCharge.magnitude, speedOne.magnitude), "diagonal charge does not accelerate faster");
            Check(((Vector2)accelerate.Invoke(hulk, new object[] { Vector2.right * 19f, Vector2.right, 1f })).magnitude == 20f,
                "maximum charge speed is enforced");
            hulk.acceleration = 20f;
            Check(Mathf.Approximately(((Vector2)accelerate.Invoke(hulk, new object[] { Vector2.zero, Vector2.right, .1f })).x, 2f),
                "acceleration changes are read live");
            Input(hulk); Input(hulk, Key.K);
            Check(hulk.IsCharging && hulk.SkillActive && !hulk.IsBlocking, "K starts Hulk charge instead of block");
            float firstChargeWait = hulk.SkillReadyIn;
            Input(hulk); Input(hulk, Key.K);
            Check(hulk.SkillReadyIn == firstChargeWait, "pressing K again cannot restart active charge");
            typeof(RhinoController).GetMethod("EndCharge", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hulk, new object[] { true });
            Check(!hulk.IsCharging && hulk.ChargeVelocity == Vector2.zero && hulk.SkillReadyIn > 0f, "ending charge clears momentum and starts cooldown");
            Input(hulk); Input(hulk, Key.K);
            Check(!hulk.IsCharging, "K respects Hulk cooldown");

            // Character purchases share the persistent wallet and the global unlock policy.
            PlayerPrefs.DeleteKey("gm.dev.unlockEverything");
            ProgressStore.ClearCompleted("character_4");
            ProgressStore.ClearCompleted("character_5");
            Check(CharacterUnlocks.IsUnlocked(0) && CharacterUnlocks.IsUnlocked(1), "starting characters remain free");
            Check(!CharacterUnlocks.IsUnlocked(2) && !CharacterUnlocks.IsUnlocked(3), "squirrel/frog stay masked");
            Check(!CharacterUnlocks.IsUnlocked(4) && !CharacterUnlocks.IsUnlocked(5), "paid characters start locked");
            Check(!CharacterUnlocks.TryPurchase(2, out _), "mystery characters cannot be purchased");
            var walletHost = new GameObject("Purchase wallet"); objects.Add(walletHost);
            var purchaseWallet = walletHost.AddComponent<PigStatus>();
            typeof(CrossLevelStatus).GetProperty("Active").SetValue(null, purchaseWallet);
            purchaseWallet.setWallet(2999);
            Check(!CharacterUnlocks.TryPurchase(4, out _) && purchaseWallet.getWallet() == 2999,
                "insufficient funds never charge or unlock");
            Check(!CharacterUnlocks.IsUnlocked(4), "failed purchase stays locked");
            purchaseWallet.setWallet(4500);
            Check(CharacterUnlocks.TryPurchase(4, out _) && purchaseWallet.getWallet() == 1500, "Elsa costs exactly 3000");
            Check(CharacterUnlocks.IsUnlocked(4), "Elsa unlock persists in progress");
            Check(CharacterUnlocks.TryPurchase(4, out _) && purchaseWallet.getWallet() == 1500, "repeat purchase never charges twice");
            Check(CharacterUnlocks.TryPurchase(5, out _) && purchaseWallet.getWallet() == 0, "Hulk costs exactly 1500");
            Check(CharacterUnlocks.IsUnlocked(5), "Hulk unlock persists in progress");
            Check(!CharacterUnlocks.TryPurchase(-1, out _) && !CharacterUnlocks.IsUnlocked(99), "invalid roster ids stay inaccessible");
            PlayerPrefs.SetInt("gm.dev.unlockEverything", 1);
            Check(!CharacterUnlocks.IsUnlocked(2) && !CharacterUnlocks.IsUnlocked(3), "console override keeps both hidden characters locked");
            ProgressStore.MarkCompleted("character_2"); ProgressStore.MarkCompleted("character_3");
            Check(!CharacterUnlocks.IsUnlocked(2) && !CharacterUnlocks.IsUnlocked(3), "old saved unlock flags cannot expose hidden characters");
            Check(!CharacterUnlocks.TryPurchase(2, out _) && !CharacterUnlocks.TryPurchase(3, out _), "console override cannot purchase hidden characters");
            Check(CharacterUnlocks.IsUnlocked(0) && CharacterUnlocks.IsUnlocked(1)
                && CharacterUnlocks.IsUnlocked(4) && CharacterUnlocks.IsUnlocked(5), "console still unlocks all four available characters");
            Check(CharacterUnlocks.TryPurchase(4, out _) && purchaseWallet.getWallet() == 0, "override never charges wallet");
            Debug.Log("CHARACTER_SKILLS_PASS: " + checks + " activation, hold/release, cooldown, HUD, pause and round-end checks.");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (var host in objects) if (host != null) UnityEngine.Object.DestroyImmediate(host);
            objects.Clear();
            GameManager.Instance = oldRound;
            typeof(CrossLevelStatus).GetProperty("Active").SetValue(null, record);
            ChampionSpawner.Chosen = previousChoice;
            if (hadCompleted) PlayerPrefs.SetString("gm.progress.completed", savedCompleted);
            else PlayerPrefs.DeleteKey("gm.progress.completed");
            typeof(ProgressStore).GetField("completed", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            Time.timeScale = scale;
            if (hadOverride) PlayerPrefs.SetInt("gm.dev.unlockEverything", savedOverride);
            else PlayerPrefs.DeleteKey("gm.dev.unlockEverything");
            if (hadWallet) PlayerPrefs.SetInt("gm.wallet", savedWallet);
            else PlayerPrefs.DeleteKey("gm.wallet");
            PlayerPrefs.Save();
        }
        EditorApplication.Exit(exit);
    }
}
