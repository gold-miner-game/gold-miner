#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using TMPro;

// Run in a disposable copy of the project with analytics disabled and its own product name.
// Uses actual player input, item activation, treasure collisions, hook hits and bullet time.
[DefaultExecutionOrder(-1500)]
public class StoreDemoCaptureDriver : MonoBehaviour
{
    const int Size = 192;
    static readonly string[] Ids = { "swift", "ration", "shield", "brace", "time_machine" };
    static readonly string[] CharacterIds = { "pig_block", "cat_roll", "hulk_charge", "elsa_frost" };
    bool characterMode;
    bool skillObserved;
    int demoSkillUses;
    float demoSkillAt;
    int rollDirections;
    Rock chargeRock;
    Rock greenBarrier;
    bool frostObserved;
    bool breathObserved;
    bool minerFrozenObserved;
    bool hookFrozenObserved;
    Transform demoKeyFace;
    SpriteRenderer demoKeyFill;
    Sprite demoKeySprite;
    Camera captureCamera;
    PlayerCharacter player;
    CrossLevelStatus record;
    GameManager gm;
    MinerAI miner;
    TextMeshPro caption;
    TextMeshPro status;
    Texture2D atlas;
    Texture2D frame;
    RenderTexture target;
    GameObject stage;
    Keyboard keyboard;
    string id;
    float started;
    float firstMinerFreezeAt = -1f, firstHookFreezeAt = -1f;
    float previousCaptureAt, largestCaptureStep;
    float DemoElapsed => (characterMode ? Time.time : Time.unscaledTime) - started;
    bool boosted;
    bool recording;
    int captured;
    int nextShot;
    float lowestTimeScale;
    float normalDistance;
    float boostedDistance;

    public static void Begin()
    {
        if (PlayerSettings.productName != "GoldMinerStoreDemoCapture")
            throw new InvalidOperationException("Record only in the isolated capture project.");
        if (!File.ReadAllText("Assets/Scripts/PlayAnalytics.cs").Contains("const string FormUrl = \"\";"))
            throw new InvalidOperationException("Disable analytics in the isolated capture project first.");
        string output = Environment.GetEnvironmentVariable("STORE_DEMO_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set STORE_DEMO_OUTPUT before recording.");
        Directory.CreateDirectory(output);
        string previousError = Path.Combine(output, "capture-error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        SessionState.SetBool("StoreDemoCapture.Pending", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    public static void Preview()
    {
        SessionState.SetBool("StoreDemoCapture.PreviewOnly", true);
        Begin();
    }

    public static void BeginCharacters()
    {
        SessionState.SetBool("StoreDemoCapture.Characters", true);
        Begin();
    }

    public static void PreviewNativeCharacters()
    {
        SessionState.SetBool("StoreDemoCapture.NativeLayout", true);
        PreviewCharacters();
    }

    public static void PreviewCharacters()
    {
        SessionState.SetBool("StoreDemoCapture.CharacterPreviewOnly", true);
        BeginCharacters();
    }

    [InitializeOnLoadMethod]
    static void Resume()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("StoreDemoCapture.Pending", false)) return;
        SessionState.SetBool("StoreDemoCapture.Pending", false);
        Application.logMessageReceived += FailOnError;
        new GameObject("Store Demo Recorder").AddComponent<StoreDemoCaptureDriver>();
    }

    static void FailOnError(string message, string stack, LogType type)
    {
        // Unity's optional editor search index is not used by the gameplay recorder.
        if (stack != null && stack.Contains("UnityEditor.Search")) return;
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            string output = Environment.GetEnvironmentVariable("STORE_DEMO_OUTPUT");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "capture-error.txt"), message + "\n" + stack);
            EditorApplication.delayCall += () => EditorApplication.Exit(1);
        }
    }

    IEnumerator Start()
    {
        if (SessionState.GetBool("StoreDemoCapture.CharacterPreviewOnly", false))
        {
            SessionState.SetBool("StoreDemoCapture.CharacterPreviewOnly", false);
            SessionState.SetBool("StoreDemoCapture.Characters", false);
            characterMode = true;
            yield return CaptureCharacterPreviews();
            EditorApplication.Exit(0);
            yield break;
        }
        if (SessionState.GetBool("StoreDemoCapture.PreviewOnly", false))
        {
            SessionState.SetBool("StoreDemoCapture.PreviewOnly", false);
            yield return CaptureStorePreviews();
            EditorApplication.Exit(0);
            yield break;
        }
        keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Time.timeScale = 1f;
        characterMode = SessionState.GetBool("StoreDemoCapture.Characters", false);
        SessionState.SetBool("StoreDemoCapture.Characters", false);
        if (characterMode) Time.captureDeltaTime = StoreItemDemo.Duration / StoreItemDemo.Frames;
        gameObject.AddComponent<StoreDemoFrameCapture>().driver = this;
        foreach (string offer in characterMode ? CharacterIds : Ids)
        {
            id = offer;
            BuildStage();
            yield return null; // Let the real prefab scripts finish Start before recording.
            started = characterMode ? Time.time : Time.unscaledTime;
            firstMinerFreezeAt = firstHookFreezeAt = -1f;
            previousCaptureAt = largestCaptureStep = 0f;
            captured = nextShot = 0;
            lowestTimeScale = 1f;
            boosted = false;
            skillObserved = false;
            demoSkillUses = rollDirections = 0;
            frostObserved = breathObserved = false;
            minerFrozenObserved = hookFrozenObserved = false;
            normalDistance = boostedDistance = 0f;
            recording = true;
            while (captured < StoreItemDemo.Frames) yield return null;
            recording = false;
            string output = Environment.GetEnvironmentVariable("STORE_DEMO_OUTPUT");
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, id + ".png"), atlas.EncodeToPNG());
            File.WriteAllText(Path.Combine(output, id + ".json"), JsonUtility.ToJson(new CaptureResult
            {
                item = id, frames = captured, seconds = StoreItemDemo.Duration,
                simulatedSeconds = DemoElapsed, largestCaptureStep = largestCaptureStep,
                firstMinerFreezeAt = firstMinerFreezeAt, firstHookFreezeAt = firstHookFreezeAt,
                lives = gm.Lives, bonusLives = gm.BonusLives, money = gm.PlayerMoney,
                speedMultiplier = record.getSpeedMultiplier(), noDigest = record.hasNoDigest(),
                shielded = record.isShielded(), hookShots = miner != null ? miner.hook.ShotCount : 0,
                lowestTimeScale = lowestTimeScale, normalDistance = normalDistance, boostedDistance = boostedDistance,
                skillObserved = skillObserved, hooksBlocked = player.HooksBlocked,
                skillUses = demoSkillUses, rollDirections = rollDirections,
                rockDestroyed = id == "hulk_charge" && chargeRock == null,
                greenPreserved = id == "hulk_charge" && greenBarrier != null && greenBarrier.RhinoProof,
                frostObserved = frostObserved, breathObserved = breathObserved,
                minerFrozenObserved = minerFrozenObserved, hookFrozenObserved = hookFrozenObserved
            }, true));
            ValidateCapture();
            Debug.Log("STORE_DEMO_CAPTURED " + id + " lives=" + gm.Lives + " money=" + gm.PlayerMoney);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Time.timeScale = 1f;
            Destroy(stage);
            Destroy(record.gameObject);
            Destroy(atlas);
            Destroy(frame);
            captureCamera.targetTexture = null;
            target.Release();
            Destroy(target);
            yield return null;
            yield return null;
        }
        Time.captureDeltaTime = 0f;
        if (characterMode) yield return CaptureCharacterPreviews();
        else yield return CaptureStorePreviews();
        Debug.Log("STORE_DEMOS_COMPLETE");
        EditorApplication.Exit(0);
    }

    void BuildStage()
    {
        stage = new GameObject("Actual prefabs - " + id);
        var cameraObject = new GameObject("Demo Camera");
        cameraObject.transform.SetParent(stage.transform);
        cameraObject.tag = "MainCamera";
        captureCamera = cameraObject.AddComponent<Camera>();
        captureCamera.orthographic = true;
        captureCamera.orthographicSize = id == "cat_roll" ? 3.2f : 3.6f;
        captureCamera.transform.position = new Vector3(0f, 0f, -10f);
        captureCamera.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f);
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        // Art: record on the papercraft felt floor with the game's camera grade, as the game looks
        Sprite floor = ArtLibrary.Get("MineFloor");
        if (floor != null)
        {
            captureCamera.backgroundColor = new Color(0.23f, 0.16f, 0.12f);
            var floorObject = new GameObject("Mine Floor");
            floorObject.transform.SetParent(stage.transform);
            floorObject.transform.position = new Vector3(0f, 0f, 5f);
            SpriteRenderer floorLook = floorObject.AddComponent<SpriteRenderer>();
            floorLook.sprite = floor;
            floorLook.drawMode = SpriteDrawMode.Tiled;
            floorLook.size = new Vector2(16f, 16f);
            floorLook.sortingOrder = short.MinValue;
            floorLook.color = new Color(0.92f, 0.9f, 0.88f);
        }
        var cameraData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(captureCamera);
        if (cameraData != null) cameraData.renderPostProcessing = true;
        target = new RenderTexture(Size, Size, 24);
        captureCamera.targetTexture = target;
        frame = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        atlas = new Texture2D(Size * StoreItemDemo.Columns, Size * StoreItemDemo.Rows, TextureFormat.RGB24, false);

        // Same battle rules as level_01 and the shared 0.8 game-speed setting.
        LevelData rules = LevelData.Parse(Resources.Load<TextAsset>("Levels/level_01").text);
        record = CrossLevelStatus.StartFresh(id == "cat_roll" ? typeof(CatStatus)
            : id == "hulk_charge" ? typeof(RhinoStatus) : id == "elsa_frost" ? typeof(PenguinStatus) : typeof(PigStatus));
        record.setSpeed(rules.Param("pig_speed", 5.2f) * 0.8f);
        record.setDigestTime(rules.Param("digest_time", 0.5f));
        record.setDigestPerValue(rules.Param("digest_per_value", 0.03f));
        record.setDigestMaxTime(rules.Param("digest_max", 3f));
        if (characterMode)
        {
            // Only the disposable capture project's save is modified, never the browsing player's.
            SkillUnlockSummary.UnlockForWin("level_01");
            record.setCanBlock(true);
            record.setBlockDuration(rules.Param("block_duration", .8f));
            record.setBlockCooldown(rules.Param("block_cooldown", 1f));
        }
        else
        {
            var skill = record.addSkill(id);
            skill.stock = 1;
            // Store purchases arm before the round begins. Swift has a brief normal-speed comparison.
            if (id != "swift") record.useSkill(id);
        }
        record.onLevelStarted();

        var round = new GameObject("GameManager");
        round.SetActive(false);
        round.transform.SetParent(stage.transform);
        gm = round.AddComponent<GameManager>();
        gm.startingLives = 3 + record.getExtraLives();
        gm.UseRoundTimer = false;
        gm.EndWhenTreasureGone = false;
        round.SetActive(true);
        gm.SetBonusLives(record.getBraceBonusLives());

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/"
            + (id == "cat_roll" ? "Cat" : id == "hulk_charge" ? "Rhino" : id == "elsa_frost" ? "Penguin" : "MrHyde") + ".prefab");
        player = Instantiate(prefab, stage.transform).GetComponent<PlayerCharacter>();
        player.moveSpeed = record.getSpeed() * player.speedScale;
        player.digestTime = record.getDigestTime();
        player.digestPerValue = record.getDigestPerValue();
        player.digestMaxTime = record.getDigestMaxTime();
        SetPlayer(id == "cat_roll" ? new Vector2(-1.2f, -1.2f) : id == "hulk_charge" ? new Vector2(-2.6f, -.7f)
            : id == "elsa_frost" ? new Vector2(-2.2f, -.7f)
            : new Vector2(id == "swift" || id == "ration" || id == "time_machine" ? -2.5f : 0f, -.7f));
        TimeMovesWithPlayer.ApplyStoreEffect(player, record);

        chargeRock = greenBarrier = null;
        if (id == "hulk_charge")
        {
            GameObject rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Object/Rock.prefab");
            chargeRock = Instantiate(rockPrefab, new Vector3(1.2f, -.7f, 0f), Quaternion.identity, stage.transform).GetComponent<Rock>();
            greenBarrier = Instantiate(rockPrefab, new Vector3(3f, -.7f, 0f), Quaternion.identity, stage.transform).GetComponent<Rock>();
            greenBarrier.GetComponent<SpriteRenderer>().color = MapHub.GreenRock;
            greenBarrier.hookPasses = true;
            greenBarrier.MakeWall();
        }

        if (id == "elsa_frost")
        {
            if (((PenguinController)player).SkillSlot.ActiveSkill is not ElsaColdBreathSkill)
                throw new InvalidOperationException("Elsa demo requires the actual equipped cold-breath skill.");
        }

        if (id == "ration")
        {
            var gold = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Object/Gold_Small.prefab");
            for (int i = 0; i < 4; i++)
                Instantiate(gold, new Vector3(-1.6f + i * 1.05f, -0.7f, 0f), Quaternion.identity, stage.transform);
        }
        miner = null;
        if (id == "shield" || id == "brace" || id == "time_machine" || id == "pig_block" || id == "elsa_frost")
        {
            // Same setup as LevelLoader.SetUpCompete: the real hook-hole builder and miner AI.
            MinerAI template = Resources.Load<GameObject>("Prefabs/Miner").GetComponent<MinerAI>();
            SpriteRenderer hookLook = template.hook.GetComponent<SpriteRenderer>();
            SurvivalHookAI hole = HookHole.Build("Compete Miner", id == "elsa_frost" ? new Vector2(1.8f, .2f) : new Vector2(0f, 2.1f), player.transform,
                template.telegraph.dotPrefab, HookHole.CreateSprite(HookHole.HoleColor, HookHole.RimColor),
                hookLook != null ? hookLook.sharedMaterial : null,
                rules.Param("hook_extend", 8f) * 0.8f, rules.Param("hook_retract", 8f) * 0.8f);
            GameObject holeObject = hole.gameObject;
            Hook hook = hole.hook;
            TelegraphLine telegraph = hole.telegraph;
            GameObject host = holeObject.transform.parent.gameObject;
            host.transform.SetParent(stage.transform, true);
            DestroyImmediate(hole);
            holeObject.transform.localScale = Vector3.one * rules.Param("hole_size", 0.9f);
            miner = holeObject.AddComponent<MinerAI>();
            miner.hook = hook;
            miner.telegraph = telegraph;
            miner.hookOrigin = holeObject.transform;
            miner.pig = player.transform;
            miner.autoShoot = false;
            miner.hook.extendSpeed = rules.Param("hook_extend", 8f) * 0.8f;
            miner.hook.retractSpeed = rules.Param("hook_retract", 8f) * 0.8f;
            miner.hook.hitsPig = true;
            miner.hook.grabsTreasure = false;
            host.SetActive(true);
        }
        if (characterMode) BuildDemoKey();
        else
        {
            caption = Words("Caption", new Vector2(0f, 2.95f), 3.8f);
            status = Words("Status", new Vector2(0f, -2.6f), 3.4f);
        }
    }

    void BuildDemoKey()
    {
        var key = new GameObject("K key").transform;
        key.SetParent(stage.transform, false);
        key.localPosition = new Vector3(0f, captureCamera.orthographicSize - .55f, 0f);
        Texture2D white = Texture2D.whiteTexture;
        demoKeySprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(.5f, .5f), white.width);
        var shadow = new GameObject("Key shadow");
        shadow.transform.SetParent(key, false);
        shadow.transform.localPosition = Vector3.down * .07f;
        shadow.transform.localScale = new Vector3(.84f, .86f, 1f);
        SpriteRenderer edge = shadow.AddComponent<SpriteRenderer>();
        edge.sprite = demoKeySprite;
        edge.color = new Color(.07f, .09f, .13f);
        edge.sortingOrder = 108;
        demoKeyFace = new GameObject("Pressed key face").transform;
        demoKeyFace.SetParent(key, false);
        var face = new GameObject("Key surface");
        face.transform.SetParent(demoKeyFace, false);
        face.transform.localScale = Vector3.one * .76f;
        demoKeyFill = face.AddComponent<SpriteRenderer>();
        demoKeyFill.sprite = demoKeySprite;
        demoKeyFill.color = new Color(.97f, .97f, .94f);
        demoKeyFill.sortingOrder = 109;
        var letter = new GameObject("K");
        letter.transform.SetParent(demoKeyFace, false);
        TextMeshPro text = letter.AddComponent<TextMeshPro>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        text.text = "K";
        text.fontSize = 6f;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(.12f, .13f, .16f);
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(.7f, .7f);
        text.GetComponent<MeshRenderer>().sortingOrder = 110;
    }

    void AnimateDemoKey(float t)
    {
        float elapsed = t - demoSkillAt;
        float pressed = demoSkillUses == 0 ? 0f : 1f - Mathf.InverseLerp(.14f, .28f, elapsed);
        demoKeyFace.localPosition = Vector3.down * (.07f * pressed);
        demoKeyFace.localScale = new Vector3(1f, 1f - .1f * pressed, 1f);
        demoKeyFill.color = Color.Lerp(new Color(.97f, .97f, .94f), new Color(1f, .85f, .3f), pressed);
    }

    bool CharacterTextIsKeyOnly()
    {
        TMP_Text[] labels = stage.GetComponentsInChildren<TMP_Text>(true);
        return labels.Length == 1 && labels[0].text == "K";
    }

    IEnumerator CaptureStorePreviews()
    {
        string output = Environment.GetEnvironmentVariable("STORE_DEMO_OUTPUT");
        string folder = Path.Combine(Application.dataPath, "Resources/StoreDemos");
        Directory.CreateDirectory(folder);
        foreach (string offer in Ids) File.Copy(Path.Combine(output, offer + ".png"), Path.Combine(folder, offer + ".png"), true);
        AssetDatabase.Refresh();
        foreach (string offer in Ids)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/StoreDemos/" + offer + ".png");
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        CrossLevelStatus.Ensure().setWallet(5000);
        StoreView.Show(() => { });
        StoreView view = FindFirstObjectByType<StoreView>();
        Canvas canvas = view.GetComponentInChildren<Canvas>();
        var cameraObject = new GameObject("Store Preview Camera");
        var uiCamera = cameraObject.AddComponent<Camera>();
        uiCamera.transform.position = new Vector3(0f, 0f, -10f);
        var image = new RenderTexture(1280, 720, 24);
        uiCamera.targetTexture = image;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 1f;
        yield return new WaitForSecondsRealtime(1.4f);
        Canvas.ForceUpdateCanvases();
        foreach (StoreItemHover item in view.GetComponentsInChildren<StoreItemHover>())
        {
            item.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(0.4f);
            Canvas.ForceUpdateCanvases();
            uiCamera.Render();
            RenderTexture.active = image;
            var screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            screenshot.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(output, "store-" + item.name + ".png"), screenshot.EncodeToPNG());
            Destroy(screenshot);
            item.OnPointerExit(null);
        }
        StoreView.Hide();
        uiCamera.targetTexture = null;
        image.Release();
        Destroy(image);
        Destroy(cameraObject);
        Debug.Log("STORE_DEMO_HOVER_PREVIEWS_CAPTURED");
    }

    IEnumerator CaptureCharacterPreviews()
    {
        string output = Environment.GetEnvironmentVariable("STORE_DEMO_OUTPUT");
        string folder = Path.Combine(Application.dataPath, "Resources/CharacterDemos");
        Directory.CreateDirectory(folder);
        foreach (string clip in CharacterIds) File.Copy(Path.Combine(output, clip + ".png"), Path.Combine(folder, clip + ".png"), true);
        AssetDatabase.Refresh();
        foreach (string clip in CharacterIds)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/CharacterDemos/" + clip + ".png");
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        // Verify the hover playback while both skills are still locked in this isolated save.
        ProgressStore.ClearCompleted(SkillUnlockSummary.BlockUnlockId);
        ProgressStore.ClearCompleted(SkillUnlockSummary.RollUnlockId);
        ProgressStore.ClearCompleted("battle_06");
        yield return SceneManager.LoadSceneAsync("Select", LoadSceneMode.Additive);
        yield return null;
        Camera camera = Camera.main;
        bool nativeLayout = SessionState.GetBool("StoreDemoCapture.NativeLayout", false);
        SessionState.SetBool("StoreDemoCapture.NativeLayout", false);
        var image = new RenderTexture(1280, 720, 24);
        if (!nativeLayout) camera.targetTexture = image;
        foreach (Canvas ui in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (nativeLayout) continue;
            ui.renderMode = RenderMode.ScreenSpaceCamera;
            ui.worldCamera = camera;
            ui.planeDistance = 1f;
        }
        SkillPreview[] previews = FindObjectsByType<SkillPreview>(FindObjectsSortMode.None);
        foreach (SkillPreview preview in previews) preview.enabled = false;
        foreach (SkillPreview preview in previews)
        {
            if (!CharacterSkillDemo.Supports(preview.ChampionIndex)) continue;
            string before = PlayerPrefs.GetString("gm.progress.completed");
            if (nativeLayout)
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                InputSystem.EnableDevice(mouse);
                mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = camera.WorldToScreenPoint(preview.PortraitArea.center) });
                InputSystem.Update();
                // Drive the actual hover handler synchronously so OS mouse events cannot replace the test pointer.
                foreach (SkillPreview candidate in previews) candidate.SendMessage("Update");
                yield return new WaitForSecondsRealtime(.5f);
                mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = camera.WorldToScreenPoint(preview.PortraitArea.center) });
                InputSystem.Update();
                foreach (SkillPreview candidate in previews) candidate.SendMessage("Update");
                CharacterSkillDemo live = FindFirstObjectByType<CharacterSkillDemo>();
                if (live == null) throw new InvalidOperationException("Hover did not open a demo: " + preview.ChampionIndex
                    + " mouse=" + Mouse.current.position.ReadValue() + " intended=" + camera.WorldToScreenPoint(preview.PortraitArea.center)
                    + " tutorial=" + TutorialDialogue.Playing + " console=" + DeveloperConsole.CapturesInput);
                VerifyDemoPlacement(live.GetComponent<Canvas>(), camera, preview);
                UnityEngine.UI.RawImage playback = live.GetComponentInChildren<UnityEngine.UI.RawImage>();
                if (playback == null || playback.texture == null || playback.texture.name != CharacterSkillDemo.ClipName(preview.ChampionIndex))
                    throw new InvalidOperationException("Hover opened the wrong clip: " + preview.ChampionIndex);
                Debug.Log("NATIVE_MOUSE_HOVER_PASS " + preview.ChampionIndex);
                foreach (SkillPreview candidate in previews) candidate.enabled = false;
                CharacterSkillDemo.Hide(preview);
                InputSystem.RemoveDevice(mouse);
                continue;
            }
            Renderer[] parts = preview.GetComponentsInChildren<Renderer>();
            Bounds bounds = parts[0].bounds;
            foreach (Renderer part in parts) bounds.Encapsulate(part.bounds);
            CharacterSkillDemo.Show(preview, bounds);
            Canvas overlay = FindFirstObjectByType<CharacterSkillDemo>().GetComponent<Canvas>();
            if (!nativeLayout)
            {
                overlay.renderMode = RenderMode.ScreenSpaceCamera;
                overlay.worldCamera = camera;
                overlay.planeDistance = .5f;
            }
            yield return new WaitForSecondsRealtime(1.45f);
            Canvas.ForceUpdateCanvases();
            CharacterSkillDemo.Show(preview, bounds);
            if (!nativeLayout) camera.Render();
            VerifyDemoPlacement(overlay, camera, preview);
            if (nativeLayout)
            {
                Debug.Log("NATIVE_HOVER_PASS " + preview.ChampionIndex + " screen=" + Screen.width + "x" + Screen.height);
                CharacterSkillDemo.Hide(preview);
                continue;
            }
            RenderTexture.active = image;
            var screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            screenshot.Apply();
            RenderTexture.active = null;
            string id = CharacterSkillDemo.ClipName(preview.ChampionIndex);
            File.WriteAllBytes(Path.Combine(output, "select-" + id + ".png"), screenshot.EncodeToPNG());
            Destroy(screenshot);
            CharacterSkillDemo.Hide(preview);
            if (before != PlayerPrefs.GetString("gm.progress.completed") || SkillUnlockSummary.BlockUnlocked || SkillUnlockSummary.RollUnlocked)
                throw new InvalidOperationException("Hover playback must not unlock skills or modify progress.");
        }
        camera.targetTexture = null;
        image.Release();
        Destroy(image);
        Debug.Log("CHARACTER_DEMO_HOVERS_PASS: all four enlarged previews stay clear of their complete source card, work while locked, and preserve saved progress.");
    }

    static void VerifyDemoPlacement(Canvas overlay, Camera camera, SkillPreview preview)
    {
        RectTransform panel = null;
        foreach (RectTransform child in overlay.GetComponentsInChildren<RectTransform>(true))
            if (child.name == "DemoTip") { panel = child; break; }
        if (panel == null || !panel.gameObject.activeInHierarchy)
            throw new InvalidOperationException("The skill demo must remain visible.");
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        Camera popupCamera = overlay.renderMode == RenderMode.ScreenSpaceOverlay ? null : overlay.worldCamera;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(popupCamera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(popupCamera, corners[2]);
        Rect popup = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        if (panel.sizeDelta != Vector2.one * CharacterSkillDemo.PreviewSize)
            throw new InvalidOperationException("The demo must retain its enlarged size.");
        {
            Vector2 min = camera.WorldToScreenPoint(preview.PortraitArea.min);
            Vector2 max = camera.WorldToScreenPoint(preview.PortraitArea.max);
            Rect portrait = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            if (popup.Overlaps(portrait))
                throw new InvalidOperationException("The demo covers an original portrait: " + preview.name);
        }
        foreach (RectTransform element in new[] { preview.namePlate, preview.remark, preview.selectButton })
        {
            if (element == null || !element.gameObject.activeInHierarchy) continue;
            Canvas elementCanvas = element.GetComponentInParent<Canvas>();
            Camera elementCamera = elementCanvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : elementCanvas.rootCanvas.worldCamera;
            foreach (RectTransform part in element.GetComponentsInChildren<RectTransform>())
            {
                TMP_Text text = part.GetComponent<TMP_Text>();
                if (text != null && part.GetComponent<UnityEngine.UI.Image>() == null)
                {
                    text.ForceMeshUpdate();
                    corners[0] = part.TransformPoint(text.textBounds.min);
                    corners[2] = part.TransformPoint(text.textBounds.max);
                }
                else part.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(elementCamera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(elementCamera, corners[2]);
                if (popup.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y)))
                    throw new InvalidOperationException("The demo covers its source card's " + part.name);
            }
        }
    }

    void SetPlayer(Vector2 position)
    {
        player.transform.position = position;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        body.position = position;
        body.linearVelocity = Vector2.zero;
    }

    void Update()
    {
        if (!recording) return;
        lowestTimeScale = Mathf.Min(lowestTimeScale, Time.timeScale);
        float t = DemoElapsed;
        bool move = false;
        if (characterMode)
        {
            skillObserved |= player.SkillActive;
            if (id == "elsa_frost")
            {
                var skill = ((PenguinController)player).SkillSlot.ActiveSkill as ElsaColdBreathSkill;
                breathObserved |= skill != null && skill.BreathEffect != null && skill.BreathEffect.gameObject.activeSelf;
                minerFrozenObserved |= miner != null && miner.IsFrozen;
                hookFrozenObserved |= miner != null && miner.hook != null && miner.hook.IsFrozen;
                frostObserved |= minerFrozenObserved || hookFrozenObserved;
            }
            bool cat = id == "cat_roll";
            bool press = demoSkillUses < (cat ? 2 : 1) && t >= (demoSkillUses == 0 ? .8f : 2.5f);
            if (press)
            {
                demoSkillAt = t;
                demoSkillUses++;
            }
            if ((id == "pig_block" || id == "elsa_frost") && t >= (id == "elsa_frost" ? 1.35f : .6f) && nextShot == 0)
                FireAtPlayer(true, id == "elsa_frost" ? 3f : 5.5f);
            bool steer = cat && (press || player.SkillActive);
            int turn = steer ? Mathf.Clamp((int)((t - demoSkillAt) / ((CatController)player).RollDuration * 4f), 0, 3) : 0;
            Key direction = turn == 0 ? Key.D : turn == 1 ? Key.W : turn == 2 ? Key.A : Key.S;
            AnimateDemoKey(t);
            bool chargeMove = id == "hulk_charge" && t >= .2f && (demoSkillUses == 0 || player.SkillActive);
            bool faceElsa = id == "elsa_frost" && t >= .2f && t < .35f;
            InputSystem.QueueStateEvent(keyboard, press
                ? cat ? new KeyboardState(direction, Key.K)
                    : chargeMove ? new KeyboardState(Key.D, Key.K) : new KeyboardState(Key.K)
                : steer ? new KeyboardState(direction) : chargeMove || faceElsa ? new KeyboardState(Key.D) : new KeyboardState());
            InputSystem.Update();
            keyboard.MakeCurrent();
            return;
        }
        if (id == "swift")
        {
            if (t >= 2f && !boosted)
            {
                normalDistance = player.transform.position.x + 2.5f;
                SetPlayer(new Vector2(-2.5f, -0.7f));
                record.useSkill("swift");
                boosted = true;
            }
            if (t >= 2.85f) boostedDistance = player.transform.position.x + 2.5f;
            move = t >= 0.2f && t < 0.85f || t >= 2.1f && t < 2.75f;
            caption.text = boosted ? "Swift: 2x speed" : "Normal speed";
            status.text = "Same movement time";
        }
        else if (id == "ration")
        {
            move = t > 0.4f && player.transform.position.x < 2.2f;
            caption.text = "Ration: no digestion";
            status.text = "$" + gm.PlayerMoney + "   " + (player.CanEat ? "CAN EAT" : "DIGESTING");
        }
        else if (id == "shield" || id == "brace")
        {
            caption.text = id == "shield" ? "Shield: 5s protection" : "Life: +1 bonus life";
            status.text = "LIFE " + gm.Lives + (gm.BonusLives > 0 ? "  (+1)" : "");
            float shotAt = id == "shield" ? 0.7f + nextShot * 1.5f : 1.1f;
            if (t >= shotAt && (id == "shield" ? nextShot < 2 : nextShot == 0)) FireAtPlayer();
        }
        else
        {
            move = t < 0.55f || t > 2.8f && t < 3.35f;
            caption.text = move ? "Moving: normal time" : "Still: time slows";
            status.text = "Time Machine";
            if (t >= 1.2f && nextShot == 0) FireAtPlayer();
        }
        InputSystem.QueueStateEvent(keyboard, move ? new KeyboardState(Key.D) : new KeyboardState());
        // Feed the real input reader even when the editor is hidden and has no focused Game view.
        InputSystem.Update();
        keyboard.MakeCurrent();
    }

    void FireAtPlayer(bool straightDown = false, float length = 5.5f)
    {
        Vector2 from = miner.hookOrigin.position;
        miner.hook.Fire(from, straightDown ? Vector2.down : (Vector2)player.transform.position - from, length, Color.red);
        nextShot++;
    }

    void ValidateCapture()
    {
        bool valid = captured == StoreItemDemo.Frames && gm.IsPlaying;
        if (characterMode) valid &= CharacterTextIsKeyOnly();
        if (id == "swift") valid &= Mathf.Approximately(record.getSpeedMultiplier(), CrossLevelStatus.SwiftBoost)
            && normalDistance > 1f && boostedDistance > normalDistance * 1.7f;
        if (id == "ration") valid &= record.hasNoDigest() && gm.PlayerMoney == 200 && player.CanEat;
        if (id == "shield") valid &= gm.Lives == 3 && record.isShielded() && miner.hook.ShotCount == 2;
        if (id == "brace") valid &= gm.Lives == 3 && gm.BonusLives == 0 && miner.hook.HitPlayer;
        if (id == "time_machine") valid &= lowestTimeScale <= 0.07f;
        if (id == "pig_block") valid &= skillObserved && gm.Lives == 3 && player.HooksBlocked > 0;
        // Retained previous assertion for reference. Temporarily inactive: the original cat's ground
        // pickups start digestion immediately. These older gold/hook scenarios are retained here;
        // the current cat demo instead checks changing direction during a roll.
        // if (id == "cat_roll") valid &= skillObserved && gm.Lives == 3 && gm.PlayerMoney >= 200
        //     && player.transform.position.x > 2f && !miner.hook.HitPlayer;
        // if (id == "cat_roll") valid &= skillObserved && gm.Lives == 3 && gm.PlayerMoney == 50
        //     && player.transform.position.x > 2f && !miner.hook.HitPlayer;
        if (id == "cat_roll") valid &= skillObserved && gm.Lives == 3 && demoSkillUses == 2 && rollDirections == 15;
        if (id == "hulk_charge") valid &= skillObserved && gm.Lives == 3 && demoSkillUses == 1
            && chargeRock == null && greenBarrier != null && greenBarrier.RhinoProof
            && greenBarrier.GetComponent<Collider2D>().enabled;
        if (characterMode) valid &= Mathf.Abs(DemoElapsed - StoreItemDemo.Duration) < .06f
            && largestCaptureStep <= .051f;
        if (id == "elsa_frost")
        {
            var skill = ((PenguinController)player).SkillSlot.ActiveSkill as ElsaColdBreathSkill;
            valid &= firstMinerFreezeAt >= demoSkillAt && firstHookFreezeAt >= demoSkillAt;
            valid &= skillObserved && breathObserved && frostObserved && gm.Lives == 3 && demoSkillUses == 1
                && skill != null && Mathf.Approximately(skill.skillCooldown, 6f)
                && Mathf.Approximately(FrostField2D.FrostDuration, 5f)
                && miner != null && miner.hook.ShotCount > 0 && miner.IsFrozen && miner.hook.IsFrozen;
        }
        if (!valid) throw new InvalidOperationException("Store demo behavior check failed: " + id);
    }

    // Called after skills and FrostField2D.LateUpdate, matching the real camera's final state.
    public void CaptureFrame()
    {
        if (!recording) return;
        if (id == "elsa_frost")
        {
            bool minerFrozen = miner != null && miner.IsFrozen;
            bool hookFrozen = miner != null && miner.hook != null && miner.hook.IsFrozen;
            if (minerFrozen && firstMinerFreezeAt < 0f) firstMinerFreezeAt = DemoElapsed;
            if (hookFrozen && firstHookFreezeAt < 0f) firstHookFreezeAt = DemoElapsed;
            minerFrozenObserved |= minerFrozen;
            hookFrozenObserved |= hookFrozen;
            frostObserved |= minerFrozen || hookFrozen;
        }
        if (id == "cat_roll" && player.SkillActive)
        {
            Vector2 velocity = player.GetComponent<Rigidbody2D>().linearVelocity;
            if (velocity.x > .1f) rollDirections |= 1;
            if (velocity.y > .1f) rollDirections |= 2;
            if (velocity.x < -.1f) rollDirections |= 4;
            if (velocity.y < -.1f) rollDirections |= 8;
        }
        if (!characterMode && DemoElapsed < captured / 20f) return;
        largestCaptureStep = Mathf.Max(largestCaptureStep, DemoElapsed - previousCaptureAt);
        previousCaptureAt = DemoElapsed;
        captureCamera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        frame.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        frame.Apply();
        RenderTexture.active = previous;
        atlas.SetPixels(captured % StoreItemDemo.Columns * Size,
            (StoreItemDemo.Rows - 1 - captured / StoreItemDemo.Columns) * Size, Size, Size, frame.GetPixels());
        captured++;
    }

    TextMeshPro Words(string name, Vector2 at, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(stage.transform);
        go.transform.position = at;
        var text = go.AddComponent<TextMeshPro>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(7f, 0.7f);
        text.GetComponent<MeshRenderer>().sortingOrder = 100;
        return text;
    }

    [Serializable]
    class CaptureResult
    {
        public string item;
        public int frames;
        public float seconds, simulatedSeconds, largestCaptureStep, firstMinerFreezeAt, firstHookFreezeAt;
        public int lives, bonusLives, money, hookShots;
        public float speedMultiplier;
        public float lowestTimeScale;
        public float normalDistance, boostedDistance;
        public bool noDigest, shielded;
        public bool skillObserved;
        public int hooksBlocked;
        public int skillUses, rollDirections;
        public bool rockDestroyed, greenPreserved;
        public bool frostObserved, breathObserved;
        public bool minerFrozenObserved, hookFrozenObserved;
    }
}
// Input runs early on the driver; recording runs after the actual skill/frost updates.
[DefaultExecutionOrder(2000)]
public sealed class StoreDemoFrameCapture : MonoBehaviour
{
    public StoreDemoCaptureDriver driver;
    void LateUpdate() { if (driver != null) driver.CaptureFrame(); }
}
#endif
