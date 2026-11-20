using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FrostEffect = FrostBody2D.FrostBody2D;

// Run in an isolated project: exercises real imported artwork, partial coverage and expiration.
public static class FrostFieldPlayTest
{
    const string Pending = "gm.tests.frostField";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static Sprite square;
    static FrostEffect template;
    static Type ConeType => typeof(FrostField2D).GetNestedType("Cone", BindingFlags.NonPublic);
    static Type SurfaceType => typeof(FrostField2D).GetNestedType("Surface", BindingFlags.NonPublic);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.Public | Hidden).Invoke(target, args);
    static object Cone(Vector2 origin, Vector2 direction, float length = 4f, float arc = 26.56505f, Transform caster = null)
        => Activator.CreateInstance(ConeType, new object[] { caster, origin, direction, length, arc });
    static IList Cones(params object[] cones)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ConeType));
        foreach (object cone in cones) list.Add(cone);
        return list;
    }
    static object Surface(Renderer source) => Activator.CreateInstance(SurfaceType, new object[] { source, template });
    static bool Tick(object surface, float time, params object[] cones) => (bool)Call(surface, "Tick", Cones(cones), time);
    static Color32[] Pixels(object surface) => ((Texture2D)Get(surface, "coverage")).GetPixels32();
    static int Touched(Color32[] pixels) { int count = 0; foreach (var pixel in pixels) if (pixel.r > 0) count++; return count; }
    static GameObject Make(string name) { var go = new GameObject(name); objects.Add(go); return go; }
    static SpriteRenderer Art(string name, Vector2 position, Vector2 size)
    {
        var go = Make(name); go.transform.position = position; go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sprite = go.AddComponent<SpriteRenderer>(); sprite.sprite = square; sprite.color = new Color(1f, .3f, .08f);
        return sprite;
    }
    public static void Run() { SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode(); }
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); EditorApplication.update -= Poll;
        int exit = 0;
        try { Execute(); }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
            if (square != null) UnityEngine.Object.DestroyImmediate(square);
            Time.timeScale = 1f;
        }
        EditorApplication.Exit(exit);
    }
    static void Execute()
    {
        Time.timeScale = 1f; GameManager.Instance = null;
        template = Resources.Load<FrostEffect>("Effects/FrostBody2D");
        Check(template != null && template.SharedFrostMaterial != null, "Imported frost is available in player resources");
        Check(!ShaderUtil.ShaderHasError(template.SharedFrostMaterial.shader), "Frost shader compiles");
        Texture2D white = Texture2D.whiteTexture;
        square = Sprite.Create(white, new Rect(0,0,white.width,white.height), Vector2.one * .5f, white.width);
        object lower = Cone(Vector2.zero, Vector2.right);
        var source = Art("Untyped future world object", new Vector2(2,2), new Vector2(4,4));
        object surface = Surface(source);
        Check(Tick(surface, 100f, lower), "Any sprite is eligible, even with its center outside the cone");
        var first = Pixels(surface);
        Check(Touched(first) > 0 && Touched(first) < first.Length / 2, "Only the exposed part receives frost");
        Vector4 rect = (Vector4)Get(surface, "coverageRect");
        int width = (int)Get(surface, "width"), height = (int)Get(surface, "height");
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if (first[y * width + x].r > 0)
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 local = new Vector3(Mathf.Lerp(rect.x, rect.z, (x + corner % 2f) / width),
                        Mathf.Lerp(rect.y, rect.w, (y + corner / 2) / (float)height));
                    Check((bool)Call(lower, "Contains", (Vector2)source.transform.TransformPoint(local)), "Frost never spills beyond the cone");
                }
        float lifetime = FrostField2D.FrostDuration;
        Check(Tick(surface, 100f + lifetime - .1f), "Frost remains through its configured lifetime");
        Check(!Tick(surface, 100f + lifetime + .01f), "Frost expires after its configured lifetime");
        Call(surface, "Dispose");

        surface = Surface(source); Tick(surface, 200f, lower); first = Pixels(surface);
        object upper = Cone(new Vector2(0,4), Vector2.right);
        Tick(surface, 201f, upper); var union = Pixels(surface);
        Check(Touched(union) > Touched(first), "Sweeping the breath adds patches instead of freezing the entire object");
        Tick(surface, 200f + lifetime + .1f); var remaining = Pixels(surface);
        int expired = 0, fresh = 0;
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i].r > 0 && remaining[i].r == 0) expired++;
            if (first[i].r == 0 && remaining[i].r > 0) fresh++;
        }
        Check(expired > 0 && fresh > 0, "Each patch expires independently from its last exposure");
        source.transform.position += Vector3.right * 10;
        Check(Tick(surface, 200f + lifetime + .2f) && Touched(Pixels(surface)) == Touched(remaining), "Partial frost follows moving objects");
        Call(surface, "Dispose"); source.gameObject.SetActive(false);

        foreach (string name in new[] { "Treasure", "Rock", "Future enemy" })
        {
            var art = Art(name, new Vector2(2,0), Vector2.one);
            var status = Surface(art);
            Check(Tick(status, 300f, lower), name + " gets the same frost treatment without a gameplay-type filter");
            Call(status, "Dispose"); art.gameObject.SetActive(false);
        }
        var casterArt = Art("Caster artwork", Vector2.right, Vector2.one);
        Check(!(bool)Call(Cone(Vector2.zero, Vector2.right, 4, 45, casterArt.transform), "MayTouch", casterArt), "The caster does not frost itself");
        casterArt.gameObject.SetActive(false);

        var hook = Make("Real hook crossing cone").AddComponent<Hook>();
        hook.BeginExtended(new Vector2(-2,1), Vector2.right, 8f, 8f, Color.yellow);
        Call(hook, "UpdateRope");
        LineRenderer rope = hook.transform.Find("Rope").GetComponent<LineRenderer>();
        surface = Surface(rope);
        Check(Tick(surface, 400f, lower), "Rope is exposed even though both endpoints are outside the cone");
        var ropePixels = Pixels(surface);
        Check(Touched(ropePixels) > 0 && Touched(ropePixels) < ropePixels.Length, "Only the middle rope section gets frost");
        Check(hook.IsBusy && hook.extendSpeed == 12f && hook.Carried == null, "Art leaves hook movement and gameplay state alone");
        foreach (Color shotColor in new[] { Color.yellow, Color.red })
        {
            hook.BeginExtended(new Vector2(-2,1), Vector2.right, 8f, 8f, shotColor);
            Call(hook,"UpdateRope");
            FrostField2D.Expose(null,Vector2.zero,Vector2.right,4f,26.56505f);
            var hookField=UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
            Call(hookField,"LateUpdate");
            Check(((IDictionary)Get(hookField,"surfaces")).Contains(rope), "The real field discovers hidden " + shotColor + " hook strokes");
            UnityEngine.Object.DestroyImmediate(hookField.gameObject);
        }
        Call(surface, "Dispose"); hook.gameObject.SetActive(false);

        var automatic = Art("Automatic new world target", Vector2.right, Vector2.one * .5f);
        var lifeUI = Make("Elsa health UI test").AddComponent<PlayerLifeBar>();
        Call(lifeUI,"Make"); Call(lifeUI,"Build",3);
        var lifeBar = (Transform)Get(lifeUI,"bar"); objects.Add(lifeBar.gameObject);
        lifeBar.position = new Vector3(2,0,0);
        var healthUI = Make("Enemy health UI test").AddComponent<HookHealth>();
        Call(healthUI,"MakeBar");
        var healthBar = (Transform)Get(healthUI,"bar"); objects.Add(healthBar.gameObject);
        healthBar.position = new Vector3(2,0,0);
        FrostField2D.Expose(null, Vector2.zero, Vector2.right, 4f, 26.56505f);
        var field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        Check(field != null, "Casting creates the generic frost field");
        Call(field,"LateUpdate");
        Check(((IDictionary)Get(field,"surfaces")).Contains(automatic), "Any world object is discovered automatically without a type marker");
        foreach (var uiRoot in new[] { lifeBar, healthBar })
        foreach (SpriteRenderer ui in uiRoot.GetComponentsInChildren<SpriteRenderer>())
            Check(!((IDictionary)Get(field,"surfaces")).Contains(ui) && !FrostField2D.IsFrozen(ui),
                "sprite-based health UI never receives frost");
        automatic.gameObject.AddComponent<FrostIgnore2D>(); Call(field,"LateUpdate");
        Check(!((IDictionary)Get(field,"surfaces")).Contains(automatic), "newly excluded UI immediately loses existing frost");
        UnityEngine.Object.DestroyImmediate(field.gameObject); automatic.gameObject.SetActive(false);
        lifeUI.gameObject.SetActive(false); healthUI.gameObject.SetActive(false);

        var miner = Make("Whole miner frost test").AddComponent<MinerAI>(); miner.autoShoot = false;
        var minerHead = Art("Touched miner head", new Vector2(2,0), Vector2.one * .3f);
        var minerBody = Art("Miner body outside breath", new Vector2(2,3), Vector2.one * .5f);
        var minerArm = Art("Miner arm outside breath", new Vector2(6,0), Vector2.one * .3f);
        foreach (var part in new[] { minerHead, minerBody, minerArm }) part.transform.SetParent(miner.transform, true);
        var minerUI = Art("Miner UI stays clear", new Vector2(2,3), Vector2.one * .2f);
        minerUI.transform.SetParent(miner.transform, true); minerUI.gameObject.AddComponent<FrostIgnore2D>();
        var carriedHook = Make("Miner separate hook").AddComponent<Hook>(); carriedHook.transform.SetParent(miner.transform, true);
        var hookArt = Art("Separate hook artwork", new Vector2(6,3), Vector2.one * .2f);
        hookArt.transform.SetParent(carriedHook.transform, true);
        var neighbor = Make("Untouched neighboring miner").AddComponent<MinerAI>(); neighbor.autoShoot = false;
        var neighborBody = Art("Untouched neighboring body", new Vector2(2,6), Vector2.one * .3f);
        neighborBody.transform.SetParent(neighbor.transform, true);
        FrostField2D.Expose(null,Vector2.zero,Vector2.right,4f,26.56505f);
        field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>(); Call(field,"LateUpdate");
        var minerSurfaces = (IDictionary)Get(field,"surfaces");
        float minerExpiry = (float)Get(minerSurfaces[minerHead],"activeUntil");
        foreach (var part in new[] { minerHead, minerBody, minerArm })
        {
            Check(FrostField2D.IsFrozen(part), "one touched body part frosts the whole miner");
            var fullPixels = Pixels(minerSurfaces[part]);
            Check(Touched(fullPixels) == fullPixels.Length, "the entire miner part receives frost, including outside the cone");
            Check((float)Get(minerSurfaces[part],"activeUntil") == minerExpiry, "whole-miner frost shares the original contact expiry");
        }
        Check(!FrostField2D.IsFrozen(minerUI) && !FrostField2D.IsFrozen(hookArt), "whole-body frost excludes UI and separate hook artwork");
        Check(!FrostField2D.IsFrozen(neighborBody), "frost does not spread to other miners");
        foreach (var part in new[] { minerHead, minerBody, minerArm })
            Check(!Tick(minerSurfaces[part],minerExpiry + .01f), "whole-miner frost expires without refreshing itself");
        UnityEngine.Object.DestroyImmediate(field.gameObject);
        miner.gameObject.SetActive(false); neighbor.gameObject.SetActive(false);

        var elsaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/Penguin.prefab");
        var elsaObject = UnityEngine.Object.Instantiate(elsaPrefab); objects.Add(elsaObject);
        var elsa = elsaObject.GetComponent<PenguinController>();
        elsa.SkillSlot.Equip(Resources.Load<ColdBreathSkillDefinition>("Skills/ColdBreath"));
        var skill = (ElsaColdBreathSkill)elsa.SkillSlot.ActiveSkill;
        Check(Mathf.Abs(skill.breathLength - 4.572f) < .001f, "Elsa's default breath length is halved");
        Check(skill.TryActivate(), "Elsa activates her real independent cold-breath skill");
        Check(skill.mouthPose.TryGetMouthFrame(out Vector3 mouth, out Vector2 forward), "Actual Elsa mouth supplies the frost cone");
        var liveTarget = Art("Real skill frost target", (Vector2)mouth + forward * 2f, Vector2.one * .25f);
        Check(skill.BreathEffect.ContactLength == 0f, "the empty first animation frame has no frost reach");
        skill.UpdateSkillPose(); field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        Check(field == null, "casting does not instantly expose the whole cone");
        typeof(ElsaColdBreathSkill).GetField("castStarted", Hidden).SetValue(skill, Time.time - skill.skillDuration * .1f);
        skill.UpdateSkillPose(); field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        Call(field,"LateUpdate");
        Check(!((IDictionary)Get(field,"surfaces")).Contains(liveTarget), "an item ahead of the early visible front stays unfrosted");
        float earlyReach = skill.BreathEffect.ContactLength;
        typeof(ElsaColdBreathSkill).GetField("castStarted", Hidden).SetValue(skill, Time.time - skill.skillDuration * .55f);
        skill.UpdateSkillPose(); field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        Call(field,"LateUpdate");
        Check(skill.BreathEffect.ContactLength > earlyReach, "contact range follows the displayed animation frame");
        Check(((IDictionary)Get(field,"surfaces")).Contains(liveTarget), "The actual skill applies visual frost through its live cone");
        foreach (var renderer in elsaObject.GetComponentsInChildren<SpriteRenderer>())
            Check(!((IDictionary)Get(field,"surfaces")).Contains(renderer), "Elsa's own artwork stays unfrosted");
        UnityEngine.Object.DestroyImmediate(field.gameObject);
        foreach (float mapScale in new[] { .35f, 1.6f })
        {
            skill.Cancel(); elsaObject.transform.localScale = Vector3.one * mapScale;
            Check(skill.TryActivate(), "scaled Elsa starts a cast");
            typeof(ElsaColdBreathSkill).GetField("castStarted", Hidden).SetValue(skill, Time.time - skill.skillDuration * .55f);
            skill.UpdateSkillPose();
            Transform visual = skill.BreathEffect.transform;
            float reach = skill.BreathEffect.ContactLength;
            var mapInside = Art("Inside scaled map breath", visual.TransformPoint(new Vector3(0, reach * .6f)), Vector2.one * .08f * mapScale);
            var mapOutside = Art("Beyond scaled map breath", visual.TransformPoint(new Vector3(0, reach * 1.3f)), Vector2.one * .08f * mapScale);
            field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>(); Call(field,"LateUpdate");
            var patches = (IDictionary)Get(field,"surfaces");
            Check(patches.Contains(mapInside), "frost reaches artwork inside the scaled visible breath");
            Check(!patches.Contains(mapOutside), "frost cannot extend past the scaled visible breath");
            UnityEngine.Object.DestroyImmediate(field.gameObject);
            mapInside.gameObject.SetActive(false); mapOutside.gameObject.SetActive(false);
        }
        elsaObject.SetActive(false); liveTarget.gameObject.SetActive(false);

        var rounded = Activator.CreateInstance(ConeType, new object[] { null, Vector2.zero, Vector2.right, 4f, 45f, true });
        var cornerArt = Art("Ahead of rounded breath front", new Vector2(3.4f,3.2f), Vector2.one * .1f);
        var cornerSurface = Surface(cornerArt);
        Check(!Tick(cornerSurface, 500f, rounded), "triangle corners ahead of the curved animation front stay unfrosted");
        Call(cornerSurface,"Dispose"); cornerArt.gameObject.SetActive(false);
        var inside = Art("Behind rounded breath front", new Vector2(2f,0), Vector2.one * .2f);
        var insideSurface = Surface(inside);
        Check(Tick(insideSurface, 500f, rounded), "rounded contact still frosts items behind the visible front");
        Call(insideSurface,"Dispose"); inside.gameObject.SetActive(false);

        foreach (Vector3 scale in new[] { new Vector3(.35f,.35f,1), new Vector3(-.6f,1.2f,1), new Vector3(1.8f,.5f,1) })
        {
            Matrix4x4 visual = Matrix4x4.TRS(new Vector3(5,-3,0), Quaternion.Euler(0,0,31), scale)
                * Matrix4x4.Rotate(Quaternion.Euler(0,0,23));
            var transformed = Activator.CreateInstance(ConeType, new object[] { null, visual, 4f, 45f, true });
            for (int x = -5; x <= 5; x++)
            for (int y = -1; y <= 9; y++)
            {
                Vector2 local = new Vector2(x * .51f + .017f, y * .51f + .033f);
                bool expected = local.y >= 0 && Mathf.Abs(local.x) <= local.y && local.sqrMagnitude <= 16f;
                Check((bool)Call(transformed,"Contains",(Vector2)visual.MultiplyPoint3x4(local)) == expected,
                    "contact uses the mesh transform under rotation, mirroring and uneven parent scale");
            }
            var art = Art("Transformed contact patch", visual.MultiplyPoint3x4(new Vector3(0,2,0)), Vector2.one * .04f);
            var transformedSurface = Surface(art);
            Check(Tick(transformedSurface,500f,transformed), "transformed coverage stamping agrees with point contact");
            Call(transformedSurface,"Dispose"); art.gameObject.SetActive(false);
        }

        RenderCheck(lower);
        Debug.Log("FROST_FIELD_PASS: " + checks + " checks: partial sprites and real hook rope, generic world art, per-patch lifetime, motion, shader and rendering.");
    }

    static void RenderCheck(object cone)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var camera = Make("Frost validation camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(2,2,-10); camera.orthographic = true; camera.orthographicSize = 2.5f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.07f,.09f);
        camera.enabled = false;
        var sprite = Art("Rendered partial frost", new Vector2(2,2), new Vector2(4,4));
        var line = Make("Rendered rope").AddComponent<LineRenderer>();
        line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        line.positionCount = 2; line.SetPositions(new[] { new Vector3(-2,1), new Vector3(6,1) });
        line.startWidth = line.endWidth = .2f; line.startColor = line.endColor = Color.yellow; line.sortingOrder = 5;
        var rt = new RenderTexture(512,512,24); camera.targetTexture = rt;
        Texture2D Capture()
        {
            camera.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(512,512,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,512,512),0,0); image.Apply(); RenderTexture.active = previous; return image;
        }
        var before = Capture();
        object spriteSurface = Surface(sprite), lineSurface = Surface(line);
        Tick(spriteSurface,500f,cone); Tick(lineSurface,500f,cone);
        Tick(spriteSurface,500.5f); Tick(lineSurface,500.5f);
        var effect = (FrostEffect)Get(spriteSurface,"spriteEffect");
        typeof(FrostEffect).GetField("started",Hidden).SetValue(effect,Time.timeAsDouble-1.5);
        effect.Refresh();
        var after = Capture();
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath,"../artifacts/FrostValidation")); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"before.png"),before.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(folder,"frost.png"),after.EncodeToPNG());
        Color32[] a = before.GetPixels32(), b = after.GetPixels32(); int changed = 0, outside = 0;
        for (int y=0;y<512;y++) for(int x=0;x<512;x++)
        {
            int index=y*512+x;
            if (Mathf.Abs(a[index].r-b[index].r)+Mathf.Abs(a[index].g-b[index].g)+Mathf.Abs(a[index].b-b[index].b)<12) continue;
            changed++;
            Vector2 point = camera.ViewportToWorldPoint(new Vector3((x+.5f)/512,(y+.5f)/512,10));
            if (!(bool)Call(cone,"Contains",point)) outside++;
        }
        Check(changed>200,"Imported frost artwork is visibly rendered on the exposed section");
        Debug.Log("FROST_RENDER: changed="+changed+", outside="+outside+", images="+folder);
        Check(outside==0,"Rendered frost changes no pixels outside the breath cone");
        Call(spriteSurface,"Dispose"); Call(lineSurface,"Dispose");
        camera.targetTexture=null; rt.Release();
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after);
    }
}
