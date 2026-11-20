using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Rebuild explicitly: never changes a scene just because the editor recompiles.
public static class GeometricCharactersBuilder
{
    const string Teams = "Assets/Prefabs/Teams/";
    static Sprite circle, square, triangle;
    static readonly Color Ink = Hex("211F20"), Orange = Hex("FF8C2B"), Blue = Hex("355CAF"), Cyan = Hex("45B5CD");

    public static void BuildAndVerify()
    {
        Build();
        WalletAndMovementSmokeTest.Run();
    }

    [MenuItem("Tools/Gold Miner/Build Elsa And Hulk")]
    public static void Build()
    {
        circle = SpriteAt("Circle"); square = SpriteAt("Square"); triangle = SpriteAt("Triangle");
        Make("Penguin", true);
        Make("Rhino", false);
        foreach (string name in new[] { "MrHyde", "Cat", "Squirrel", "Frog" }) UpdateExistingEyeCatchlights(name);
        BuildSelect();
        foreach (string path in new[] { "Assets/Scenes/Game.unity", "Assets/Scenes/Survival.unity" })
        {
            EditorSceneManager.OpenScene(path);
            foreach (ChampionSpawner spawner in Object.FindObjectsByType<ChampionSpawner>(FindObjectsSortMode.None))
            { spawner.champions = Extend(spawner.champions); ValidateRoster(spawner.champions); }
            foreach (SurvivalLoader loader in Object.FindObjectsByType<SurvivalLoader>(FindObjectsSortMode.None))
            { loader.champions = Extend(loader.champions); ValidateRoster(loader.champions); }
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/Select.unity");
        Validate();
        Capture();
        CapturePortraits();
        Debug.Log("GEOMETRIC_CHARACTERS_PASS: six selectable portraits, separate controllers and records, Game/Survival references, editable sprite and UI prefabs.");
    }

    static GameObject[] Extend(GameObject[] original)
    {
        var result = new GameObject[6];
        Array.Copy(original, result, Mathf.Min(original.Length, result.Length));
        result[4] = AssetDatabase.LoadAssetAtPath<GameObject>(Teams + "Penguin.prefab");
        result[5] = AssetDatabase.LoadAssetAtPath<GameObject>(Teams + "Rhino.prefab");
        return result;
    }

    static Sprite SpriteAt(string name) => AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/" + name + ".png").OfType<Sprite>().First();
    static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out Color c); return c; }

    static void Make(string name, bool penguin)
    {
        var root = new GameObject(name);
        var visual = new GameObject("Artwork");
        visual.transform.SetParent(root.transform, false);
        // The source drawing faces left. Gameplay's default facing is right.
        visual.transform.localScale = new Vector3(-1, 1, 1);
        Transform art = visual.transform;
        SpriteRenderer body;
        if (penguin)
        {
            body = Oval(art, "Body", .10f, .07f, 1.12f, 1.12f, Ink, 0);
            Oval(art, "White Bib", -.135f, .045f, .51f, .85f, Color.white, 1);
            // Equal mandibles narrow toward the tip but keep a visible opening.
            // The dark pocket stays close to the head, behind the bright blue tongue.
            Oval(art, "Mouth Interior", -.45f, .025f, .105f, .12f, Ink, 2);
            Capsule(art, "Blue Tongue", -.495f, .025f, .145f, .065f, 0, Hex("159CFF"), 3);
            BeakHalf(art, "Upper Beak", new Vector2(-.42f, .115f), 101.4f);
            BeakHalf(art, "Lower Beak", new Vector2(-.42f, -.065f), 78.6f);
            Oval(art, "Gold Eye Ring", -.20f, .17f, .257f, .275f, Hex("F3D142"), 4);
            Oval(art, "Eye", -.20f, .17f, .204f, .217f, Ink, 5);
            Oval(art, "Eye Catchlight", -.234f, .213f, .044f, .044f, Color.white, 6);
            // Keep both arcs convex: a complete tilted ellipse, with a thin black
            // rim on the right and its lower end hidden behind the scarf fold.
            Part(art, "Orange Head Patch", circle, .51f, -.01f, .19f, .59f, -12, Orange, 3);
            Capsule(art, "Scarf Front", -.02f, -.39f, .88f, .20f, -16, Blue, 6);
            Capsule(art, "Scarf Fold", .20f, -.43f, .72f, .20f, 26, Blue, 7);
            Capsule(art, "Scarf End", .13f, -.51f, .46f, .19f, 24, Blue, 8);
        }
        else
        {
            Capsule(art, "Back Ear", .49f, .29f, .35f, .13f, 75, Cyan, 0);
            Capsule(art, "Top Ear", .04f, .37f, .35f, .13f, 75, Cyan, 0);
            body = Oval(art, "Head", .12f, -.03f, 1.20f, .90f, Cyan, 1);
            Part(art, "Large Horn", triangle, -.56f, .08f, .36f, .69f, 37, Cyan, 2);
            Part(art, "Small Horn", triangle, -.31f, .15f, .23f, .40f, 43, Cyan, 3);
            Oval(art, "Muzzle", -.29f, -.24f, .95f, .49f, Cyan, 4);
            Capsule(art, "Eye", .17f, .08f, .22f, .14f, -12, Ink, 5);
            Oval(art, "Eye Catchlight", .12f, .11f, .035f, .035f, Color.white, 6);
            Capsule(art, "Nostril", -.51f, -.14f, .085f, .046f, 12, Ink, 5);
            // The left cap meets the muzzle outline and sits below the right corner.
            CurvedMouth(art, new Vector2(-.704f, -.34f), new Vector2(-.10f, -.49f), new Vector2(.42f, -.26f));
        }
        var rb = root.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0; rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var collider = root.AddComponent<BoxCollider2D>();
        collider.size = penguin ? new Vector2(.88f, 1.02f) : new Vector2(1.23f, .78f);
        collider.offset = penguin ? Vector2.zero : new Vector2(.01f, -.06f);
        PlayerCharacter controller = penguin ? (PlayerCharacter)root.AddComponent<PenguinController>() : root.AddComponent<RhinoController>();
        controller.body = body;
        if (penguin) root.AddComponent<ElsaMouthPose>();
        if (controller is PenguinController elsa)
        {
            elsa.visualRoot = art;
        }
        PrefabUtility.SaveAsPrefabAsset(root, Teams + name + ".prefab");

        // A reusable canvas portrait, using exactly the same editable pieces.
        RectTransform ui = UISpriteCopy.Build(root, null, 200);
        ui.name = name + "UI";
        ui.localScale = new Vector3(-1, 1, 1);
        if (penguin) ui.gameObject.AddComponent<ElsaMouthPose>();
        PrefabUtility.SaveAsPrefabAsset(ui.gameObject, Teams + name + "UI.prefab");
        Object.DestroyImmediate(ui.gameObject);
        Object.DestroyImmediate(root);
    }

    static SpriteRenderer Oval(Transform parent, string name, float x, float y, float w, float h, Color color, int order)
        => Part(parent, name, circle, x, y, w, h, 0, color, order);

    static void BeakHalf(Transform parent, string name, Vector2 atBase, float angle)
    {
        Vector2[] vertices = triangle.vertices;
        float minX = vertices.Min(v => v.x), maxX = vertices.Max(v => v.x);
        float minY = vertices.Min(v => v.y), maxY = vertices.Max(v => v.y);
        Vector3 scale = new Vector3(.09f / (maxX - minX), .335f / (maxY - minY), 1);
        Quaternion turn = Quaternion.Euler(0, 0, angle);
        Vector3 baseOffset = turn * Vector3.Scale(new Vector3((minX + maxX) * .5f, minY, 0), scale);
        SpriteRenderer sr = Part(parent, name, triangle, 0, 0, 1, 1, angle, Orange, 4);
        sr.transform.localScale = scale;
        sr.transform.localPosition = (Vector3)atBase - baseOffset;
    }

    static void UpdateExistingEyeCatchlights(string name)
    {
        string path = Teams + name + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            AddEyeCatchlights(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Gold Miner/Move Cat Catchlights Lower Right")]
    public static void UpdateCatCatchlights()
    {
        circle = SpriteAt("Circle");
        UpdateExistingEyeCatchlights("Cat");
        EditorSceneManager.OpenScene("Assets/Scenes/Select.unity");
        SkillPreview portrait = Object.FindObjectsByType<SkillPreview>(FindObjectsSortMode.None)
            .Single(p => p.skill == SkillPreview.Skill.Roll);
        AddEyeCatchlights(portrait.gameObject);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Capture();
        Debug.Log("CAT_CATCHLIGHT_PASS: updated the cat prefab and select portrait; other characters were not rebuilt.");
    }

    static void AddEyeCatchlights(GameObject root)
    {
        CatController cat = root.GetComponent<CatController>();
        SkillPreview portrait = root.GetComponent<SkillPreview>();
        bool lowerRight = cat != null || (portrait != null && portrait.skill == SkillPreview.Skill.Roll);
        string[] names = { "EyeL", "EyeR", "Eye Left", "Eye Right", "Eye L", "Eye R" };
        var eyes = root.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name))
            .Select(t => t.GetComponent<SpriteRenderer>() != null ? t.GetComponent<SpriteRenderer>() : t.GetComponentInChildren<SpriteRenderer>(true))
            .Where(sr => sr != null).ToList();
        // The cat portrait is an older flattened drawing: its two black circles
        // have no EyeL/EyeR objects. Their colour and shape distinguish them from the ears/body.
        if (eyes.Count == 0)
            eyes.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true).Where(sr => sr.sprite != null
                && sr.color.r < .3f && sr.color.g < .3f && sr.color.b < .3f
                && sr.transform.localScale.x > .07f && sr.transform.localScale.x < .20f));
        Debug.Log("EYE_HIGHLIGHTS " + root.name + ": " + eyes.Count);
        foreach (SpriteRenderer blackEye in eyes.Distinct())
        {
            Bounds b = blackEye.sprite.bounds;
            float offsetX = (lowerRight ? .18f : -.18f) * b.size.x;
            float offsetY = (lowerRight ? -.20f : .20f) * b.size.y;
            Transform existing = blackEye.transform.Find("Catchlight");
            if (existing != null)
            {
                if (lowerRight)
                    existing.localPosition = new Vector3(b.center.x + offsetX, b.center.y + offsetY, existing.localPosition.z);
                continue;
            }
            // Keep the existing eye object so tired-eye switching and heading animation
            // also control its catchlight. Preserve the black sprite, size and colour.
            SpriteRenderer glint = Oval(blackEye.transform, "Catchlight", b.center.x + offsetX,
                b.center.y + offsetY, b.size.x * .24f, b.size.y * .24f,
                Color.white, blackEye.sortingOrder + 1);
            glint.sharedMaterial = blackEye.sharedMaterial;
        }
    }

    static SpriteRenderer Part(Transform parent, string name, Sprite sprite, float x, float y, float w, float h, float angle, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, 0);
        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        go.transform.localScale = new Vector3(w / sprite.bounds.size.x, h / sprite.bounds.size.y, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.color = color; sr.sortingOrder = order;
        return sr;
    }

    static void Capsule(Transform parent, string name, float x, float y, float w, float h, float angle, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, 0);
        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        Part(go.transform, "Middle", square, 0, 0, Mathf.Max(.001f, w - h), h, 0, color, order);
        Oval(go.transform, "Left Cap", -(w - h) / 2, 0, h, h, color, order);
        Oval(go.transform, "Right Cap", (w - h) / 2, 0, h, h, color, order);
    }

    static void CurvedMouth(Transform parent, Vector2 start, Vector2 control, Vector2 end)
    {
        var mouth = new GameObject("Curved Smile");
        mouth.transform.SetParent(parent, false);
        const int segments = 24;
        const float thickness = .043f;
        Vector2 At(float t) => (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control + t * t * end;
        Vector2 from = start;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector2 to = At(t);
            Vector2 mid = (from + to) * .5f;
            Vector2 delta = to - from;
            Capsule(mouth.transform, "Smile Segment " + i.ToString("00"), mid.x, mid.y,
                delta.magnitude + thickness, thickness, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, Ink, 6);
            from = to;
        }
    }

    static void BuildSelect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Select.unity");
        ChampionSelect select = Object.FindFirstObjectByType<ChampionSelect>();
        SkillPreview[] previous = Object.FindObjectsByType<SkillPreview>(FindObjectsSortMode.None);
        SkillPreview template = previous.First(p => p.skill == SkillPreview.Skill.Block);
        for (int i = 4; i < 6; i++)
        {
            // Safe to rerun: keep existing cards and their persistent button binding.
            string name = i == 4 ? "Penguin" : "Rhino";
            string displayName = i == 4 ? "Elsa" : "Hulk";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Teams + name + ".prefab");
            SkillPreview existing = previous.FirstOrDefault(p => p.selectButton != null && p.selectButton.name == "Select " + i);
            if (existing != null)
            {
                existing.name = displayName;
                existing.namePlate.name = displayName;
                existing.namePlate.GetComponentInChildren<TMP_Text>(true).text = displayName;
                // Portraits have no controller. Refresh their artwork when proportions change.
                for (int child = existing.transform.childCount - 1; child >= 0; child--)
                    Object.DestroyImmediate(existing.transform.GetChild(child).gameObject);
                Object.Instantiate(prefab.transform.GetChild(0).gameObject, existing.transform, false);
                continue;
            }
            GameObject portrait = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(portrait, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            portrait.name = displayName;
            Object.DestroyImmediate(portrait.GetComponent<PlayerCharacter>());
            Object.DestroyImmediate(portrait.GetComponent<Collider2D>());
            Object.DestroyImmediate(portrait.GetComponent<Rigidbody2D>());
            var preview = portrait.AddComponent<SkillPreview>();
            preview.skill = SkillPreview.Skill.Portrait;
            preview.namePlate = Object.Instantiate(template.namePlate, template.namePlate.parent);
            preview.namePlate.name = displayName;
            // Name plates in the original scene also have Button components, with no action.
            preview.namePlate.GetComponentInChildren<TMP_Text>(true).text = displayName;
            preview.remark = Object.Instantiate(template.remark, template.remark.parent);
            preview.remark.name = displayName + " Remark";
            preview.remark.GetComponentInChildren<TMP_Text>(true).text = i == 4 ? "The penguin." : "The rhinoceros.";
            preview.selectButton = Object.Instantiate(template.selectButton, template.selectButton.parent);
            preview.selectButton.name = "Select " + i;
            Button button = preview.selectButton.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick, new UnityAction<int>(select.Choose), i);
        }
        Canvas canvas = template.namePlate.GetComponentInParent<Canvas>();
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true))
            if (text.text.Trim() == "WHO ARE U")
            {
                Place(text.rectTransform, 0, 310, 600, 60);
                text.enableAutoSizing = false; text.fontSize = 48; text.alignment = TextAlignmentOptions.Center;
            }
        Camera camera = Camera.main;
        camera.aspect = 16f / 9;
        foreach (SkillPreview preview in Object.FindObjectsByType<SkillPreview>(FindObjectsSortMode.None))
        {
            int index = int.Parse(preview.selectButton.name.Substring(7));
            if (index < 4) AddEyeCatchlights(preview.gameObject);
            // Keep purchasable characters ahead of the two mystery slots.
            int slot = index switch { 2 => 4, 3 => 5, 4 => 3, 5 => 2, _ => index };
            float x = (slot % 3 - 1) * 380f;
            float offset = slot / 3 * -296f;
            Place(preview.namePlate, x, 73 + offset, 250, 40);
            Place(preview.remark, x, 33 + offset, 340, 30);
            Place(preview.selectButton, x, -6 + offset, 160, 30);
            foreach (TMP_Text text in preview.remark.GetComponentsInChildren<TMP_Text>(true))
            { text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = 17; text.alignment = TextAlignmentOptions.Center; }
            Transform root = preview.transform;
            root.localScale = new Vector3(index >= 4 ? -1 : 1, 1, 1);
            Bounds bounds = Outline(root.gameObject);
            float worldPerPixel = camera.orthographicSize * 2 / 720;
            float scale = Mathf.Min(210 * worldPerPixel / bounds.size.x, 155 * worldPerPixel / bounds.size.y);
            root.localScale *= scale;
            bounds = Outline(root.gameObject);
            Vector3 centre = camera.ViewportToWorldPoint(new Vector3(.5f + x / 1280, .5f + (178 + offset) / 720, -camera.transform.position.z));
            root.position += centre - bounds.center;
        }
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
    }

    static Bounds Outline(GameObject root)
    {
        SpriteRenderer[] parts = root.GetComponentsInChildren<SpriteRenderer>();
        Bounds bounds = parts[0].bounds;
        foreach (SpriteRenderer sr in parts) bounds.Encapsulate(sr.bounds);
        return bounds;
    }

    static void Validate()
    {
        if (Object.FindObjectsByType<SkillPreview>(FindObjectsSortMode.None).Length != 6)
            throw new Exception("Expected six character cards.");
        for (int i = 4; i < 6; i++)
        {
            Button button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Select " + i);
            if (button.onClick.GetPersistentEventCount() != 1 || button.onClick.GetPersistentMethodName(0) != "Choose")
                throw new Exception("Missing select action: " + i);
            var serialized = new SerializedObject(button);
            if (serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0)
                .FindPropertyRelative("m_Arguments.m_IntArgument").intValue != i)
                throw new Exception("Incorrect character index: " + i);
            if (ChampionSpawner.RecordType(i) == typeof(PigStatus)) throw new Exception("Missing character record.");
        }
    }

    static void ValidateRoster(GameObject[] roster)
    {
        if (roster.Length != 6 || roster.Any(p => p == null)) throw new Exception("Incomplete champion roster.");
        if (roster[4].GetComponent<PenguinController>() == null || roster[5].GetComponent<RhinoController>() == null)
            throw new Exception("Incorrect new character controllers.");
    }

    static void CapturePortraits()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Portrait Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true; camera.orthographicSize = .90f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
        for (int i = 0; i < 2; i++)
        {
            string name = i == 0 ? "Penguin" : "Rhino";
            var portrait = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Teams + name + ".prefab"));
            portrait.transform.position = new Vector3(i == 0 ? -1f : .98f, .02f, 0);
            portrait.transform.localScale = new Vector3(-1.05f, 1.05f, 1);
        }
        var rt = new RenderTexture(1200, 550, 24);
        var image = new Texture2D(1200, 550, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1200, 550), 0, 0); image.Apply();
            File.WriteAllBytes("artifacts/characters/character-portraits.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            EditorSceneManager.OpenScene("Assets/Scenes/Select.unity");
        }
    }

    static void Capture()
    {
        // Render the actual scene including its overlay canvas for review.
        Directory.CreateDirectory("artifacts/characters");
        Camera camera = Camera.main;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        RenderMode mode = canvas.renderMode;
        Camera oldCamera = canvas.worldCamera;
        float oldPlane = canvas.planeDistance;
        var rt = new RenderTexture(1600, 900, 24);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            camera.targetTexture = rt;
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes("artifacts/characters/character-select.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        }
    }
}
