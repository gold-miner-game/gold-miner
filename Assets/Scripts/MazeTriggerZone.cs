using System.Collections;
using UnityEngine;

// One trigger rectangle from level_tutorial.json. Handles hints, arming miners, the gate check,
// the steal shot, and the block / knock QTEs.
public class MazeTriggerZone : MonoBehaviour
{
    LevelData.MazeTrigger data;
    MazeRuntime maze;
    bool fired;
    bool qteSuccess;
    string watchingId;
    bool playerInside;
    Coroutine knockLoop;
    Coroutine blockLoop;

    public void Init(LevelData.MazeTrigger trigger, MazeRuntime runtime)
    {
        data = trigger;
        maze = runtime;
    }

    public void Rearm() { fired = false; watchingId = null; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerCharacter>() == null) return;
        playerInside = true;
        switch (data.type)
        {
            case "Hint":
                maze.ShowHint(data.text);
                break;
            case "MinerZone":
                maze.ArmMiners(data.targets, true);
                maze.ShowHint(data.text);
                break;
            case "Gate":
                // LevelGate on the X entity does the real credit check; this zone only hints.
                int money = GameManager.Instance != null ? GameManager.Instance.PlayerMoney : 0;
                if (money < maze.EntryCredit)
                    maze.ShowHint(data.text + " (need $" + maze.EntryCredit + ")");
                break;
            case "Steal":
                if (fired || MazeHubState.LessonCleared("steal")) return;
                fired = true;
                MazeHubState.MarkLessonCleared("steal");
                StartCoroutine(StealShot());
                break;
            case "KnockQTE":
                if (MazeHubState.LessonCleared("knock")) return;
                if (knockLoop == null) knockLoop = StartCoroutine(KnockLoop());
                break;
            case "BlockQTE":
                // Armed when the pig starts eating the diamond (see OnEatStart).
                break;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<PlayerCharacter>() == null) return;
        playerInside = false;
        if (data.type == "Hint") maze.ClearHint();
        if (data.type == "MinerZone") maze.ArmMiners(data.targets, false);
        // Do not stop KnockLoop on exit — pig may step aside while eating knocked gold.
        if (data.type == "KnockQTE") return;
    }

    public void OnEatStart(Treasure treasure)
    {
        if (treasure == null) return;

        // Knock lesson: gold knocked off M's hook and then eaten → cleared for good.
        if (data.type == "KnockQTE"
            && data.targets != null && data.targets.Length >= 2
            && treasure.name == data.targets[1])
        {
            MazeHubState.MarkLessonCleared("knock");
            if (knockLoop != null)
            {
                StopCoroutine(knockLoop);
                knockLoop = null;
            }
            if (maze != null) maze.ShowHint("Knock lesson cleared!");
            return;
        }

        if (data.type != "BlockQTE") return;
        if (MazeHubState.LessonCleared("block")) return;
        if (data.targets == null || data.targets.Length < 2) return;
        string diamondId = data.targets[1];
        if (treasure.name != diamondId) return;
        watchingId = diamondId;
        fired = false;
        if (blockLoop != null) StopCoroutine(blockLoop);
        blockLoop = StartCoroutine(BlockLoop(diamondId));
    }

    public void OnEatProgress(string eatenId, float progress01)
    {
        // Continuous BlockLoop owns the shots now; progress only used as arm threshold inside it.
    }

    IEnumerator StealShot()
    {
        if (data.targets == null || data.targets.Length < 2) yield break;
        MinerAI miner = maze.Miner(data.targets[0]);
        Treasure loot = maze.Treasure(data.targets[1]);
        if (miner == null || loot == null) yield break;
        maze.ShowHint(data.text);
        yield return miner.FireScripted(loot.transform.position, false);
    }

    // M corridor: M grabs G3. Block to drop it, then eat it → lesson cleared (no re-run).
    // If the miner keeps the gold → respawn and repeat.
    IEnumerator KnockLoop()
    {
        if (data.targets == null || data.targets.Length < 2) yield break;
        if (MazeHubState.LessonCleared("knock")) { knockLoop = null; yield break; }

        string goldId = data.targets[1];
        maze.ShowHint(data.text);
        float knockRange = data.param > 0.1f ? data.param : maze.BlockRange;

        while (!MazeHubState.LessonCleared("knock")
               && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
        {
            if (!playerInside)
            {
                yield return null;
                continue;
            }

            MinerAI miner = maze.Miner(data.targets[0]);
            PlayerCharacter pig = FindFirstObjectByType<PlayerCharacter>();
            if (miner == null || pig == null) yield break;

            Treasure loot = maze.Treasure(goldId);
            if (loot == null || !loot.gameObject.activeInHierarchy)
            {
                maze.RespawnTreasure(goldId);
                loot = maze.Treasure(goldId);
                if (loot == null) { yield return null; continue; }
            }

            Coroutine shot = StartCoroutine(miner.FireScripted(loot.transform.position, false));

            while (miner.hook != null && miner.hook.IsBusy)
            {
                Treasure carried = miner.hook.Carried;
                if (carried != null)
                {
                    float dist = Vector2.Distance(miner.hook.transform.position, pig.transform.position);
                    bool pressSpace = Input.GetKeyDown(KeyCode.Space)
                        || (UnityEngine.InputSystem.Keyboard.current != null
                            && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame);
                    if (dist <= knockRange && pressSpace && pig is MrHydeController hyde)
                        hyde.TryBlock();

                    // Reliable drop: if already blocking and close, force the hook to release.
                    if (dist <= knockRange && pig.IsBlocking)
                        miner.hook.Release(carried);
                }
                yield return null;
            }
            if (shot != null) yield return shot;

            if (MazeHubState.LessonCleared("knock")) break;

            // After the shot: gold still in the world = knocked loose (or never taken) → wait for eat.
            Treasure after = maze.Treasure(goldId);
            bool onGround = after != null && after.gameObject.activeInHierarchy && !after.IsTaken;
            if (onGround)
            {
                maze.ShowHint("Eat the gold!");
                while (!MazeHubState.LessonCleared("knock")
                       && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
                {
                    Treasure t = maze.Treasure(goldId);
                    // Eaten → OnEatStart marks knock cleared. Stolen somehow → leave wait.
                    if (t == null || !t.gameObject.activeInHierarchy)
                    {
                        if (!MazeHubState.LessonCleared("knock"))
                        {
                            // Gone without eat mark — treat as stolen, retry.
                            break;
                        }
                        knockLoop = null;
                        yield break;
                    }
                    yield return null;
                }
                if (MazeHubState.LessonCleared("knock"))
                {
                    knockLoop = null;
                    yield break;
                }
            }

            // Miner kept it (or gold vanished without clear) → respawn and retry.
            maze.ShowHint("Press Space to knock it off, then eat it!");
            maze.RespawnTreasure(goldId);
            float gapUntil = Time.unscaledTime + 0.55f;
            while (Time.unscaledTime < gapUntil) yield return null;
        }

        knockLoop = null;
    }

    // E: one teaching shot while digesting D2. Block once, then E stays quiet so the pig can leave.
    // (Continuous volleys + block cooldown made it impossible to walk upward without dying.)
    IEnumerator BlockLoop(string diamondId)
    {
        if (data.targets == null || data.targets.Length < 1) yield break;
        MinerAI miner = maze.Miner(data.targets[0]);
        PlayerCharacter pig = FindFirstObjectByType<PlayerCharacter>();
        if (miner == null || pig == null) yield break;

        float armAt = data.param;
        while (!string.IsNullOrEmpty(watchingId) && watchingId == diamondId)
        {
            if (pig.DigestProgress01 >= armAt) break;
            if (!pig.IsDigesting) { blockLoop = null; yield break; }
            yield return null;
        }
        if (string.IsNullOrEmpty(watchingId) || !pig.IsDigesting)
        {
            blockLoop = null;
            yield break;
        }

        maze.ShowHint(data.text);
        yield return SpaceQte(data.text);

        int livesBefore = GameManager.Instance != null ? GameManager.Instance.Lives : 0;
        Vector2 aim = pig.transform.position;
        yield return miner.FireScripted(aim, true);

        // Must actually block the hook — Space in the QTE window alone is not enough.
        bool blocked = pig.IsBlocking || qteSuccess;
        bool hit = GameManager.Instance != null && GameManager.Instance.Lives < livesBefore;

        watchingId = null;
        if (blocked && !hit)
        {
            // Lesson done — do not re-arm E; pig can walk up safely.
            fired = true;
            MazeHubState.MarkLessonCleared("block");
            maze.ShowHint("Blocked! Keep going");
        }
        else
        {
            // Missed block: refund diamond, clear eaten mark, respawn, and re-run the lesson.
            if (pig.IsDigesting) pig.CancelDigest();
            if (GameManager.Instance != null) GameManager.Instance.AddPlayerMoney(-600);
            MazeHubState.UnmarkTreasureEaten(diamondId);
            maze.RespawnTreasure(diamondId);
            maze.ShowHint("Block with Space to keep the diamond!");
            Rearm();
        }

        blockLoop = null;
    }

    IEnumerator SpaceQte(string text)
    {
        qteSuccess = false;
        float prev = Time.timeScale;
        Time.timeScale = 0f;
        maze.ShowHint(text);
        float timeout = Time.unscaledTime + 1.2f;
        while (Time.unscaledTime < timeout)
        {
            if (Input.GetKeyDown(KeyCode.Space) ||
                (UnityEngine.InputSystem.Keyboard.current != null
                 && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                qteSuccess = true;
                PlayerCharacter pig = FindFirstObjectByType<PlayerCharacter>();
                if (pig is MrHydeController hyde) hyde.TryBlock();
                break;
            }
            yield return null;
        }
        Time.timeScale = prev > 0f ? prev : 1f;
    }
}
