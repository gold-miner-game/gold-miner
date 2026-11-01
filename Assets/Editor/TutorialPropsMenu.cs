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
//   Tutorial Dodge     a step that freezes the game the first time a hook takes aim (hook_aim)
//   Tutorial Squeeze   the dodge tutorial's end: a story after 4 dodges, then the presses close in
//   Tutorial Rescue    the squeeze's way out: a friendly story on the last Life, then the Exit opens
//   Tutorial Skills    the block tutorial: J.K. Rolling's story at the start, then Block and Attack steps
//   Tutorial Store Intro  the store's first visit: J.K. Rolling's story on screen, lighting the wallet, goods, Leave
//   Tutorial Pointer   a UI object selected: a step lighting it with an arrow, from a story line #show_<name>
//   Tutorial Life Lost a step for the first Life lost: the player and the HUD's Life panel lit
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
        GameObject callout = CreateCalloutObject(go.transform, at + new Vector3(0f, 1.2f, 0f), first ? "You" : "Text");
        callout.GetComponent<TutorialFollow>().followPlayer = first;

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
        Selection.activeGameObject = BuildUICallout(step, "Text").gameObject;
    }

    // An arrow pointing down at the callout's origin, with a label, on the step's screen canvas
    static RectTransform BuildUICallout(TutorialStep step, string labelText)
    {
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
        text.text = labelText;
        text.fontSize = 40f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        return rect;
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
        TutorialDialogue dialogue = CreateStoryStep("Story", "round_won");
        dialogue.lines = new[]
        {
            new TutorialDialogue.Line { speaker = "Evil Meowmeow", text = "So... you ate all of MY gold." },
            new TutorialDialogue.Line { speaker = "You", text = "It was just lying there!" },
            new TutorialDialogue.Line { speaker = "Evil Meowmeow", text = "Then let's see how you do against my miners. Mwahaha!" },
        };
        Selection.activeGameObject = dialogue.gameObject;
    }

    // The end of the dodge tutorial: after the 4th dodged hook the EvilMeowmeow says
    // Assets/Story/squeezeYou.txt, then the Squeeze starts: two presses close in from the sides,
    // new hook holes open between the level's hooks, the hooks speed up. Surviving it wins
    [MenuItem("Tools/Gold Miner/Tutorial Squeeze", false, 107)]
    [MenuItem("GameObject/Gold Miner/Tutorial Squeeze", false, 17)]
    static void CreateSqueeze(MenuCommand command)
    {
        TutorialDialogue dialogue = CreateStoryStep("SqueezeStory", SurvivalHookAI.DodgedEvent);
        TutorialStep step = dialogue.GetComponent<TutorialStep>();
        step.startEventCount = 4;
        dialogue.scriptFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/squeezeYou.txt");
        dialogue.hideResult = false;
        dialogue.leaveWhenDone = false;   // the level goes on: the squeeze comes next

        var go = new GameObject("Squeeze");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Squeeze");
        SqueezePhase squeeze = go.AddComponent<SqueezePhase>();
        squeeze.startEvent = step.name + ".end";

        // Huge triangles on the arena's sides, base on the edge and the point inwards
        const float halfWidth = 8.89f, height = 10.5f, depth = 3.5f;
        squeeze.leftPress = CreatePress(go.transform, "Left Press", -halfWidth + depth * 0.5f, -90f, height, depth);
        squeeze.rightPress = CreatePress(go.transform, "Right Press", halfWidth - depth * 0.5f, 90f, height, depth);

        // New holes between the outermost two of the level's hooks: the middle first, then the quarters
        var spots = new GameObject("Hook Spots").transform;
        spots.SetParent(go.transform, false);
        squeeze.hookSpots = spots;
        LevelLayout layout = Object.FindFirstObjectByType<LevelLayout>();
        var hooks = layout != null && layout.hooks != null
            ? layout.hooks.Cast<Transform>().Select(h => h.position).OrderBy(p => p.x).ToList()
            : new System.Collections.Generic.List<Vector3>();
        Vector3 a = hooks.Count > 0 ? hooks[0] : new Vector3(-3f, 3.6f, 0f);
        Vector3 b = hooks.Count > 1 ? hooks[hooks.Count - 1] : new Vector3(3f, 3.6f, 0f);
        foreach (float f in new[] { 0.5f, 0.25f, 0.75f })
        {
            var spot = new GameObject("Spot " + (spots.childCount + 1)).transform;
            spot.SetParent(spots, false);
            spot.position = Vector3.Lerp(a, b, f);
        }

        Selection.activeGameObject = go;
    }

    // The squeeze's way out: once the player is down to their last Life, a friendly Meowmeow says
    // Assets/Story/niceMeowmeow.txt; then the left press slides away and the Exit appears where it
    // was. Reaching it wins the level. Needs the Squeeze (Tutorial Squeeze) in the scene
    [MenuItem("Tools/Gold Miner/Tutorial Rescue", false, 108)]
    [MenuItem("GameObject/Gold Miner/Tutorial Rescue", false, 18)]
    static void CreateRescue(MenuCommand command)
    {
        SqueezePhase squeeze = Object.FindFirstObjectByType<SqueezePhase>();
        if (squeeze == null)
        {
            EditorUtility.DisplayDialog("No Squeeze yet", "Add the squeeze first (Tools > Gold Miner > Tutorial Squeeze), then try again.", "OK");
            return;
        }

        TutorialDialogue dialogue = CreateStoryStep("NiceStory", SqueezePhase.RescueEvent);
        dialogue.scriptFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/niceMeowmeow.txt");
        dialogue.hideResult = false;
        dialogue.leaveWhenDone = false;   // the level goes on: the exit opens next
        TutorialDialogue.Character friend = dialogue.characters[0];
        friend.name = "Nice Meowmeow";
        friend.prefab = FindPrefab("GreedyMeowmeow");
        friend.side = TutorialDialogue.Side.Friend;
        friend.spawnAt.position = new Vector3(-3.5f, 1.5f, 0f);
        friend.spawnAt.name = "FriendSpot";

        Undo.RecordObject(squeeze, "Create Tutorial Rescue");
        squeeze.exitEvent = dialogue.name + ".end";
        squeeze.exitPrefab = FindPrefab("Exit");
        if (squeeze.exitSpot == null)
        {
            var spot = new GameObject("Exit Spot").transform;
            Undo.RegisterCreatedObjectUndo(spot.gameObject, "Create Tutorial Rescue");
            spot.SetParent(squeeze.transform, false);
            spot.position = new Vector3(-8f, 0f, 0f);   // the left edge, where the left press was
            squeeze.exitSpot = spot;
        }

        Selection.activeGameObject = dialogue.gameObject;
    }

    // The block tutorial (level_01b_block): J.K. Rolling (the Cat) tells Assets/Story/ProfessorCat.txt
    // when the level starts; then the first time a hook takes aim the game freezes on the player
    // with a SPACE callout (block it); when the destroy stage starts it freezes on the miner to hit
    [MenuItem("Tools/Gold Miner/Tutorial Skills", false, 109)]
    [MenuItem("GameObject/Gold Miner/Tutorial Skills", false, 19)]
    static void CreateSkills(MenuCommand command)
    {
        Vector3 player = PlayerStart();

        TutorialDialogue dialogue = CreateStoryStep("ProfessorStory", "");
        TutorialStep story = dialogue.GetComponent<TutorialStep>();
        story.startOn = TutorialStep.StartTrigger.LevelStart;
        dialogue.scriptFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/ProfessorCat.txt");
        dialogue.hideResult = false;
        dialogue.leaveWhenDone = false;   // the lesson comes next
        TutorialDialogue.Character professor = dialogue.characters[0];
        professor.name = "J.K. Rolling";
        professor.prefab = FindPrefab("Cat");
        professor.side = TutorialDialogue.Side.Friend;
        professor.spawnAt.name = "ProfessorSpot";
        professor.spawnAt.position = player + new Vector3(2.5f, 0.5f, 0f);

        // Block: the first red line freezes on the player and the miner's hook
        TutorialStep block = CreateLessonStep("Block", SurvivalHookAI.AimEvent, "SPACE");
        LevelLayout layout = Object.FindFirstObjectByType<LevelLayout>();
        if (layout != null && layout.hooks != null)
            foreach (Transform h in layout.hooks)
                CreateHoleObject(block.transform, h.position).name = "Hole " + h.name;

        // Attack: when the destroy stage starts, the miner to walk up to and hit (SPACE next to it)
        TutorialStep attack = CreateLessonStep("Attack", "stage_destroy", null);
        attack.seconds = 3f;
        if (layout != null && layout.hooks != null)
            foreach (Transform h in layout.hooks)
            {
                Vector3 body = h.position + new Vector3(0f, 0.8f, 0f);   // the miner sits above its hook origin
                CreateHoleObject(attack.transform, body).name = "Hole " + h.name;
                GameObject callout = CreateCalloutObject(attack.transform, body + new Vector3(-0.8f, 0f, 0f), "SPACE");
                callout.transform.Find("Arrow").localRotation = Quaternion.Euler(0f, 0f, 90f);   // points right, at the miner
            }

        Selection.activeGameObject = story.gameObject;
    }

    // The store's introduction (open the Store scene first, with its UI built: Tools > Gold Miner >
    // Build Store UI). The first time the store opens, J.K. Rolling and the pig stand on the
    // dialogue box and talk through Assets/Story/StoreIntro.txt; lines ending in #show_wallet,
    // #show_shelf and #show_leave light that part of the store until the next one
    [MenuItem("Tools/Gold Miner/Tutorial Store Intro", false, 111)]
    [MenuItem("GameObject/Gold Miner/Tutorial Store Intro", false, 21)]
    static void CreateStoreIntro(MenuCommand command)
    {
        TutorialDialogue dialogue = CreateStoryStep("StoreIntro", "");
        TutorialStep story = dialogue.GetComponent<TutorialStep>();
        story.startOn = TutorialStep.StartTrigger.LevelStart;
        story.onlyFirstTime = true;
        story.freezeGame = false;   // nothing to freeze, and the store keeps working
        story.darkness = 0.7f;
        dialogue.scriptFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/StoreIntro.txt");
        dialogue.onScreen = true;
        dialogue.besideBox = true;
        dialogue.hideResult = false;
        dialogue.leaveWhenDone = false;
        TutorialDialogue.Character professor = dialogue.characters[0];
        professor.name = "J.K. Rolling";
        professor.prefab = FindPrefab("Cat");
        professor.side = TutorialDialogue.Side.Friend;
        dialogue.characters[1].prefab = FindPrefab("MrHyde");   // On Screen: the player needs a picture too
        // In the arena only: no arena here
        Object.DestroyImmediate(professor.spawnAt.gameObject);
        Object.DestroyImmediate(dialogue.speakerLight.gameObject);
        dialogue.speakerLight = null;

        CreateSpotlightStep("ShowWallet", "show_wallet", "show_shelf", "Wallet");
        CreateSpotlightStep("ShowShelf", "show_shelf", "show_leave", "Counter");
        CreateSpotlightStep("ShowLeave", "show_leave", "show_modes", "Leave");

        Selection.activeGameObject = story.gameObject;
    }

    // Lights one UI object from one event to another, once
    static void CreateSpotlightStep(string name, string startEvent, string endEvent, string target)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Store Intro");
        go.transform.SetParent(TutorialRoot(), false);
        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = startEvent;
        step.endOn = TutorialStep.EndTrigger.Event;
        step.endEvent = endEvent;
        step.freezeGame = false;
        step.darkness = 0.7f;
        step.onlyFirstTime = true;

        var hole = new GameObject("UIHole " + target);
        hole.transform.SetParent(go.transform, false);
        hole.AddComponent<TutorialUIHole>().target = target;
        GameObject found = GameObject.Find(target);
        if (found != null && found.transform is RectTransform rect) AimUICallout(BuildUICallout(step, ""), rect);
    }

    // Select a UI object (the store's Wallet, Counter, a HUD panel, ...) and run this: a step that
    // lights it and points an arrow at it, from a story line ending in #show_<name> (e.g.
    // #show_wallet) until the next line (story_line). No freeze; change the events in the Inspector
    [MenuItem("Tools/Gold Miner/Tutorial Pointer", false, 112)]
    [MenuItem("GameObject/Gold Miner/Tutorial Pointer", false, 22)]
    static void CreatePointer(MenuCommand command)
    {
        GameObject picked = command.context as GameObject ?? Selection.activeGameObject;
        var target = picked != null ? picked.transform as RectTransform : null;
        if (target == null || target.GetComponentInParent<Canvas>() == null)
        {
            EditorUtility.DisplayDialog("Pick a UI object",
                "Select the UI object to point at (e.g. Wallet under StoreCanvas) in the Hierarchy, then try again.", "OK");
            return;
        }

        var go = new GameObject("Point " + target.name);
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Pointer");
        go.transform.SetParent(TutorialRoot(), false);
        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = "show_" + target.name.ToLowerInvariant().Replace(' ', '_');
        step.endOn = TutorialStep.EndTrigger.Event;
        step.endEvent = TutorialDialogue.LineEvent;
        step.freezeGame = false;
        step.darkness = 0.6f;
        step.maxTimes = 1;

        var hole = new GameObject("UIHole " + target.name);
        hole.transform.SetParent(go.transform, false);
        hole.AddComponent<TutorialUIHole>().target = target.name;
        AimUICallout(BuildUICallout(step, ""), target);

        Selection.activeGameObject = go;
    }

    // Puts a UI callout's arrow tip just above `target` (below it, pointing up, near the top of the
    // screen). Both are screen canvases, so they meet in screen pixels; the callout's canvas is laid
    // out at 1920x1080, fitted to the screen (Expand)
    static void AimUICallout(RectTransform callout, RectTransform target)
    {
        Canvas root = target.GetComponentInParent<Canvas>().rootCanvas;
        if (root.renderMode != RenderMode.ScreenSpaceOverlay) return;   // left in the middle: drag it
        Vector2 screen = root.pixelRect.size;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);   // an overlay canvas's world space is screen pixels
        float midX = (corners[0].x + corners[2].x) * 0.5f;
        bool nearTop = corners[2].y > screen.y * 0.7f;
        Vector2 tip = new Vector2(midX, nearTop ? corners[0].y - 12f : corners[2].y + 12f);
        float scale = Mathf.Min(screen.x / 1920f, screen.y / 1080f);
        callout.anchoredPosition = (tip - screen * 0.5f) / Mathf.Max(0.01f, scale);
        callout.gameObject.AddComponent<TutorialUIPoint>();   // stays by the step's UI Hole target at Play
        if (!nearTop) return;
        callout.Find("Arrow").localRotation = Quaternion.Euler(0f, 0f, 180f);   // pointing up
        ((RectTransform)callout.Find("Label")).anchoredPosition = new Vector2(40f, -90f);
    }

    // A frozen moment that lights the player, with a callout above their head
    static TutorialStep CreateLessonStep(string name, string startEvent, string label)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Skills");
        go.transform.SetParent(TutorialRoot(), false);
        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = startEvent;
        step.endOn = TutorialStep.EndTrigger.Seconds;
        step.seconds = 2.5f;
        step.darkness = 0.55f;   // the red line still shows across the dark
        step.maxTimes = 1;

        Vector3 player = PlayerStart();
        CreateHoleObject(go.transform, player).GetComponent<TutorialFollow>().followPlayer = true;
        if (!string.IsNullOrEmpty(label))
        {
            GameObject callout = CreateCalloutObject(go.transform, player + new Vector3(0f, 1.2f, 0f), label);
            callout.GetComponent<TutorialFollow>().followPlayer = true;
        }
        return step;
    }

    static Transform CreatePress(Transform parent, string name, float x, float angle, float length, float depth)
    {
        SpriteRenderer sr = Shape(parent, name, "Triangle", new Color(0.45f, 0.18f, 0.22f), 20);   // above the arena, under the tutorial dark
        sr.transform.position = new Vector3(x, 0f, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        sr.transform.localScale = new Vector3(length, depth, 1f);
        return sr.transform;
    }

    // A step with a TutorialDialogue that starts on `startEvent`: the EvilMeowmeow appears and
    // talks with the player. Change the trigger, characters and lines in the Inspector
    static TutorialDialogue CreateStoryStep(string name, string startEvent)
    {
        Transform tutorial = TutorialRoot();
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Story");
        go.transform.SetParent(tutorial, false);

        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = startEvent;
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
                                             side = TutorialDialogue.Side.Enemy },
            new TutorialDialogue.Character { name = "You", isPlayer = true },
        };
        return dialogue;
    }

    // A step for the dodge tutorial: the first time a hook takes aim (the red dotted line appears)
    // the game freezes with the player, the line and the hook holes lit, then lets go so the player
    // can step out of the line before it fires. Other hook events: hook_fire, hook_dodged, player_hit
    [MenuItem("Tools/Gold Miner/Tutorial Dodge", false, 106)]
    [MenuItem("GameObject/Gold Miner/Tutorial Dodge", false, 16)]
    static void CreateDodge(MenuCommand command)
    {
        Transform tutorial = TutorialRoot();
        var go = new GameObject("Dodge");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Dodge");
        go.transform.SetParent(tutorial, false);

        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = SurvivalHookAI.AimEvent;
        step.endOn = TutorialStep.EndTrigger.Seconds;
        step.seconds = 2.5f;
        step.darkness = 0.55f;   // light enough that the red line still shows across the dark
        step.maxTimes = 1;

        Vector3 player = PlayerStart();
        GameObject hole = CreateHoleObject(go.transform, player);
        hole.GetComponent<TutorialFollow>().followPlayer = true;
        LevelLayout layout = Object.FindFirstObjectByType<LevelLayout>();
        if (layout != null && layout.hooks != null)
            foreach (Transform h in layout.hooks)
                CreateHoleObject(go.transform, h.position).name = "Hole " + h.name;
        // Just above the player's head, and stays there as they move
        GameObject callout = CreateCalloutObject(go.transform, player + new Vector3(0f, 1.2f, 0f), "Dodge!");
        callout.GetComponent<TutorialFollow>().followPlayer = true;

        Selection.activeGameObject = go;
    }

    // A step for the first Life lost (while still alive): the game freezes with the player and the
    // HUD's Life panel lit, so the player sees the count go down
    [MenuItem("Tools/Gold Miner/Tutorial Life Lost", false, 110)]
    [MenuItem("GameObject/Gold Miner/Tutorial Life Lost", false, 20)]
    static void CreateLifeLost(MenuCommand command)
    {
        Transform tutorial = TutorialRoot();
        var go = new GameObject("LifeLost");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Life Lost");
        go.transform.SetParent(tutorial, false);

        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = "life_lost";
        step.endOn = TutorialStep.EndTrigger.Seconds;
        step.seconds = 2.5f;
        step.maxTimes = 1;

        GameObject hole = CreateHoleObject(go.transform, PlayerStart());
        hole.GetComponent<TutorialFollow>().followPlayer = true;
        var uiHole = new GameObject("UIHole Life");
        uiHole.transform.SetParent(go.transform, false);
        uiHole.AddComponent<TutorialUIHole>().target = "Life";   // the HUD's Life panel

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

    // An arrow pointing down at `at`, with a label to its right. Rotate the Arrow child to aim it.
    // Like a Hole, it has TutorialFollow, off: tick Follow Player to place it relative to the player
    static GameObject CreateCalloutObject(Transform parent, Vector3 at, string text)
    {
        var go = new GameObject("Callout");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.AddComponent<TutorialFollow>().followPlayer = false;

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
