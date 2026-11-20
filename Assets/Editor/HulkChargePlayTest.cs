using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Isolated Play-mode smoke test for real 2D casts, destruction, and artwork restoration.
public static class HulkChargePlayTest
{
    const string Pending = "gm.tests.hulkCharge";
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static void Set(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static GameObject Make(string name)
    {
        var go = new GameObject(name); objects.Add(go); return go;
    }
    static Rock Barrier(Vector2 position, bool wall)
    {
        var go = Make(wall ? "Wall rock" : "Battle rock"); go.transform.position = position;
        go.AddComponent<BoxCollider2D>();
        var rock = go.AddComponent<Rock>();
        rock.breakStageTime = 0f; // Instant-break fixture; production barriers may use timed stages.
        if (wall) rock.MakeWall(); return rock;
    }
    static void CheckRopeCut(PlayerCharacter player, string context)
    {
        var hook = Make("Shared rope cut " + context).AddComponent<Hook>();
        hook.BeginExtended(new Vector2(-2f, 0f), Vector2.right, 5f, 4f, Color.yellow);
        Physics2D.SyncTransforms();
        Check(player.TryCutRopes() && hook.Defended, context + " can cut with the shared J action");
    }

    static void CheckOverlapPixels(Sprite sprite)
    {
        Shader shader = Resources.Load<Shader>("Shaders/RhinoTintOverlap");
        Check(shader != null && !ShaderUtil.ShaderHasError(shader), "overlap shader compiles");
        var material = new Material(shader);
        var target = new RenderTexture(128, 128, 24);
        var pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            var surface = Make("Tint pixel test").AddComponent<SpriteRenderer>();
            surface.transform.position = new Vector3(100f, 100f, 0f);
            surface.sprite = sprite;
            surface.sharedMaterial = material;
            var camera = Make("Tint test camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(100f, 100f, -10f);
            camera.orthographic = true; camera.orthographicSize = .5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            material.SetColor("_ChargeColor", Color.red);
            material.SetColor("_DigestColor", Color.green);
            material.SetVector("_RedShape", new Vector4(100f, 100f, .25f, 100f));
            material.SetFloat("_GreenTop", 100.25f);
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            Color mixed = pixels.GetPixel(64, 32), green = pixels.GetPixel(64, 80);
            Check(mixed.r < .05f && mixed.g > .95f && mixed.b < .05f,
                "digestion green replaces red in the overlap without adding colors");
            Check(green.r < .05f && green.g > .95f, "green-only region stays green");
            material.SetFloat("_GreenTop", 99.75f);
            camera.Render();
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            Color red = pixels.GetPixel(64, 48);
            Check(red.r > .95f && red.g < .05f, "red-only region stays red");
            material.SetFloat("_Circular", 1f);
            material.SetFloat("_GreenTop", 100.5f);
            camera.Render();
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            mixed = pixels.GetPixel(64, 64); green = pixels.GetPixel(110, 110);
            Check(mixed.r < .05f && mixed.g > .95f && green.r < .05f && green.g > .95f,
                "digestion stays green across an overlapping circular charge");
            material.SetFloat("_RedEnabled", 0f);
            camera.Render();
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            green = pixels.GetPixel(64, 64);
            Check(green.r < .05f && green.g > .95f, "ears never acquire red from the overlap");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Resume()
    {
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(Pending, false))
            Execute();
    }
    static void Execute()
    {
        SessionState.SetBool(Pending, false);
        int exit = 0;
        Sprite artworkSprite = null;
        try
        {
            Time.timeScale = 1f;
            GameManager.Instance = null;
            var host = Make("Hulk charge under test");
            host.AddComponent<BoxCollider2D>();
            var artwork = new GameObject("Artwork").transform; artwork.SetParent(host.transform);
            Texture2D white = Texture2D.whiteTexture;
            artworkSprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), Vector2.one * .5f, white.width);
            var headObject = new GameObject("Head"); headObject.transform.SetParent(artwork);
            var head = headObject.AddComponent<SpriteRenderer>(); head.sprite = artworkSprite; head.color = Color.cyan;
            var hornObject = new GameObject("Large Horn"); hornObject.transform.SetParent(artwork);
            var horn = hornObject.AddComponent<SpriteRenderer>(); horn.color = Color.cyan; horn.sprite = artworkSprite;
            hornObject.transform.localPosition = new Vector3(.6f, .3f, 0f);
            hornObject.transform.localScale = new Vector3(.3f, .8f, 1f);
            foreach (string name in new[] { "Muzzle", "Small Horn" })
            {
                var surface = new GameObject(name); surface.transform.SetParent(artwork);
                surface.transform.localScale = new Vector3(.4f, .4f, 1f);
                var renderer = surface.AddComponent<SpriteRenderer>();
                renderer.sprite = artworkSprite; renderer.color = Color.cyan;
            }
            var eyeObject = new GameObject("Eye"); eyeObject.transform.SetParent(artwork);
            var eye = eyeObject.AddComponent<SpriteRenderer>(); eye.sprite = artworkSprite; eye.color = Color.black;
            var smile = new GameObject("Curved Smile").transform; smile.SetParent(artwork);
            var mouthObject = new GameObject("Middle"); mouthObject.transform.SetParent(smile);
            var mouth = mouthObject.AddComponent<SpriteRenderer>(); mouth.sprite = artworkSprite; mouth.color = Color.black;
            var earObject = new GameObject("Top Ear"); earObject.transform.SetParent(artwork);
            earObject.transform.localPosition = Vector3.up * 2f;
            var ear = earObject.AddComponent<SpriteRenderer>(); ear.sprite = artworkSprite; ear.color = Color.cyan;
            var hulk = host.AddComponent<RhinoController>();
            hulk.acceleration = 1000f; hulk.maximumChargeSpeed = 20f;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(hulk, Vector2.right);
            Call(hulk, "UseSkill");
            Check(hulk.IsCharging && horn.color == Color.red, "K charge turns the large horn red");
            var mask = (SpriteMask)typeof(RhinoController).GetField("chargeMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hulk);
            Check(head.color == Color.cyan && !mask.enabled, "only the horn turns red at activation");
            Check(Quaternion.Angle(artwork.localRotation, Quaternion.Euler(0, 0, -15f)) < .01f, "charge applies configured tilt");
            hulk.rotateAngle = 25f; Call(hulk, "UpdateChargeArtwork");
            Check(Quaternion.Angle(artwork.localRotation, Quaternion.Euler(0, 0, -25f)) < .01f, "tilt can be changed live");
            hulk.shakeFrequency = 5f; hulk.shakeHalfArc = 4f;
            Check(Mathf.Abs((float)Call(hulk, "ChargeShakeAngle", .05f) - 4f) < .001f,
                "shake reaches the positive half-arc");
            Check(Mathf.Abs((float)Call(hulk, "ChargeShakeAngle", .15f) + 4f) < .001f,
                "shake reaches the negative half-arc");
            hulk.shakeFrequency = 0f;
            Check((float)Call(hulk, "ChargeShakeAngle", .05f) == 0f, "zero frequency disables shaking");
            hulk.shakeFrequency = 10f; hulk.shakeHalfArc = 0f;
            Check((float)Call(hulk, "ChargeShakeAngle", .025f) == 0f, "zero half-arc disables shaking");

            Rock front = Barrier(new Vector2(1.05f, 0f), false);
            Rock wall = Barrier(new Vector2(1.35f, 0f), true);
            Rock behind = Barrier(new Vector2(-1.05f, 0f), false);
            Rock beside = Barrier(new Vector2(0f, 1.05f), false);
            wall.OnHookBlocked();
            Check(wall.GetComponent<Collider2D>().enabled, "wall rocks still resist hooks");
            Physics2D.SyncTransforms();
            Vector2 velocity = (Vector2)Call(hulk, "Velocity", Vector2.right);
            Check(velocity.x > 0f, "charge produces forward velocity");
            Check(!front.GetComponent<Collider2D>().enabled, "charge smashes the nearest ordinary rock");
            Physics2D.SyncTransforms(); Call(hulk, "Velocity", Vector2.right);
            Check(!wall.GetComponent<Collider2D>().enabled, "charge reaches the wall rock after the nearer barrier is gone");
            Check(behind.GetComponent<Collider2D>().enabled && beside.GetComponent<Collider2D>().enabled,
                "rocks behind and beside the charge stay intact");
            Check(!front.Smash(), "a smashed barrier cannot shatter twice");
            var edge = Make("Arena edge"); edge.transform.position = new Vector2(.85f, 0f);
            edge.AddComponent<BoxCollider2D>().size = new Vector2(.1f, 1f);
            Rock occluded = Barrier(new Vector2(1.35f, 0f), false);
            Physics2D.SyncTransforms(); Call(hulk, "Velocity", Vector2.right);
            Check(edge.GetComponent<Collider2D>().enabled && occluded.GetComponent<Collider2D>().enabled,
                "charge neither destroys arena edges nor reaches rocks through them");
            hulk.rotateAngle = 0f;
            Check((float)Call(hulk, "ChargeExpansion", 0f) == 0f
                && Mathf.Approximately((float)Call(hulk, "ChargeExpansion", hulk.skillDuration * .5f), .25f),
                "red expansion starts slowly and accelerates");
            Check((float)Call(hulk, "ChargeExpansion", hulk.skillDuration) == 1f
                && (float)Call(hulk, "ChargeExpansion", hulk.skillDuration * 2f) == 1f,
                "red expansion reaches and clamps to complete coverage at skill expiry");
            Set(hulk, "chargeStarted", Time.time - hulk.skillDuration * .5f);
            Call(hulk, "UpdateChargeArtwork");
            float top;
            Vector3 joint = horn.transform.TransformPoint(new Vector3(0f, -.5f, 0f));
            float halfRadius = mask.transform.lossyScale.x * .5f;
            Check(mask.enabled && Vector3.Distance(mask.transform.position, joint) < .001f
                && Mathf.Approximately(mask.transform.lossyScale.x, mask.transform.lossyScale.y),
                "red expansion is circular and centered where the horn joins the head");
            Set(hulk, "chargeStarted", Time.time - hulk.skillDuration * .25f); Call(hulk, "UpdateChargeArtwork");
            Check(Mathf.Abs(mask.transform.lossyScale.x * .5f - halfRadius * .25f) < .01f,
                "red expansion accelerates: quarter-time radius is one quarter of half-time radius");
            Set(hulk, "chargeStarted", Time.time - hulk.skillDuration * .5f); Call(hulk, "UpdateChargeArtwork");
            Check(head.color == Color.cyan, "charge wipe never overwrites original body colors");
            Check(eye.transform.Find("ChargeTint") == null && mouth.transform.Find("ChargeTint") == null
                && ear.transform.Find("ChargeTint") == null, "eyes, mouth and ears never receive red overlays");
            var eyeDetail = eye.transform.Find("ChargeFaceDetail").GetComponent<SpriteRenderer>();
            var mouthDetail = mouth.transform.Find("ChargeFaceDetail").GetComponent<SpriteRenderer>();
            Check(eyeDetail.enabled && mouthDetail.enabled && eyeDetail.color == Color.black && mouthDetail.color == Color.black
                && eyeDetail.sortingOrder > head.transform.Find("ChargeTint").GetComponent<SpriteRenderer>().sortingOrder,
                "black eyes and mouth stay visible above the red body fill");
            Check(ear.color == Color.cyan && ear.transform.Find("ChargeFaceDetail") == null,
                "ears keep their original colors and rear drawing order");
            Set(hulk, "chargeStarted", Time.time - 4f); Call(hulk, "Update");
            Check(!hulk.IsCharging && hulk.ChargeVelocity == Vector2.zero, "duration expiry clears the charge");
            Check(horn.color == Color.cyan && Quaternion.Angle(artwork.localRotation, Quaternion.identity) < .01f,
                "expiry restores the original horn color and body pose");
            top = mask.transform.TransformPoint(new Vector3(0f, 1f, 0f)).y;
            Check(mask.enabled && Mathf.Abs(top - .7f) < .01f && Mathf.Abs(mask.transform.position.y + .5f) < .01f,
                "the whole character is red when charge finishes");
            Set(hulk, "readyAt", Time.time + hulk.skillCooldown * .5f); Call(hulk, "UpdateChargeArtwork");
            top = mask.transform.TransformPoint(new Vector3(0f, 1f, 0f)).y;
            Check(Mathf.Abs(top - .1f) < .01f && Mathf.Abs(mask.transform.position.y + .5f) < .01f,
                "half cooldown clears red from the upper half, leaving the bottom red");
            Set(hulk, "readyAt", Time.time - 1f); Call(hulk, "UpdateChargeArtwork");
            Check(!mask.enabled && head.color == Color.cyan && horn.color == Color.cyan,
                "finished cooldown removes every charge tint");
            Check(!eyeDetail.enabled && !mouthDetail.enabled, "face detail copies switch off after cooldown");
            hulk.digestTime = 2f;
            hulk.Digest(50);
            Set(hulk, "readyAt", Time.time + hulk.skillCooldown * .5f);
            Call(hulk, "UpdateChargeArtwork");
            foreach (string name in new[] { "Head", "Large Horn", "Muzzle", "Small Horn", "Top Ear" })
            {
                Transform surface = artwork.Find(name);
                var tint = surface.Find(name == "Top Ear" ? "DigestTint" : "ChargeTint").GetComponent<SpriteRenderer>();
                Check(tint.enabled && tint.sharedMaterial.shader.name == "GoldMiner/RhinoTintOverlap",
                    "simultaneous digestion and cooldown use the combined tint on " + name);
                Check(tint.sharedMaterial.GetColor("_DigestColor") == hulk.digestTint
                    && tint.sharedMaterial.GetColor("_ChargeColor") == hulk.chargedHornColor,
                    "both colors remain present on " + name);
                Check(tint.sortingOrder > mask.backSortingOrder && tint.sortingOrder <= mask.frontSortingOrder,
                    name + " shares the same digest decay mask");
            }
            Check(eyeDetail.enabled && mouthDetail.enabled && eyeDetail.color == Color.black && mouthDetail.color == Color.black,
                "digestion preserves eyes and mouth details");
            float digestBottom = mask.transform.position.y;
            float fullDigestHeight = mask.transform.lossyScale.y;
            Check(Mathf.Abs(fullDigestHeight - 3f) < .01f, "digest mask includes the ears beyond the head bounds");
            typeof(PlayerCharacter).GetField("digestUntil", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(hulk, Time.time + hulk.DigestLength * .5f);
            Call(hulk, "UpdateChargeArtwork");
            Check(Mathf.Abs(mask.transform.lossyScale.y - fullDigestHeight * .5f) < .01f
                && Mathf.Abs(mask.transform.position.y - digestBottom) < .01f,
                "digestion clears every body part together from the top down");
            hulk.CancelDigest(); Call(hulk, "UpdateChargeArtwork");
            Check(head.transform.Find("ChargeTint").GetComponent<SpriteRenderer>().color == hulk.chargedHornColor
                && !ear.transform.Find("DigestTint").GetComponent<SpriteRenderer>().enabled,
                "finishing digestion restores the charge cooldown without coloring ears red");
            Set(hulk, "readyAt", Time.time - 1f); Call(hulk, "UpdateChargeArtwork");
            Check(!mask.enabled, "finishing both timers restores normal artwork");
            Check(Mathf.Approximately(((Vector2)Call(hulk, "Velocity", Vector2.right)).magnitude, hulk.CurrentSpeed),
                "normal fixed-speed movement returns after charge");
            Set(hulk, "readyAt", 0f); Call(hulk, "UseSkill");
            hulk.enabled = false;
            Check(!hulk.IsCharging && horn.color == Color.cyan, "disabling Hulk restores charge artwork");
            Hook.RopeCuts = true;
            foreach (Type type in new[] { typeof(MrHydeController), typeof(CatController), typeof(SquirrelController),
                typeof(FrogController), typeof(PenguinController), typeof(RhinoController) })
            {
                var characterHost = Make("Rope cut " + type.Name);
                characterHost.AddComponent<BoxCollider2D>();
                // The squirrel collector is deliberately absent; J must not depend on its K component.
                var character = (PlayerCharacter)characterHost.AddComponent(type);
                CheckRopeCut(character, type.Name);
            }
            hulk.enabled = true;
            Set(hulk, "readyAt", 0f); Call(hulk, "UseSkill");
            CheckRopeCut(hulk, "Hulk during charge");
            Call(hulk, "EndCharge", true);
            Check(Mathf.Abs(hulk.SkillReadyIn - 5f) < .01f, "Hulk K cooldown is five seconds");
            CheckRopeCut(hulk, "Hulk during K cooldown");
            Time.timeScale = 0f;
            Check(!hulk.TryCutRopes(), "shared rope cutting stays paused while gameplay is paused");
            Time.timeScale = 1f;
            CheckOverlapPixels(artworkSprite);
            Debug.Log("HULK_CHARGE_PLAY_PASS: " + checks + " physics, barrier, artwork, duration, and restoration checks.");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
            if (artworkSprite != null) UnityEngine.Object.DestroyImmediate(artworkSprite);
            Time.timeScale = 1f;
        }
        EditorApplication.Exit(exit);
    }
}

