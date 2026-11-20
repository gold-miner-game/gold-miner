using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FrostEffect = FrostBody2D.FrostBody2D;

// Isolated play-mode regression tests for frost work restrictions and ice-breaking impacts.
public static class FrostGameplayPlayTest
{
    const string Pending = "gm.tests.frostGameplay";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static Sprite square;
    static FrostField2D field;
    static FrostEffect template;
    static int checks, nextCase;
    static object Get(object target, string name) => target.GetType().GetField(name,Hidden).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name,Hidden).SetValue(target,value);
    static object Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name,BindingFlags.Public|Hidden).Invoke(target,args);
    static void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
    static GameObject Make(string name, Vector2 at)
    {
        var go=new GameObject(name); go.transform.position=at; objects.Add(go); return go;
    }
    static Vector2 Case() => new Vector2(100f + nextCase++ * 40f,0);
    static SpriteRenderer Art(GameObject go)
    {
        var art=go.AddComponent<SpriteRenderer>(); art.sprite=square; art.color=Color.cyan; return art;
    }
    static Hook Shot(Vector2 from, float extended=0f)
    {
        var go=Make("Frost test hook",from); Art(go);
        var hook=go.AddComponent<Hook>(); hook.wallGrace=0f; hook.extendSpeed=hook.retractSpeed=10000f;
        if(extended>0f) hook.BeginExtended(from,Vector2.right,12f,extended,Color.yellow);
        else hook.Fire(from,Vector2.right,12f,Color.yellow);
        Call(hook,"UpdateRope"); Call(hook,"UpdateClaw"); return hook;
    }
    static LineRenderer Part(Hook hook,string name) => hook.transform.Find(name).GetComponent<LineRenderer>();
    static Treasure Gold(Vector2 at)
    {
        var go=Make("Frozen gold",at); Art(go); go.AddComponent<BoxCollider2D>().isTrigger=true;
        return go.AddComponent<Treasure>();
    }
    static IList Cones(params object[] cones)
    {
        Type cone=typeof(FrostField2D).GetNestedType("Cone",BindingFlags.NonPublic);
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(cone));
        foreach(var item in cones)list.Add(item); return list;
    }
    static object Freeze(Renderer renderer,float from=0f,float to=1f)
    {
        Type cone=typeof(FrostField2D).GetNestedType("Cone",BindingFlags.NonPublic);
        Vector2 center=renderer is LineRenderer line ? (Vector2)line.GetPosition(0) : (Vector2)renderer.bounds.center;
        object area=Activator.CreateInstance(cone,new object[]{null,center-Vector2.right*100f,Vector2.right,200f,89f});
        Type surface=typeof(FrostField2D).GetNestedType("Surface",BindingFlags.NonPublic);
        object value=Activator.CreateInstance(surface,new object[]{renderer,template});
        Check((bool)Call(value,"Tick",Cones(area),Time.time),"Fixture receives real frost coverage");
        var expires=(float[])Get(value,"expires"); int width=(int)Get(value,"width"),height=(int)Get(value,"height");
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            expires[y*width+x]=x>=Mathf.CeilToInt(from*width)&&x<Mathf.FloorToInt(to*width)?Time.time+FrostField2D.FrostDuration:0f;
        Set(value,"activeUntil",Time.time+FrostField2D.FrostDuration); Call(value,"Tick",Cones(),Time.time);
        var all=(IDictionary)Get(field,"surfaces");
        if(all.Contains(renderer))Call(all[renderer],"Dispose"); all[renderer]=value;
        return value;
    }
    static void SyncRope(Hook hook, object ropeSurface)
    { Call(hook,"UpdateRope"); Call(ropeSurface,"Tick",Cones(),Time.time); }

    public static void Run(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
    [InitializeOnLoadMethod]static void Watch(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.frameCount<3||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);EditorApplication.update-=Poll;int exit=0;
        try{Execute();}
        catch(Exception error){Debug.LogException(error);exit=1;}
        finally
        {
            foreach(var go in objects)if(go!=null)UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();if(field!=null)UnityEngine.Object.DestroyImmediate(field.gameObject);
            if(square!=null)UnityEngine.Object.DestroyImmediate(square);Time.timeScale=1f;
        }
        EditorApplication.Exit(exit);
    }

    static void Execute()
    {
        Time.timeScale=1f;GameManager.Instance=null;Hook.RopeCuts=true;
        Check(Time.deltaTime>0f,"Play-mode movement clock is running");
        template=Resources.Load<FrostEffect>("Effects/FrostBody2D");
        Texture2D white=Texture2D.whiteTexture;
        square=Sprite.Create(white,new Rect(0,0,white.width,white.height),Vector2.one*.5f,white.width);
        FrostField2D.Expose(null,Vector2.zero,Vector2.right,1f,1f);
        field=UnityEngine.Object.FindFirstObjectByType<FrostField2D>();
        var pig=Make("Ordinary player",Case()).AddComponent<MrHydeController>();

        Vector2 origin=Case(); var hook=Shot(origin); var treasure=Gold(origin+Vector2.right*3f);
        Freeze(treasure.GetComponent<SpriteRenderer>());
        Check(treasure.IsFrozen&&!treasure.TryEat(pig),"Ordinary players cannot eat frozen treasure");
        Check(!treasure.GrabByHook(hook.transform)&&!treasure.IsTaken,"Every hook/tongue grab rejects frozen treasure");
        Physics2D.SyncTransforms();Call(hook,"Extend");
        Check(!treasure.IsFrozen&&!treasure.IsTaken&&hook.Carried==null,"Hook breaks treasure ice and catches nothing");
        Check(Get(hook,"state").ToString()=="Retracting","Frozen treasure sends the hook home");
        Call(hook,"Retract",true);Check(hook.Carried==null,"Same returning shot cannot collect the defrosted treasure");
        hook.StopShot();hook.Fire(origin,Vector2.right,12f);Call(hook,"Extend");
        Check(hook.Carried==treasure,"A later shot can collect the thawed treasure");
        Freeze(treasure.GetComponent<SpriteRenderer>());Call(hook,"Update");
        Check(hook.Carried==null&&!treasure.IsTaken&&treasure.GetComponent<Collider2D>().enabled,
            "Treasure frozen during transport is released instead of collected");

        origin=Case();hook=Shot(origin);var rockObject=Make("Frozen rock",origin+Vector2.right*3f);Art(rockObject);
        var rockCollider=rockObject.AddComponent<BoxCollider2D>();var rock=rockObject.AddComponent<Rock>();rock.breakStageTime=0f;
        Freeze(rockObject.GetComponent<SpriteRenderer>());
        Check(rock.IsFrozen&&!rock.Smash(),"Frozen blocks resist Rhino smashing");
        Physics2D.SyncTransforms();Call(hook,"Extend");
        Check(!rock.IsFrozen&&rockCollider.enabled&&hook.Carried==null,"Hook impact removes only block ice");
        Check(Get(hook,"state").ToString()=="Retracting","An icy block turns the hook around");

        origin=Case();hook=Shot(origin);treasure=Gold(origin+Vector2.right*3f);
        Freeze(Part(hook,"ClawLeft"));Freeze(Part(hook,"ClawRight"));
        Check(hook.IsFrozen,"Claw frost disables hook fetching");
        Physics2D.SyncTransforms();Call(hook,"Extend");
        Check(!hook.IsFrozen&&!treasure.IsTaken&&hook.Carried==null,"Frozen claw cracks its own ice and returns empty");
        hook=Shot(Case());Freeze(Part(hook,"ClawLeft"));
        var playerCollider=pig.gameObject.AddComponent<BoxCollider2D>();
        Check((bool)Call(hook,"HandleHit",playerCollider)&&!hook.IsFrozen&&!hook.HitPlayer&&pig.HooksBlocked==0,
            "A frozen claw's player impact only cracks ice, without damage or block rewards");

        origin=Case();hook=Shot(origin,8f);var minerObject=Make("Miner body",origin);var body=Art(minerObject);
        var miner=minerObject.AddComponent<MinerAI>();miner.hook=hook;miner.hookOrigin=minerObject.transform;
        hook.transform.SetParent(minerObject.transform,true);Freeze(body);
        Check(miner.IsFrozen&&hook.ControllerFrozen,"Miner body ice stops its controlled hook");
        Vector3 before=hook.transform.position;Call(hook,"Update");
        Check(hook.transform.position==before,"Frozen miner holds an outgoing rope still");
        hook.SendHome();Call(hook,"Update");Check(hook.transform.position==before,"Frozen miner also stops retraction");
        var wait=(IEnumerator)Call(miner,"Wait",.001f);Check(wait.MoveNext()&&wait.MoveNext(),"Frozen miner windup/cooldown clocks wait");
        hook.StopShot();hook.Fire(origin,Vector2.right,12f);Check(!hook.IsBusy,"Frozen miners cannot fire new shots");
        FrostField2D.BreakIce(body);Check(!miner.IsFrozen,"Breaking miner ice restores its work");
        Check(!wait.MoveNext(),"Miner timers continue after thawing");
        hook.Fire(origin,Vector2.right,12f);Check(hook.IsBusy,"Thawed miner can fire again");
        hook.StopShot();Freeze(body);
        var scripted=miner.FireScripted(origin+Vector2.right*5f,false);
        Check(scripted.MoveNext()&&!hook.IsBusy,"Frozen miners hold scripted shots too");
        FrostField2D.BreakIce(body);
        Check(scripted.MoveNext()&&hook.IsBusy,"Scripted firing continues after thaw");
        hook.StopShot();Check(!scripted.MoveNext()&&miner.autoShoot,"Scripted shot restores automatic firing");
        var telegraphObject=Make("Scripted windup",origin);telegraphObject.SetActive(false);
        var telegraph=telegraphObject.AddComponent<TelegraphLine>();telegraph.maxDots=1;
        var dot=Make("Scripted dot template",origin);Art(dot);telegraph.dotPrefab=dot;telegraphObject.SetActive(true);
        miner.telegraph=telegraph;miner.telegraphMin=Mathf.Max(.05f,Time.deltaTime*3f);
        scripted=miner.FireScripted(origin+Vector2.right*5f,false);
        Check(scripted.MoveNext()&&!hook.IsBusy,"Scripted windup starts before firing");
        Freeze(body);int windupTicks=Mathf.CeilToInt(miner.telegraphMin/Time.deltaTime)+4;
        for(int i=0;i<windupTicks;i++)scripted.MoveNext();
        Check(!hook.IsBusy,"Frozen time cannot finish a scripted telegraph");
        FrostField2D.BreakIce(body);
        for(int i=0;i<windupTicks&&!hook.IsBusy;i++)scripted.MoveNext();
        Check(hook.IsBusy,"Scripted windup resumes with its remaining time");
        hook.StopShot();scripted.MoveNext();

        origin=Case();hook=Shot(origin,10f);var rope=Part(hook,"Rope");object ropeStatus=Freeze(rope,.4f,.6f);
        float clearPrefix=FrostField2D.LimitReelStep(rope,10f);
        Check(clearPrefix>3.8f&&clearPrefix<4.2f,"Only the clear rope prefix may be reclaimed");
        hook.SendHome();Call(hook,"Retract",true);SyncRope(hook,ropeStatus);
        Check(Vector2.Distance(hook.transform.position,origin)>5.7f,"Reeling stops before reclaiming the icy middle");
        Check(FrostField2D.LimitReelStep(rope,10f)<.05f,"The same icy material reaches the winch instead of stretching away");
        before=hook.transform.position;Call(hook,"Retract",true);
        Check(Vector3.Distance(before,hook.transform.position)<.05f,"Icy rope stalls at the winch");
        FrostField2D.BreakIce(rope);Call(hook,"Retract",true);Check(!hook.IsBusy,"Reeling resumes when rope ice is gone");

        origin=Case();hook=Shot(origin,8f);var hole=Make("Survival controller",origin);body=Art(hole);
        var survival=hole.AddComponent<SurvivalHookAI>();survival.hook=hook;hook.transform.SetParent(hole.transform,true);
        Freeze(Part(hook,"Rope"),.3f,.5f);
        Check(!survival.IsFrozen&&!hook.ControllerFrozen,"Rope ice does not freeze its survival controller");
        Freeze(body);Check(survival.IsFrozen&&hook.ControllerFrozen,"Frozen survival controller stops its hook too");
        before=hook.transform.position;Call(hook,"Update");Check(hook.transform.position==before,"Survival rope stays still during controller frost");
        FrostField2D.BreakIce(body);

        var elsa=Make("Elsa frost exceptions",Case()).AddComponent<PenguinController>();
        treasure=Gold((Vector2)elsa.transform.position+Vector2.right);Freeze(treasure.GetComponent<SpriteRenderer>());
        Check(elsa.CanEatFrozenTreasure&&treasure.TryEat(elsa)&&treasure.IsTaken,"Elsa eats frozen treasure normally");
        origin=(Vector2)elsa.transform.position+Vector2.up*3f;hook=Shot(origin,8f);
        Freeze(Part(hook,"Rope"),.35f,.65f);
        elsa.transform.position=origin+Vector2.right*4f;elsa.gameObject.AddComponent<BoxCollider2D>();
        Physics2D.SyncTransforms();Check(elsa.TryCutRopes()&&hook.Defended,"Elsa can cut an icy rope through the normal J action");
        Debug.Log("FROST_GAMEPLAY_PASS: "+checks+" checks: miner/controller pause, icy rope reeling, empty ice-breaking impacts, protected treasure/blocks, and Elsa collection/cutting.");
    }
}
