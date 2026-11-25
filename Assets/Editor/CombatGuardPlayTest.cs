using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Real Play-mode colliders and damage entry points, isolated from level generation.
public static class CombatGuardPlayTest
{
    const string Pending = "gm.tests.combatGuard";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static Sprite sprite;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Hidden).Invoke(obj, args);
    static void Set(object obj, string name, object value)
        => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
    static GameObject Make(string name, Vector2 at)
    { var go = new GameObject(name); objects.Add(go); go.transform.position = at; return go; }
    static Rock Stone(Vector2 at)
    {
        var go = Make("Guard test rock", at);
        go.AddComponent<BoxCollider2D>().size = Vector2.one * .1f;
        go.AddComponent<SpriteRenderer>().sprite = sprite;
        return go.AddComponent<Rock>();
    }
    static RedSquare Enemy(Vector2 at, Transform target)
    {
        var go = Make("Guard test enemy", at);
        go.AddComponent<BoxCollider2D>().size = Vector2.one * .2f;
        go.AddComponent<SpriteRenderer>().sprite = sprite;
        var enemy = go.AddComponent<RedSquare>(); enemy.target = target;
        Call(enemy, "Start"); Set(enemy, "player", target.GetComponent<PlayerCharacter>());
        return enemy;
    }
    static void Frost(Transform root)
    {
        FrostField2D.Expose(null, (Vector2)root.position - Vector2.right, Vector2.right, 2f, 45f);
        var field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        Set(field, "candidates", null); Call(field, "LateUpdate");
        Check(FrostField2D.IsFrozen(root), "Fixture receives real frost patches");
    }
    public static void Run() { SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode(); }
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int exit = 0;
        try { Execute(); }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            var field = UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
            if (field != null) UnityEngine.Object.DestroyImmediate(field.gameObject);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            objects.Clear(); GameManager.Instance = null; Time.timeScale = 1f; PlayerCharacter.CooldownScale = 1f;
        }
        EditorApplication.Exit(exit);
    }
    static void Execute()
    {
        Time.timeScale = 1f; GameManager.Instance = null; PlayerCharacter.CooldownScale = 1f;
        sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,2,2), Vector2.one * .5f, 2f);
        var gm = Make("Guard test round", Vector2.zero).AddComponent<GameManager>();
        GameManager.Instance = gm; gm.SetLives(10); gm.invincibleTime = 0f;
        typeof(GameManager).GetProperty("IsPlaying").SetValue(gm, true);
        var host = Make("Guard test Hulk", Vector2.zero);
        host.AddComponent<BoxCollider2D>();
        var hulk = host.AddComponent<RhinoController>();
        typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(hulk, Vector2.right);
        Physics2D.SyncTransforms(); Call(hulk, "UseSkill");
        var guard = host.GetComponent<HulkChargeGuard>();
        Check(guard.IsGuarding && Mathf.Abs(guard.Radius - .75f) < .001f, "Guard starts at K activation, collider-sized");
        Check(hulk.skillDuration == .8f && hulk.skillCooldown == 5f, "Charge keeps 0.8 seconds and five-second base cooldown");
        foreach (float angle in new[] { -29f, 0f, 29f })
            Check(guard.ContainsAttack(Quaternion.Euler(0,0,angle) * Vector2.right * .7f), "Attack stays inside front 60-degree sector");
        foreach (float angle in new[] { -31f, 31f, 90f, 180f })
            Check(!guard.ContainsAttack(Quaternion.Euler(0,0,angle) * Vector2.right * .7f), "Attack excludes sides and rear");
        for (int angle = 0; angle < 360; angle += 15)
            Check(guard.Contains(Quaternion.Euler(0,0,angle) * Vector2.right * .7f), "Shield protects every direction");
        Check(!guard.Contains(new Vector2(.76f,0)), "Radius boundary excludes points beyond range");
        host.transform.localScale = Vector3.one * .7f; Physics2D.SyncTransforms();
        Check(Mathf.Abs(guard.Radius - .525f) < .001f, "Guard scales with body collider");
        host.transform.localScale = Vector3.one; Physics2D.SyncTransforms();
        Call(guard, "LateUpdate");
        var line = host.transform.Find("Hulk 360 degree shield").GetComponent<LineRenderer>();
        var attackLine = host.transform.Find("Hulk 60 degree attack").GetComponent<LineRenderer>();
        Check(line.enabled && line.loop && line.positionCount == 64, "Shield has a complete visible circumference");
        for (int i = 0; i < line.positionCount; i++)
            Check(Mathf.Abs(Vector2.Distance(line.GetPosition(i),guard.Centre)-guard.Radius)<.001f, "Shield outline matches protection radius");
        Check(attackLine.enabled && attackLine.loop && attackLine.positionCount == 26
            && Mathf.Abs(Vector2.SignedAngle(guard.Forward,(Vector2)attackLine.GetPosition(1)-guard.Centre)+30f)<.01f,
            "Separate attack outline displays only the front 60 degrees");
        Set(hulk,"chargeVelocity",Vector2.left * 5f);
        Check(guard.ContainsAttack(Vector2.left*.7f) && !guard.ContainsAttack(Vector2.right*.7f)
            && guard.Contains(Vector2.right*.7f), "Turning rotates the attack but preserves all-around protection");
        Set(hulk,"chargeVelocity",Vector2.zero);

        var hook = Make("Incoming guard test hook", Vector2.zero).AddComponent<Hook>();
        hook.BeginExtended(new Vector2(3,0), Vector2.left, 5f, 2.4f, Color.red);
        int lives = gm.Lives;
        hook.ApplyHit(hulk);
        Check(hook.Defended && !hook.HitPlayer && gm.Lives == lives, "Damage entry deflects immediately without losing HP");
        hook.ApplyHit(hulk);
        Check(gm.Lives == lives, "Already deflected claw cannot inflict a second scripted hit");
        hook.StopShot(); hook.BeginExtended(new Vector2(3,0), Vector2.left, 5f, 2.4f, Color.red);
        Call(hook,"HandlePlayer", hulk, host.GetComponent<Collider2D>());
        Check(hook.Defended && gm.Lives == lives, "Normal collision damage entry also protects before HP loss");
        var remote = Make("Remote rope crossing Hulk", Vector2.zero).AddComponent<Hook>();
        remote.BeginExtended(new Vector2(-3,0), Vector2.right, 8f, 6f, Color.red);
        Call(guard, "Update");
        Check(!remote.Defended && remote.IsBusy, "K leaves distant claws and crossing ropes alone");
        Check(guard.TryDeflect(remote, new Vector2(2,0), new Vector2(-2,0)) && remote.Defended,
            "Swept fast claw cannot tunnel through the sector");
        var rear = Make("Rear hook", Vector2.zero).AddComponent<Hook>();
        rear.BeginExtended(new Vector2(-3,0), Vector2.right, 5f, 2.4f, Color.red);
        rear.ApplyHit(hulk);
        Check(rear.Defended && gm.Lives == lives, "Rear claws are repelled without HP loss");
        var side = Make("Side hook",Vector2.zero).AddComponent<Hook>();
        side.BeginExtended(new Vector2(0,3),Vector2.down,5f,2.4f,Color.red);
        Call(side,"HandlePlayer",hulk,host.GetComponent<Collider2D>());
        Check(side.Defended && gm.Lives==lives, "Side claws are blocked at the normal damage entry");
        var outside = Make("Outside shield hook",Vector2.zero).AddComponent<Hook>();
        outside.BeginExtended(new Vector2(-3,0),Vector2.right,5f,1.8f,Color.red);
        outside.ApplyHit(hulk);
        Check(!outside.Defended && gm.Lives==lives-1, "Protection does not extend beyond the displayed radius");

        var treasureGo = Make("Carried guard treasure", new Vector2(.65f,0));
        treasureGo.AddComponent<CircleCollider2D>();
        var treasure = treasureGo.AddComponent<Treasure>();
        var carrying = Make("Carrying guard hook", Vector2.zero).AddComponent<Hook>();
        carrying.PullBack(new Vector2(3,0), treasure, Color.red, 5f);
        Check(carrying.Carried == treasure && treasure.IsTaken, "Fixture claw carries treasure");
        guard.TryDeflect(carrying, carrying.transform.position, carrying.transform.position);
        Check(carrying.Defended && carrying.Carried == null && !treasure.IsTaken && treasure.transform.parent == null,
            "Repelled claw drops treasure immediately and returns empty");
        treasureGo.SetActive(false);

        var fixedHole = Make("Fixed hook spawn", new Vector2(.65f,0));
        fixedHole.AddComponent<BoxCollider2D>().size = Vector2.one * .1f;
        fixedHole.AddComponent<SurvivalHookAI>(); Physics2D.SyncTransforms(); Call(guard,"Update");
        Check((Vector2)fixedHole.transform.position == new Vector2(.65f,0) && fixedHole.GetComponent<Rigidbody2D>() == null,
            "Fixed hook spawn is never knocked back");
        fixedHole.SetActive(false);

        var enemy = Enemy(new Vector2(.65f,0), host.transform); Physics2D.SyncTransforms();
        lives = gm.Lives;
        int flashes = UnityEngine.Object.FindObjectsByType<BlockFlash>(FindObjectsSortMode.None).Length;
        Call(enemy,"Ram");
        Check(gm.Lives == lives && enemy.LastRamBlocked, "Contact damage entry guards before HP loss");
        Check(UnityEngine.Object.FindObjectsByType<BlockFlash>(FindObjectsSortMode.None).Length==flashes+1,
            "Mobile-enemy repel shows the shield impact effect just like claw blocks");
        var pushSpeed = typeof(RedEnemy).GetField("pushSpeed", Hidden);
        var pushUntil = typeof(RedEnemy).GetField("pushUntil", Hidden);
        Check(Mathf.Abs((float)pushSpeed.GetValue(enemy) - 10f) < .001f
            && Mathf.Abs((float)pushUntil.GetValue(enemy) - Time.time - .2f) < .001f,
            "Knockback is two units over 0.2 seconds");
        pushUntil.SetValue(enemy, Time.time + .1f); Call(enemy,"Ram");
        Check(Mathf.Abs((float)pushUntil.GetValue(enemy) - Time.time - .1f) < .001f, "Same enemy is repelled only once per charge");
        Check(UnityEngine.Object.FindObjectsByType<BlockFlash>(FindObjectsSortMode.None).Length==flashes+1,
            "Repeated contact does not spam shield impact effects");
        enemy.transform.position = new Vector2(-.65f,0); Physics2D.SyncTransforms(); Call(enemy,"Ram");
        Check(gm.Lives == lives && enemy.LastRamBlocked, "Rear contact attack is protected too");
        enemy.gameObject.SetActive(false);

        var displaced = Enemy(new Vector2(10,10), host.transform);
        displaced.PushBack(Vector2.right, 2f, .2f);
        var simulation = Physics2D.simulationMode;
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Vector2 start = displaced.GetComponent<Rigidbody2D>().position;
            for (int i = 0; i < 10; i++) { Call(displaced,"FixedUpdate"); Physics2D.Simulate(.02f); }
            Check(Vector2.Distance(displaced.GetComponent<Rigidbody2D>().position, start + Vector2.right * 2f) < .01f,
                "Real enemy physics travels two world units in ten 0.02-second steps");
        }
        finally { Physics2D.simulationMode = simulation; displaced.gameObject.SetActive(false); }

        var frozenEnemy = Enemy(new Vector2(3,3), host.transform);
        Frost(frozenEnemy.transform);
        lives = gm.Lives; Call(frozenEnemy,"Ram");
        Check(gm.Lives == lives, "Frozen enemy cannot deal contact damage");
        frozenEnemy.GetComponent<Rigidbody2D>().linearVelocity = Vector2.right * 5;
        Call(frozenEnemy,"FixedUpdate");
        Check(frozenEnemy.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero,
            "Frozen enemy stops all movement before chase and knockback");
        Check(FrostField2D.FrostDuration == 2.5f, "Frost lifetime is 2.5 seconds after last exposure");
        frozenEnemy.transform.position = new Vector2(.65f,0); Physics2D.SyncTransforms();
        Check(HulkChargeGuard.TryBlockEnemy(hulk,frozenEnemy) && !frozenEnemy.IsFrozen,
            "Charge shatters enemy frost before applying physical knockback");
        frozenEnemy.gameObject.SetActive(false);

        foreach (Type type in new[] { typeof(RedRound), typeof(GoldRound) })
        {
            var walker = Make("Frozen " + type.Name, new Vector2(8,8));
            walker.AddComponent<SpriteRenderer>().sprite = sprite;
            walker.AddComponent<BoxCollider2D>();
            var mobile = (RedEnemy)walker.AddComponent(type); mobile.target = host.transform; Call(mobile,"Start");
            Frost(walker.transform); var mobileBody = walker.GetComponent<Rigidbody2D>();
            mobileBody.linearVelocity = Vector2.right * 5f; Call(mobile,"FixedUpdate");
            Check(mobileBody.linearVelocity == Vector2.zero && walker.GetComponent<SurvivalHookAI>().IsFrozen,
                type.Name + " stops physical movement and its shooting controller while frozen");
            walker.SetActive(false);
        }

        var rock = Stone(new Vector2(.65f,0)); Physics2D.SyncTransforms();
        Check(guard.TrySmashRock(rock) && !rock.GetComponent<Collider2D>().enabled,
            "Ordinary rock breaks immediately despite nonzero break animation duration");
        var iced = Stone(new Vector2(.65f,0)); Frost(iced.transform); Physics2D.SyncTransforms();
        Check(guard.TrySmashRock(iced) && !iced.IsFrozen && !iced.GetComponent<Collider2D>().enabled,
            "Frozen rock loses ice then breaks immediately");
        var green = Stone(new Vector2(.65f,0)); green.hookPasses = true; Physics2D.SyncTransforms();
        Check(!guard.TrySmashRock(green) && green.GetComponent<Collider2D>().enabled, "Locked green barrier stays intact");
        green.gameObject.SetActive(false);
        var sideRock = Stone(new Vector2(0,.65f)); Physics2D.SyncTransforms();
        Check(!guard.TrySmashRock(sideRock), "Side rocks outside sector stay intact");
        var wall = Make("Near solid wall", new Vector2(.3f,0));
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.1f,1f);
        var occluded = Stone(new Vector2(.65f,0)); Physics2D.SyncTransforms();
        Check(!guard.TrySmashRock(occluded), "Sector cannot break rocks through a nearer solid wall");
        Set(hulk,"chargeStarted", Time.time - .81f); Call(guard,"LateUpdate");
        Check(!guard.IsGuarding && !line.enabled && !attackLine.enabled && !guard.Contains(Vector2.right * .5f),
            "Protection and both outlines expire together at charge end");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Teams/Rhino.prefab");
        var real = UnityEngine.Object.Instantiate(prefab); objects.Add(real); real.transform.position = new Vector2(20,20);
        Physics2D.SyncTransforms(); Call(real.GetComponent<RhinoController>(),"UseSkill");
        var realGuard = real.GetComponent<HulkChargeGuard>();
        Check(realGuard.IsGuarding && Mathf.Abs(realGuard.Radius - .64575f) < .001f,
            "Real Hulk prefab uses the requested approximately 0.65-world-unit guard");
        Debug.Log("COMBAT_GUARD_PLAY_PASS: " + checks + " sector, damage, treasure, knockback, frost and obstacle checks.");
    }
}
