using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ElsaHeadingPlayTest
{
    const string Pending = "gm.tests.elsaHeading";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Input(PenguinController elsa, Vector2 input)
    {
        typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(elsa, input);
        typeof(PenguinController).GetMethod("LateUpdate", Private).Invoke(elsa, null);
    }
    static Vector2 Facing(PenguinController elsa) => elsa.visualRoot.TransformVector(Vector3.left).normalized;
    static void Reset(PenguinController elsa, float angle)
    {
        Input(elsa, Vector2.zero);
        Vector3 scale = elsa.visualRoot.localScale; scale.x = -Mathf.Abs(scale.x);
        elsa.visualRoot.localScale = scale;
        elsa.visualRoot.localRotation = Quaternion.Euler(0, 0, angle);
    }
    public static void Run() { SessionState.SetBool(Pending, true); Watch(); EditorApplication.EnterPlaymode(); }
    [InitializeOnLoadMethod]
    static void Watch()
    {
        EditorApplication.update -= Poll;
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount < 3) return;
        EditorApplication.update -= Poll; SessionState.SetBool(Pending, false);
        GameObject go = null; int exit = 0;
        try
        {
            Time.timeScale = 1; GameManager.Instance = null;
            go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/Penguin.prefab"));
            var elsa = go.GetComponent<PenguinController>(); elsa.SkillSlot.Equip(null);
            var body = go.GetComponent<Rigidbody2D>();
            Check(elsa.nearbyTurnDelay > 0, "nearby stepping enabled by default");
            for (int step = 0; step < 8; step++)
            foreach (Vector2 input in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
            {
                Reset(elsa, step * 45);
                Vector2 before = Facing(elsa);
                float turn = Vector2.SignedAngle(before, input);
                bool staged = Mathf.Abs(turn) > 45.01f && Mathf.Abs(turn) <= 90.01f;
                Vector2 expected = staged ? (Vector2)(Quaternion.Euler(0, 0, Mathf.Sign(turn) * 45) * before) : input;
                body.linearVelocity = -input * 5; // Old physics velocity must not choose heading.
                Input(elsa, input);
                Check(Vector2.Dot(Facing(elsa), expected.normalized) > .9999f, "nearby turn steps 45; distant or opposite turn stays direct");
                typeof(PlayerCharacter).GetMethod("FixedUpdate", Private).Invoke(elsa, null);
                Check(Vector2.Dot(body.linearVelocity.normalized, input) > .9999f, "single key movement remains cardinal");
                Input(elsa, Vector2.zero);
                Check(Vector2.Dot(Facing(elsa), expected.normalized) > .9999f, "quick release retains heading despite old velocity");
            }
            Reset(elsa, 0); Input(elsa, Vector2.up);
            typeof(PenguinController).GetField("finishHeadingAt", Private).SetValue(elsa, Time.time + elsa.nearbyTurnDelay * .5f);
            Input(elsa, Vector2.up);
            Check(Mathf.Abs(Vector2.SignedAngle(Vector2.right, Facing(elsa)) - 67.5f) < .02f,
                "held turn moves smoothly past 45 degrees before its finish time");
            Input(elsa, Vector2.zero);
            Check(Mathf.Abs(Vector2.SignedAngle(Vector2.right, Facing(elsa)) - 45f) < .02f,
                "releasing a short tap retains the diagonal heading");
            Reset(elsa, 0); Input(elsa, Vector2.up);
            typeof(PenguinController).GetField("finishHeadingAt", Private).SetValue(elsa, Time.time - .01f);
            Input(elsa, Vector2.up);
            Check(Vector2.Dot(Facing(elsa), Vector2.up) > .9999f, "held nearby key completes second step");
            Reset(elsa, 0); Input(elsa, Vector2.up); Input(elsa, Vector2.zero); Input(elsa, Vector2.up);
            Check(Vector2.Dot(Facing(elsa), Vector2.up) > .9999f, "second tap from diagonal takes remaining 45 degrees");
            foreach (Vector2 diagonal in new[] { new Vector2(1,1), new Vector2(-1,1), new Vector2(1,-1), new Vector2(-1,-1) })
            {
                Reset(elsa, 0); Input(elsa, diagonal);
                typeof(PlayerCharacter).GetMethod("FixedUpdate", Private).Invoke(elsa, null);
                Check(Vector2.Dot(Facing(elsa), diagonal.normalized) > .9999f, "two keys face diagonal directly");
                Check(Vector2.Dot(body.linearVelocity.normalized, diagonal.normalized) > .9999f, "two keys move diagonally");
            }
            Vector2 last = Facing(elsa); Input(elsa, Vector2.zero);
            typeof(PlayerCharacter).GetMethod("FixedUpdate", Private).Invoke(elsa, null);
            Check(body.linearVelocity == Vector2.zero && Vector2.Dot(last, Facing(elsa)) > .9999f, "cancelled opposite inputs stop without turning");
            Check(Quaternion.Angle(go.transform.rotation, Quaternion.identity) < .01f, "heading does not rotate physics body");
            Debug.Log("ELSA HEADING PASS: " + checks + " checks");
        }
        catch (Exception ex) { Debug.LogException(ex); exit = 1; }
        finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); EditorApplication.Exit(exit); }
    }
}
