using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Tutorial ... (also GameObject > Gold Miner): put the wordless-tutorial props
// into the open level scene, built from the project's basic shape sprites.
//   Tutorial Step      a TutorialStep under the scene's "Tutorial" object, with a Hole and a
//                      Callout. The first step starts with the level and its Hole follows the
//                      player; later ones start after the step above them and light a fixed spot
//   Tutorial Hole      another lit spot in the selected step (stays where you put it)
//   Tutorial Callout   another arrow + label in the selected step
//   Tutorial UI Hole   lights a HUD element in the selected step (TutorialUIHole)
//   Tutorial UI Callout  an arrow + label on the selected step's screen canvas, drawn above the HUD
//   Tutorial Story     a step with a TutorialDialogue: a conversation when the round is won
//   Tutorial Arrow / Tutorial Spotlight   the one-off intro props (TutorialArrow, TutorialSpotlight)
public static class TutorialPropsMenu
{
    const int DarkOrder = 100;    // above the arena (items, characters, hooks)
    const int ArrowOrder = 110;   // above the dark
    const int UIOverlayOrder = 510;   // above the HUD and TutorialUIDark (500)
    static readonly Color ArrowColor = new Color(1f, 0.85f, 0.2f);

    [MenuItem("Tools/Gold Miner/Tutorial Step", false, 100)]
    [MenuItem("GameObject/Gold Miner/Tutorial Step", false, 10)]
    static void CreateStep(MenuCommand command)
    {
        Transform tutorial = TutorialRoot();
        int number = tutorial.GetComponentsInChildren<TutorialStep>(true).Length + 1;

        var go = new GameObject("Step " + number);
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Step");
        go.transform.SetParent(tutorial, false);
        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = number == 1 ? TutorialStep.StartTrigger.LevelStart : TutorialStep.StartTrigger.AfterStep;

        // Step 1 introduces the player, so its light follows them. Later steps light a spot: the
        // Hole stays where it is put (it starts in the middle of the Scene view)
        bool first = number == 1;
        Vector3 at = first ? PlayerStart() : ViewCentre();
        GameObject hole = CreateHoleObject(go.transform, at);
        hole.GetComponent<TutorialFollow>().followPlayer = first;
        CreateCalloutObject(go.transform, at + new Vector3(0f, 1.2f, 0f), first ? "You" : "Text");

        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Gold Miner/Tutorial Hole", false, 101)]
    [MenuItem("GameObject/Gold Miner/Tutorial Hole", false, 11)]
    static void CreateHole(MenuCommand command)
    {
        TutorialStep step = SelectedStep(command);
        if (step == null) return;
        GameObject hole = CreateHoleObject(step.transform, ViewCentre());
        Undo.RegisterCreatedObjectUndo(hole, "Create Tutorial Hole");
        Selection.activeGameObject = hole;
    }

    [MenuItem("Tools/Gold Miner/Tutorial Callout", false, 102)]
    [MenuItem("GameObject/Gold Miner/Tutorial Callout", false, 12)]
    static void CreateCallout(MenuCommand command)
    {
        TutorialStep step = SelectedStep(command);
        if (step == null) return;
        GameObject callout = CreateCalloutObject(step.transform, ViewCentre(), "Text");
        Undo.RegisterCreatedObjectUndo(callout, "Create Tutorial Callout");
        Selection.activeGameObject = callout;
    }

    [MenuItem("Tools/Gold Miner/Tutorial UI Hole", false, 103)]
    [MenuItem("GameObject/Gold Miner/Tutorial UI Hole", false, 13)]
    static void CreateUIHole(MenuCommand command)
    {
        TutorialStep step = SelectedStep(command);
        if (step == null) return;
        var go = new GameObject("UIHole");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial UI Hole");
        go.transform.SetParent(step.transform, false);
        go.AddComponent<TutorialUIHole>();
        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Gold Miner/Tutorial UI Callout", false, 104)]
    [MenuItem("GameObject/Gold Miner/Tutorial UI Callout", false, 14)]
    static void CreateUICallout(MenuCommand command)
    {
        TutorialStep step = SelectedStep(command);
        if (step == null) return;
        RectTransform overlay = StepOverlay(step);

        var go = new GameObject("UICallout", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial UI Callout");
        var rect = (RectTransform)go.transform;
        rect.SetParent(overlay, false);
        rect.sizeDelta = new Vector2(100f, 150f);
        rect.anchoredPosition = Vector2.zero;   // the middle of the screen; drag it next to the HUD part

        // The arrow points down at the callout's origin; rotate the Arrow child (Z) to aim it
        var arrowObject = new GameObject("Arrow", typeof(RectTransform));
        arrowObject.transform.SetParent(rect, false);
        TutorialArrow arrow = arrowObject.AddComponent<TutorialArrow>();
        arrow.withSpotlight = false;   // the step fades it
        arrow.bob = 20f;               // canvas units (1080p pixels), not world units
        var visual = new GameObject("Visual", typeof(RectTransform)).GetComponent<RectTransform>();
        visual.SetParent(arrowObject.transform, false);
        arrow.visual = visual;
        Image head = UIShape(visual, "Head", "Triangle", new Vector2(0f, 25f), new Vector2(60f, 50f));
        head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        UIShape(visual, "Shaft", "Square", new Vector2(0f, 85f), new Vector2(22f, 75f));

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(rect, false);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(40f, 90f);
        labelRect.sizeDelta = new Vector2(400f, 60f);
        var text = label.GetComponent<TextMeshProUGUI>();
        text.text = "Text";
        text.fontSize = 40f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        Selection.activeGameObject = go;
    }

    // The step's screen canvas for UI callouts (made the first time): drawn above the HUD and the
    // tutorial's dark, laid out at 1920x1080 like the HUD, so positions line up with the HUD's
    static RectTransform StepOverlay(TutorialStep step)
    {
        foreach (Transform child in step.transform)
            if (child.GetComponent<Canvas>() != null) return (RectTransform)child;

        var go = new GameObject("UIOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial UI Overlay");
        go.transform.SetParent(step.transform, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UIOverlayOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // what ScreenFrame sets at runtime
        go.GetComponent<CanvasGroup>().blocksRaycasts = false;          // never blocks a click
        return (RectTransform)go.transform;
    }

    static Image UIShape(Transform parent, string name, string sprite, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.sprite = LoadSprite(sprite);
        image.color = ArrowColor;
        image.raycastTarget = false;
        return image;
    }

    // A step that tells a story when the round is won: the EvilMeowmeow appears and talks with the
    // player, then the level ends. Change the trigger, characters and lines in the Inspector
    [MenuItem("Tools/Gold Miner/Tutorial Story", false, 105)]
    [MenuItem("GameObject/Gold Miner/Tutorial Story", false, 15)]
    static void CreateStory(MenuCommand command)
    {
        Transform tutorial = TutorialRoot();
        var go = new GameObject("Story");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Story");
        go.transform.SetParent(tutorial, false);

        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = "round_won";
        step.endOn = TutorialStep.EndTrigger.Manual;   // the conversation ends it
        step.maxTimes = 1;

        // The speaker's spotlight, moved by the dialogue; and where the enemy appears
        var light = new GameObject("SpeakerLight");
        light.transform.SetParent(go.transform, false);
        light.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
        SpriteMask mask = light.AddComponent<SpriteMask>();
        mask.sprite = LoadSprite("Circle");
        var spot = new GameObject("EnemySpot");
        spot.transform.SetParent(go.transform, false);
        spot.transform.position = new Vector3(4f, 1f, 0f);

        TutorialDialogue dialogue = go.AddComponent<TutorialDialogue>();
        dialogue.speakerLight = mask;
        dialogue.characters = new[]
        {
            new TutorialDialogue.Character { name = "Evil Meowmeow", prefab = FindPrefab("EvilMeowmeow"), spawnAt = spot.transform,
                                             nameColor = new Color(1f, 0.45f, 0.45f) },
            new TutorialDialogue.Character { name = "You", isPlayer = true, nameColor = new Color(1f, 0.72f, 0.82f) },
        };
        dialogue.lines = new[]
        {
            new TutorialDialogue.Line { speaker = "Evil Meowmeow", text = "So... you ate all of MY gold." },
            new TutorialDialogue.Line { speaker = "You", text = "It was just lying there!" },
            new TutorialDialogue.Line { speaker = "Evil Meowmeow", text = "Then let's see how you do against my miners. Mwahaha!" },
        };

        Selection.activeGameObject = go;
    }

    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    [MenuItem("Tools/Gold Miner/Tutorial Arrow", false, 120)]
    [MenuItem("GameObject/Gold Miner/Tutorial Arrow", false, 20)]
    static void CreateArrow(MenuCommand command)
    {
        GameObject root = BuildArrow(null, true);
        Place(root, command, "Tutorial Arrow");
    }

    [MenuItem("Tools/Gold Miner/Tutorial Spotlight", false, 121)]
    [MenuItem("GameObject/Gold Miner/Tutorial Spotlight", false, 21)]
    static void CreateSpotlight(MenuCommand command)
    {
        var root = new GameObject("TutorialSpotlight");
        TutorialSpotlight spotlight = root.AddComponent<TutorialSpotlight>();

        SpriteRenderer dark = Shape(root.transform, "Dark", "Square", Color.black, DarkOrder);
        dark.transform.localScale = new Vector3(60f, 40f, 1f);   // well past the arena on any screen
        dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

        var holeObject = new GameObject("Hole");
        holeObject.transform.SetParent(root.transform, false);
        SpriteMask hole = holeObject.AddComponent<SpriteMask>();
        hole.sprite = LoadSprite("Circle");

        spotlight.dark = dark;
        spotlight.hole = hole;

        // Covers the whole arena, so it goes at the origin whatever the Scene view is looking at
        Undo.RegisterCreatedObjectUndo(root, "Create Tutorial Spotlight");
        GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
        Selection.activeGameObject = root;
    }

    // --- building blocks ------------------------------------------------------------------------

    // A lit circle (a sprite mask the step's dark leaves out). TutorialFollow is there, off, so it
    // can be pointed at the player or an object with a tick
    static GameObject CreateHoleObject(Transform parent, Vector3 at)
    {
        var go = new GameObject("Hole");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.localScale = new Vector3(3.2f, 3.2f, 1f);   // the circle sprite is 1 unit across
        go.AddComponent<SpriteMask>().sprite = LoadSprite("Circle");
        go.AddComponent<TutorialFollow>().followPlayer = false;
        return go;
    }

    // An arrow pointing down at `at`, with a label to its right. Rotate the Arrow child to aim it
    static GameObject CreateCalloutObject(Transform parent, Vector3 at, string text)
    {
        var go = new GameObject("Callout");
        go.transform.SetParent(parent, false);
        go.transform.position = at;

        BuildArrow(go.transform, false);

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshPro));
        label.transform.SetParent(go.transform, false);
        var rect = (RectTransform)label.transform;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(4f, 1f);
        rect.localPosition = new Vector3(0.4f, 0.9f, 0f);
        TextMeshPro tmp = label.GetComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = ArrowOrder;
        return go;
    }

    // Pointing down: the head's tip is at the arrow's origin, the shaft above it
    static GameObject BuildArrow(Transform parent, bool withSpotlight)
    {
        var root = new GameObject(parent == null ? "TutorialArrow" : "Arrow");
        if (parent != null) root.transform.SetParent(parent, false);
        TutorialArrow arrow = root.AddComponent<TutorialArrow>();
        arrow.withSpotlight = withSpotlight;   // in a step, the step fades it

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        arrow.visual = visual;

        SpriteRenderer head = Shape(visual, "Head", "Triangle", ArrowColor, ArrowOrder);
        head.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        head.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        head.transform.localScale = new Vector3(0.6f, 0.5f, 1f);
        SpriteRenderer shaft = Shape(visual, "Shaft", "Square", ArrowColor, ArrowOrder);
        shaft.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        shaft.transform.localScale = new Vector3(0.22f, 0.75f, 1f);
        return root;
    }

    static SpriteRenderer Shape(Transform parent, string name, string sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = LoadSprite(sprite);
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }

    // The shape sprites in Assets/Sprites (Triangle, Square, Circle)
    static Sprite LoadSprite(string name)
    {
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/" + name + ".png").OfType<Sprite>().FirstOrDefault();
        if (sprite == null) Debug.LogWarning("TutorialPropsMenu: no sprite in Assets/Sprites/" + name + ".png");
        return sprite;
    }

    // --- where things go ------------------------------------------------------------------------

    // The open scene's "Tutorial" object, made if missing
    static Transform TutorialRoot()
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Tutorial") return root.transform;
        var go = new GameObject("Tutorial");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial");
        return go.transform;
    }

    // The step the right-clicked / selected object is in
    static TutorialStep SelectedStep(MenuCommand command)
    {
        GameObject picked = command.context as GameObject ?? Selection.activeGameObject;
        TutorialStep step = picked != null ? picked.GetComponentInParent<TutorialStep>(true) : null;
        if (step == null)
            EditorUtility.DisplayDialog("Pick a step first", "Select a Tutorial Step (or something inside one) in the Hierarchy, then try again.", "OK");
        return step;
    }

    static Vector3 PlayerStart()
    {
        LevelLayout layout = Object.FindFirstObjectByType<LevelLayout>();
        return layout != null && layout.playerStart != null ? layout.playerStart.position : Vector3.zero;
    }

    static Vector3 ViewCentre()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? new Vector3(view.pivot.x, view.pivot.y, 0f) : Vector3.zero;
    }

    // Under the right-clicked object if any, else in the middle of the Scene view
    static void Place(GameObject go, MenuCommand command, string undoName)
    {
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null) go.transform.position = ViewCentre();
        Undo.RegisterCreatedObjectUndo(go, undoName);
        Selection.activeGameObject = go;
    }
}
