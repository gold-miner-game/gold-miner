using System;
using UnityEditor;
using UnityEngine;

public static class ElsaLoadoutPlayTest
{
    const string Pending = "gm.tests.elsaLoadout";
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static void Run() { SessionState.SetBool(Pending, true); Watch(); EditorApplication.EnterPlaymode(); }
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
        int exit = 0; GameObject go = null, second = null;
        try
        {
            GameManager.Instance = null; Time.timeScale = 1f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/Penguin.prefab");
            Check(prefab.GetComponent<CharacterSkill>() == null && prefab.GetComponent<RuntimeSkillSlot>() == null, "prefab has no attached skill");
            go = UnityEngine.Object.Instantiate(prefab);
            var elsa = go.GetComponent<PenguinController>(); var slot = elsa.SkillSlot;
            var definition = slot.CurrentDefinition as ColdBreathSkillDefinition;
            var skill = slot.ActiveSkill as ElsaColdBreathSkill;
            Check(definition != null && skill != null, "external loadout creates the live skill");
            float defaultDuration = definition.skillDuration, defaultCooldown = definition.skillCooldown;
            Check(go.GetComponent("ElsaRuntimeSettings") == null, "no gameplay settings panel is created");
            skill.skillDuration = 3f; skill.skillCooldown = 9f; skill.breathHalfArc = 35f; skill.breathLength = 6f;
            skill.mouthPose.iceTint = new Color(.2f, .6f, .7f); skill.mouthPose.ApplyPose();
            Check(slot.TryActivate() && elsa.SkillActive, "live skill activates after tuning");
            Check(skill.BreathEffect.halfArc == 35f && skill.BreathEffect.length == 6f, "Inspector fields control the live effect");
            Check(definition.skillDuration == defaultDuration && definition.skillCooldown == defaultCooldown, "live tuning leaves shared defaults intact");
            var effect = skill.BreathEffect;
            slot.Equip(definition);
            Check(slot.ActiveSkill == skill && elsa.SkillActive, "same selection preserves active cast");
            slot.Equip(null);
            Check(!elsa.HasSkill && !effect.gameObject.activeSelf && !skill.mouthPose.ice.activeSelf, "clearing Inspector skill cancels effects and ice");
            slot.Equip(definition);
            Check(slot.ActiveSkill != skill && elsa.HasSkill, "Inspector can re-equip a fresh skill");
            second = UnityEngine.Object.Instantiate(prefab);
            Check(((ElsaColdBreathSkill)second.GetComponent<PenguinController>().SkillSlot.ActiveSkill).skillCooldown == defaultCooldown, "other instances retain default values");
            Debug.Log("ELSA LOADOUT PASS: " + checks + " checks");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
        }
        EditorApplication.Exit(exit);
    }
}
