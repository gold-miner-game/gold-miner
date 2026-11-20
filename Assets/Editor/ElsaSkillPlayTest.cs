using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ElsaSkillPlayTest
{
    const string Pending = "gm.tests.elsaSkill";
    static int checks;
    static readonly List<GameObject> objects = new List<GameObject>();
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static ElsaColdBreathSkill Skill(PenguinController elsa) => (ElsaColdBreathSkill)elsa.SkillSlot.ActiveSkill;
    static void Call(PenguinController elsa, string method)
    {
        if (method == "Update") typeof(ElsaColdBreathSkill).GetMethod("Update", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Skill(elsa),null);
        var characterMethod = typeof(PenguinController).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic);
        if (characterMethod != null) characterMethod.Invoke(elsa,null);
        else typeof(ElsaColdBreathSkill).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(Skill(elsa),null);
        if (method == "LateUpdate") Skill(elsa).UpdateSkillPose();
    }
    static void Set(PenguinController elsa,string field,float value)
        => typeof(ElsaColdBreathSkill).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(Skill(elsa),value);
    static float Get(PenguinController elsa,string field)
        => (float)typeof(ElsaColdBreathSkill).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Skill(elsa));
    static void K(PenguinController elsa)
        => typeof(PlayerCharacter).GetMethod("HandleSkillInput", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(elsa, new object[] { true, true });
    static void Cut(PenguinController elsa)
    {
        var go = new GameObject("Elsa rope cut"); objects.Add(go);
        var hook = go.AddComponent<Hook>();
        hook.BeginExtended(new Vector2(-2,0), Vector2.right, 5f, 4f, Color.yellow);
        Physics2D.SyncTransforms();
        Check(elsa.TryCutRopes() && hook.Defended, "J rope cut works independently of Elsa K state");
    }
    public static void Run()
    {
        SessionState.SetBool(Pending, true); Watch(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Watch()
    {
        EditorApplication.update -= Poll;
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= Poll; SessionState.SetBool(Pending, false); Execute();
    }
    static void Execute()
    {
        int exit = 0;
        bool hadTuning = PlayerPrefs.HasKey("gm.settings.elsa.v1");
        string tuning = PlayerPrefs.GetString("gm.settings.elsa.v1");
        bool hadChoice = PlayerPrefs.HasKey("gm.skill.elsa");
        string choice = PlayerPrefs.GetString("gm.skill.elsa");
        PlayerPrefs.DeleteKey("gm.settings.elsa.v1"); PlayerPrefs.DeleteKey("gm.skill.elsa");
        try
        {
            Time.timeScale = 1f; GameManager.Instance = null; Hook.RopeCuts = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/Penguin.prefab");
            var go = UnityEngine.Object.Instantiate(prefab); objects.Add(go); go.transform.position = Vector3.zero;
            var elsa = go.GetComponent<PenguinController>(); var pose = Skill(elsa).mouthPose;
            Check(Skill(elsa).skillCooldown == ((ColdBreathSkillDefinition)Skill(elsa).Definition).skillCooldown, "Elsa uses the configured cooldown");
            Check(Skill(elsa).skillDuration == 1.5f && Skill(elsa).afterEffectDuration == .5f, "major wave and trailing particles have separate durations");
            Check(!Skill(elsa).IsCasting && elsa.HasSkill && pose.ice.activeSelf && pose.mouthHalfArc == 0f,
                "ready Elsa has congealed ice and her resting mouth pose");
            go.GetComponent<Rigidbody2D>().linearVelocity = Vector2.right * 5f;
            K(elsa);
            Check(go.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "casting immediately stops existing movement");
            Check(Skill(elsa).IsCasting && elsa.SkillActive && !elsa.IsBlocking, "K starts Elsa's own skill");
            // Exercise the actual physics movement path while the skill is active.
            var physicsBody = go.GetComponent<Rigidbody2D>();
            var simulation = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Vector2 beforeMove = physicsBody.position;
            try
            {
                foreach (Vector2 direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
                {
                    typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, direction);
                    typeof(PlayerCharacter).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(elsa, null);
                    Vector2 at = physicsBody.position;
                    Physics2D.Simulate(Time.fixedDeltaTime);
                    Check(Vector2.Distance(physicsBody.position, at) < .0001f && physicsBody.linearVelocity == Vector2.zero && Skill(elsa).IsCasting,
                        "Cold Breath blocks movement input in every direction");
                }
                Vector2 pinned = physicsBody.position;
                physicsBody.AddForce(Vector2.one * 200f, ForceMode2D.Impulse);
                Physics2D.Simulate(Time.fixedDeltaTime);
                Check(Vector2.Distance(pinned, physicsBody.position) < .0001f, "casting position is pinned against external physics impulses");
            }
            finally
            {
                physicsBody.position = beforeMove; physicsBody.linearVelocity = Vector2.zero;
                typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.zero);
                Physics2D.simulationMode = simulation;
            }
            Check(!pose.ice.activeSelf && Mathf.Abs(pose.mouthHalfArc - pose.OpeningForHalfArc(Skill(elsa).breathHalfArc)) < .01f,
                "casting consumes ice and opens the mouth to the breath cone");
            Check(Skill(elsa).BreathEffect != null && Skill(elsa).BreathEffect.gameObject.activeSelf
                && Skill(elsa).BreathEffect.externalPlayback && !Skill(elsa).BreathEffect.loop, "K starts the imported breath as a skill-driven one-shot");
            pose.TryGetMouthFrame(out Vector3 mouthAt, out Vector2 mouthForward);
            Check(Vector3.Distance(Skill(elsa).BreathEffect.transform.position, mouthAt + (Vector3)mouthForward * Skill(elsa).breathMouthOffset) < .001f,
                "breath origin is anchored in front of the mouth");
            Check(Mathf.Abs(Vector2.Angle(pose.upperBeak.TransformVector(Vector3.up), mouthForward) - Skill(elsa).breathHalfArc) < .01f
                && Mathf.Abs(Vector2.Angle(pose.lowerBeak.TransformVector(Vector3.up), mouthForward) - Skill(elsa).breathHalfArc) < .01f,
                "both beak halves match the cone half-arc");
            foreach (SpriteRenderer ice in pose.ice.GetComponentsInChildren<SpriteRenderer>(true))
                Check(Mathf.Abs(ice.color.r - .8f) < .001f && Mathf.Abs(ice.color.g - .86f) < .001f
                    && Mathf.Abs(ice.color.b - .9f) < .001f, "ready ice matches the effect's pale cold palette");
            Set(elsa, "castStarted", Time.time - .25f); float started = Get(elsa, "castStarted"); K(elsa);
            Check(Get(elsa, "castStarted") == started, "repeated K cannot restart an active skill");
            Skill(elsa).matchMouthToBreath = false;
            Skill(elsa).skillMouthHalfArc = 32f; Call(elsa, "UpdateSkillPose");
            Check(pose.mouthHalfArc == 32f, "active mouth half-arc can be tuned live");
            Quaternion castingFacing = elsa.visualRoot.localRotation;
            Vector3 castingScale = elsa.visualRoot.localScale;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.up);
            Call(elsa, "LateUpdate");
            Check(Quaternion.Angle(elsa.visualRoot.localRotation, castingFacing) < .01f && elsa.visualRoot.localScale == castingScale && pose.mouthHalfArc == 32f,
                "casting keeps Elsa's facing fixed while her mouth animates");
            Check(elsa.flipCost == .5f && elsa.rotationCostPer45 == 1f, "facing weights match the requested costs");
            // Isolate pose-cost checks from the separately tested tap/hold heading delay.
            elsa.nearbyTurnDelay = 0f;
            foreach (float mirrorSign in new[] { -1f, 1f })
            for (int initialStep = 0; initialStep < 8; initialStep++)
            foreach (Vector2 direction in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down,
                new Vector2(-1,1), new Vector2(1,1), new Vector2(-1,-1), new Vector2(1,-1) })
            {
                Skill(elsa).Cancel();
                go.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
                float initialAngle = initialStep * 45f;
                Vector3 scale = elsa.visualRoot.localScale;
                scale.x = Mathf.Abs(scale.x) * mirrorSign;
                elsa.visualRoot.localScale = scale;
                elsa.visualRoot.localRotation = Quaternion.Euler(0,0,initialAngle);
                typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, direction);
                Call(elsa, "LateUpdate");
                K(elsa);
                Vector2 forward = elsa.visualRoot.TransformVector(Vector3.left);
                Check(Vector2.Dot(Skill(elsa).BreathEffect.transform.up, direction.normalized) > .9999f,
                    "breath follows every flip and rotation without pointing backward");
                pose.TryGetMouthFrame(out Vector3 anchor, out Vector2 aimed);
                Check(Vector3.Distance(Skill(elsa).BreathEffect.transform.position, anchor + (Vector3)aimed * Skill(elsa).breathMouthOffset) < .001f,
                    "breath stays attached to the moving mouth");
                Check(Vector2.Dot(forward.normalized, direction.normalized) > .9999f,
                    "minimum-cost pose faces the requested direction");
                float wanted = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                float minimum = float.PositiveInfinity;
                foreach (float candidateSign in new[] { -1f, 1f })
                {
                    float angle = wanted - (candidateSign < 0f ? 0f : 180f);
                    float cost = Mathf.Abs(Mathf.DeltaAngle(initialAngle, angle)) / 45f
                        + (candidateSign == mirrorSign ? 0f : .5f);
                    minimum = Mathf.Min(minimum, cost);
                }
                float actual = Mathf.Abs(Mathf.DeltaAngle(initialAngle, elsa.visualRoot.localEulerAngles.z)) / 45f
                    + (Mathf.Sign(elsa.visualRoot.localScale.x) == mirrorSign ? 0f : .5f);
                Check(Mathf.Abs(actual - minimum) < .001f, "every direction uses the lowest available flip/rotation cost");
                Quaternion chosen = elsa.visualRoot.localRotation;
                Vector3 chosenScale = elsa.visualRoot.localScale;
                Call(elsa, "LateUpdate");
                Check(Quaternion.Angle(chosen, elsa.visualRoot.localRotation) < .01f && elsa.visualRoot.localScale == chosenScale,
                    "holding a direction does not alternate between equivalent poses");
            }
            Skill(elsa).matchMouthToBreath = true;
            foreach (float arc in new[] { 15f, 30f, 45f, 65f, 85f })
            {
                Skill(elsa).breathHalfArc = arc; Call(elsa, "UpdateSkillPose");
                pose.TryGetMouthFrame(out _, out Vector2 facing);
                Check(Mathf.Abs(Vector2.Angle(pose.upperBeak.TransformVector(Vector3.up), facing) - arc) < .02f
                    && Mathf.Abs(Vector2.Angle(pose.lowerBeak.TransformVector(Vector3.up), facing) - arc) < .02f,
                    "mouth follows adjustable breath half-arcs");
            }
            Skill(elsa).breathHalfArc = 45f; Skill(elsa).matchMouthToBreath = false; Call(elsa, "UpdateSkillPose");
            Vector3 lastFacing = elsa.visualRoot.localScale;
            Quaternion lastRotation = elsa.visualRoot.localRotation;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.zero);
            Call(elsa, "LateUpdate");
            Check(elsa.visualRoot.localScale == lastFacing && Quaternion.Angle(lastRotation, elsa.visualRoot.localRotation) < .01f,
                "idle preserves Elsa's chosen facing");
            Cut(elsa);
            Set(elsa, "castStarted", Time.time - Skill(elsa).skillDuration - .1f); Call(elsa, "Update");
            Check(!Skill(elsa).IsCasting && Mathf.Abs(elsa.SkillReadyIn - (Skill(elsa).skillCooldown - .1f)) < .01f,
                "cooldown starts at the major wave end, excluding trailing particles");
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.right);
            typeof(PlayerCharacter).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(elsa, null);
            Check(physicsBody.linearVelocity.x > 0f, "movement resumes as soon as casting ends, during cooldown");
            Check(physicsBody.constraints == RigidbodyConstraints2D.FreezeRotation, "cast end restores original physics constraints");
            physicsBody.linearVelocity = Vector2.zero;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.zero);
            Check(!pose.ice.activeSelf && pose.mouthHalfArc == 0f, "cooldown closes the mouth and keeps ice absent");
            Check(Skill(elsa).BreathEffect.gameObject.activeSelf && Skill(elsa).BreathEffect.transform.parent == null
                && Skill(elsa).BreathEffect.ContactLength == 0f, "visual-only particles remain after casting without frost contact");
            Vector3 tailPosition = Skill(elsa).BreathEffect.transform.position;
            Quaternion tailRotation = Skill(elsa).BreathEffect.transform.rotation;
            go.transform.position += Vector3.right;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.up);
            Call(elsa, "LateUpdate");
            Check(Skill(elsa).BreathEffect.transform.position == tailPosition
                && Quaternion.Angle(Skill(elsa).BreathEffect.transform.rotation, tailRotation) < .01f,
                "trailing particles stay at the cast location when Elsa walks and turns");
            go.transform.position -= Vector3.right;
            typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, Vector2.zero);
            Set(elsa, "afterStarted", Time.time - Skill(elsa).afterEffectDuration - .1f); Call(elsa, "UpdateSkillPose");
            Check(!Skill(elsa).BreathEffect.gameObject.activeSelf, "particles disappear when their independent fade duration ends");
            K(elsa); Check(!Skill(elsa).IsCasting, "cooldown blocks K reactivation"); Cut(elsa);
            Set(elsa, "readyAt", Time.time + 3.5f); Call(elsa, "UpdateSkillPose");
            Check(!pose.ice.activeSelf, "ice does not return halfway through cooldown");
            Set(elsa, "readyAt", Time.time); Call(elsa, "UpdateSkillPose");
            Check(pose.ice.activeSelf && elsa.SkillReadyIn == 0f, "ice reforms exactly when the skill becomes ready");
            Time.timeScale = 0f; K(elsa);
            Check(!Skill(elsa).IsCasting && pose.ice.activeSelf, "pause prevents spending the ready ice");
            Time.timeScale = 1f; K(elsa);
            Check(Skill(elsa).IsCasting && !pose.ice.activeSelf, "a ready skill can activate again");
            Check(Skill(elsa).BreathEffect.transform.parent == elsa.transform
                && Skill(elsa).BreathEffect.transform.localScale == Skill(elsa).coldBreathPrefab.transform.localScale,
                "next cast reattaches the reusable effect without accumulating scale");
            var breathRenderer = Skill(elsa).BreathEffect.GetComponent<MeshRenderer>();
            var frame = new MaterialPropertyBlock(); breathRenderer.GetPropertyBlock(frame);
            Vector4 beforePause = frame.GetVector("_FrameRect");
            Time.timeScale = 0f; Call(elsa, "UpdateSkillPose");
            breathRenderer.GetPropertyBlock(frame);
            Check(beforePause == frame.GetVector("_FrameRect"), "paused skill freezes the breath frame");
            Check(Skill(elsa).IsCasting && pose.mouthHalfArc == 32f, "pause keeps the current cast pose"); Time.timeScale = 1f;
            var roundHost = new GameObject("Ended round"); objects.Add(roundHost);
            var round = roundHost.AddComponent<GameManager>(); GameManager.Instance = round;
            typeof(GameManager).GetProperty("IsPlaying").SetValue(round, false); Call(elsa, "Update");
            Check(!Skill(elsa).IsCasting && pose.ice.activeSelf && pose.mouthHalfArc == 0f,
                "round end cancels the cast and restores the ready artwork");
            GameManager.Instance = null; K(elsa); elsa.enabled = false;
            Check(!Skill(elsa).IsCasting && pose.ice.activeSelf && pose.mouthHalfArc == 0f,
                "disabling Elsa cannot leave her mouth stuck open");
            Check(physicsBody.constraints == RigidbodyConstraints2D.FreezeRotation, "disabling Elsa releases the casting position lock");
            Debug.Log("ELSA_SKILL_PLAY_PASS: " + checks + " mouth, ice, cooldown, input, movement and lifecycle checks.");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear(); Time.timeScale = 1f; GameManager.Instance = null;
            if (hadTuning) PlayerPrefs.SetString("gm.settings.elsa.v1",tuning); else PlayerPrefs.DeleteKey("gm.settings.elsa.v1");
            if (hadChoice) PlayerPrefs.SetString("gm.skill.elsa",choice); else PlayerPrefs.DeleteKey("gm.skill.elsa");
            PlayerPrefs.Save();
        }
        EditorApplication.Exit(exit);
    }
}
