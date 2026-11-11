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
        foreach (string offer in Ids)
        {
            id = offer;
            BuildStage();
            yield return null; // Let the real prefab scripts finish Start before recording.
            started = Time.unscaledTime;
            captured = nextShot = 0;
            lowestTimeScale = 1f;
            boosted = false;
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
                lives = gm.Lives, bonusLives = gm.BonusLives, money = gm.PlayerMoney,
                speedMultiplier = record.getSpeedMultiplier(), noDigest = record.hasNoDigest(),
                shielded = record.isShielded(), hookShots = miner != null ? miner.hook.ShotCount : 0,
                lowestTimeScale = lowestTimeScale, normalDistance = normalDistance, boostedDistance = boostedDistance
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
        yield return CaptureStorePreviews();
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
        captureCamera.orthographicSize = 3.6f;
        captureCamera.transform.position = new Vector3(0f, 0f, -10f);
        captureCamera.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f);
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        target = new RenderTexture(Size, Size, 24);
        captureCamera.targetTexture = target;
        frame = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        atlas = new Texture2D(Size * StoreItemDemo.Columns, Size * StoreItemDemo.Rows, TextureFormat.RGB24, false);

        // Same battle rules as level_01 and the shared 0.8 game-speed setting.
        LevelData rules = LevelData.Parse(Resources.Load<TextAsset>("Levels/level_01").text);
        record = CrossLevelStatus.StartFresh(typeof(PigStatus));
        record.setSpeed(rules.Param("pig_speed", 5.2f) * 0.8f);
        record.setDigestTime(rules.Param("digest_time", 0.5f));
        record.setDigestPerValue(rules.Param("digest_per_value", 0.03f));
        record.setDigestMaxTime(rules.Param("digest_max", 3f));
        var skill = record.addSkill(id);
        skill.stock = 1;
        // Store purchases arm before the round begins. Swift has a brief normal-speed comparison.
        if (id != "swift") record.useSkill(id);
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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/MrHyde.prefab");
        player = Instantiate(prefab, stage.transform).GetComponent<PlayerCharacter>();
        player.moveSpeed = record.getSpeed() * player.speedScale;
        player.digestTime = record.getDigestTime();
        player.digestPerValue = record.getDigestPerValue();
        player.digestMaxTime = record.getDigestMaxTime();
        SetPlayer(new Vector2(id == "swift" || id == "ration" || id == "time_machine" ? -2.5f : 0f, -0.7f));
        TimeMovesWithPlayer.ApplyStoreEffect(player, record);

        if (id == "ration")
        {
            var gold = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Object/Gold_Small.prefab");
            for (int i = 0; i < 4; i++)
                Instantiate(gold, new Vector3(-1.6f + i * 1.05f, -0.7f, 0f), Quaternion.identity, stage.transform);
        }
        miner = null;
        if (id == "shield" || id == "brace" || id == "time_machine")
        {
            // Same setup as LevelLoader.SetUpCompete: the real hook-hole builder and miner AI.
            MinerAI template = Resources.Load<GameObject>("Prefabs/Miner").GetComponent<MinerAI>();
            SpriteRenderer hookLook = template.hook.GetComponent<SpriteRenderer>();
            SurvivalHookAI hole = HookHole.Build("Compete Miner", new Vector2(0f, 2.1f), player.transform,
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
        caption = Words("Caption", new Vector2(0f, 2.95f), 3.8f);
        status = Words("Status", new Vector2(0f, -2.6f), 3.4f);
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
        float t = Time.unscaledTime - started;
        bool move = false;
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

    void FireAtPlayer()
    {
        Vector2 from = miner.hookOrigin.position;
        miner.hook.Fire(from, (Vector2)player.transform.position - from, 5.5f, Color.red);
        nextShot++;
    }

    void ValidateCapture()
    {
        bool valid = captured == StoreItemDemo.Frames && gm.IsPlaying;
        if (id == "swift") valid &= Mathf.Approximately(record.getSpeedMultiplier(), CrossLevelStatus.SwiftBoost)
            && normalDistance > 1f && boostedDistance > normalDistance * 1.7f;
        if (id == "ration") valid &= record.hasNoDigest() && gm.PlayerMoney == 200 && player.CanEat;
        if (id == "shield") valid &= gm.Lives == 3 && record.isShielded() && miner.hook.ShotCount == 2;
        if (id == "brace") valid &= gm.Lives == 3 && gm.BonusLives == 0 && miner.hook.HitPlayer;
        if (id == "time_machine") valid &= lowestTimeScale <= 0.07f;
        if (!valid) throw new InvalidOperationException("Store demo behavior check failed: " + id);
    }

    void LateUpdate()
    {
        if (!recording || Time.unscaledTime - started < captured / 20f) return;
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
        public float seconds;
        public int lives, bonusLives, money, hookShots;
        public float speedMultiplier;
        public float lowestTimeScale;
        public float normalDistance, boostedDistance;
        public bool noDigest, shielded;
    }
}
#endif
