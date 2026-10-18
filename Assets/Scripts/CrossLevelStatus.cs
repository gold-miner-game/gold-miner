using System;
using System.Collections.Generic;
using UnityEngine;

// Parent record of one player across levels. This object survives scene loads.
// It holds default values and the methods that change them. Pig, frog, cat, and squirrel
// each extend this class and override what they need. Call base when you still want the default.
//
//     public class FrogStatus : CrossLevelStatus
//     {
//         [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
//         static void Register() { Prefer<FrogStatus>(); }
//
//         protected override void onReady()
//         {
//             base.onReady();
//             setSpeed(4.5f);
//             addSkill("tongue").power = 4f;
//             addSkill("leap");
//         }
//
//         public override bool useSkill(string id)
//         {
//             if (id != "tongue") return base.useSkill(id);
//             PlayerSkill tongue = getSkill("tongue");
//             if (tongue == null || tongue.stock <= 0) return false;
//             tongue.stock--;
//             tongue.power += 1f;
//             return true;
//         }
//     }
//
// Only one subclass is the live player. Prefer picks it. If several call Prefer, the last one is used.
// If nobody calls Prefer, the player is PigStatus.
public class CrossLevelStatus : MonoBehaviour
{
    public const float SwiftBoost = 2f;
    public const float ShieldDuration = 60f;

    public static CrossLevelStatus Active { get; private set; }

    static readonly List<Type> preferred = new List<Type>();

    string skinId = "default";
    int wallet;
    float moveSpeed = 5f;
    float speedMultiplier = 1f;
    float multiplierBeforeSwift = 1f;
    bool swiftBoostOn;
    float digestTime;
    float digestPerValue;
    bool canBlock;
    float blockDuration = 0.8f;
    float blockCooldown = 1f;
    int lives = 3;
    int extraLives;
    float invincibleTime = 1.5f;
    readonly List<PlayerSkill> skills = new List<PlayerSkill>();

    int bankedLevel = -1;

    public static void Prefer<T>() where T : CrossLevelStatus
    {
        Type type = typeof(T);
        if (!preferred.Contains(type)) preferred.Add(type);
    }

    public static CrossLevelStatus Ensure()
    {
        if (Active != null) return Active;

        CrossLevelStatus found = FindFirstObjectByType<CrossLevelStatus>();
        if (found != null) return found;

        return Create(ChooseType());
    }

    public static T Install<T>() where T : CrossLevelStatus
    {
        Prefer<T>();
        if (Active is T ready) return ready;

        if (Active != null)
        {
            GameObject host = Active.gameObject;
            CrossLevelStatus previous = Active;
            Active = null;
            T created = host.AddComponent<T>();
            created.copyFrom(previous);
            Destroy(previous);
            return created;
        }

        var go = new GameObject(typeof(T).Name);
        DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }

    // A new run: throw the current record away (if any) and start a fresh one of this type.
    // The select screen calls it when a champion is picked, after the tutorial, so nothing carries
    // over from an earlier run. Unlike Install, nothing is copied.
    public static CrossLevelStatus StartFresh(Type type)
    {
        if (type == null || !typeof(CrossLevelStatus).IsAssignableFrom(type)) type = typeof(PigStatus);
        if (Active != null)
        {
            CrossLevelStatus old = Active;
            Active = null;
            Destroy(old.gameObject);
        }
        preferred.Clear();
        preferred.Add(type);
        return Create(type);
    }

    static Type ChooseType()
    {
        Type best = typeof(PigStatus);
        for (int i = 0; i < preferred.Count; i++)
        {
            Type type = preferred[i];
            if (type == null || !typeof(CrossLevelStatus).IsAssignableFrom(type)) continue;
            if (best.IsAssignableFrom(type) || !type.IsAssignableFrom(best)) best = type;
        }
        return best;
    }

    static CrossLevelStatus Create(Type type)
    {
        var go = new GameObject(type.Name);
        DontDestroyOnLoad(go);
        return (CrossLevelStatus)go.AddComponent(type);
    }

    void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        DontDestroyOnLoad(gameObject);
        onReady();
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    void Update()
    {
        tickSkills(Time.deltaTime);
        onTick(Time.deltaTime);
    }

    public virtual string getSkinId() { return skinId; }
    public virtual void setSkinId(string value) { skinId = value; }

    public virtual int getWallet() { return wallet; }
    public virtual void setWallet(int value) { wallet = value; }
    public virtual void addWallet(int amount) { wallet += amount; }

    public virtual float getSpeed() { return moveSpeed; }
    public virtual void setSpeed(float value) { moveSpeed = value; }

    public virtual float getSpeedMultiplier() { return speedMultiplier; }

    // Sets the multiplier kept on the player. Swift water multiplies this for the rest of the level,
    // then puts this value back when the next level starts.
    public virtual void setSpeedMultiplier(float value)
    {
        speedMultiplier = value;
        multiplierBeforeSwift = value;
        swiftBoostOn = false;
    }

    public virtual float getDigestTime() { return digestTime; }
    public virtual void setDigestTime(float value) { digestTime = value; }

    public virtual float getDigestPerValue() { return digestPerValue; }
    public virtual void setDigestPerValue(float value) { digestPerValue = value; }

    public virtual bool hasNoDigest()
    {
        PlayerSkill ration = getSkill("ration");
        return ration != null && ration.active;
    }

    public virtual bool getCanBlock() { return canBlock; }
    public virtual void setCanBlock(bool value) { canBlock = value; }

    public virtual float getBlockDuration() { return blockDuration; }
    public virtual void setBlockDuration(float value) { blockDuration = value; }

    public virtual float getBlockCooldown() { return blockCooldown; }
    public virtual void setBlockCooldown(float value) { blockCooldown = value; }

    public virtual int getLives() { return lives; }
    public virtual void setLives(int value) { lives = value; }

    public virtual int getExtraLives() { return extraLives; }
    public virtual void setExtraLives(int value) { extraLives = value; }

    public virtual float getInvincibleTime() { return invincibleTime; }
    public virtual void setInvincibleTime(float value) { invincibleTime = value; }

    public virtual bool isShielded()
    {
        PlayerSkill shield = getSkill("shield");
        return shield != null && shield.secondsLeft > 0f;
    }

    public virtual IReadOnlyList<PlayerSkill> getSkills() { return skills; }

    // The skill with this id, or null if addSkill was never called for it.
    public virtual PlayerSkill getSkill(string id)
    {
        for (int i = 0; i < skills.Count; i++)
            if (skills[i].id == id) return skills[i];
        return null;
    }

    // Adds the skill if this id is new, then returns it so the caller can set stock or power.
    public virtual PlayerSkill addSkill(string id)
    {
        PlayerSkill found = getSkill(id);
        if (found != null) return found;

        var skill = new PlayerSkill { id = id };
        skills.Add(skill);
        return skill;
    }

    public virtual void addSkill(PlayerSkill skill)
    {
        if (skill == null || string.IsNullOrEmpty(skill.id)) return;
        if (getSkill(skill.id) != null) return;
        skills.Add(skill);
    }

    // Called once when this record is created. Add this character's own skills here.
    protected virtual void onReady()
    {
        addSkill("swift");
        addSkill("ration");
        addSkill("shield").power = ShieldDuration;
    }

    // Called every frame after skill timers. deltaTime is 0 while the game is paused.
    protected virtual void onTick(float deltaTime) { }

    public virtual void onLevelStarted()
    {
        bankedLevel = -1;
        endSwiftBoost();
        PlayerSkill swift = getSkill("swift");
        if (swift != null) swift.active = false;

        for (int i = 0; i < skills.Count; i++)
        {
            PlayerSkill skill = skills[i];
            if (!skill.armed) continue;
            skill.armed = false;
            if (skill.id == "ration" || skill.id == "swift")
            {
                skill.active = true;
                if (skill.id == "swift") beginSwiftBoost();
                continue;
            }
            skill.secondsLeft = armedDuration(skill);
            skill.active = skill.secondsLeft > 0f;
        }
    }

    protected virtual float armedDuration(PlayerSkill skill)
    {
        if (skill.id == "shield") return ShieldDuration;
        if (skill.power > 0f) return skill.power;
        return 0f;
    }

    // Stores winnings once per level.
    public virtual void bank(int levelNumber, int amount)
    {
        if (bankedLevel == levelNumber) return;
        bankedLevel = levelNumber;
        addWallet(Mathf.Max(0, amount));
    }

    // Uses one stock of a skill. Swift, ration, and shield have a default.
    // Return true when the skill was used. A subclass handles its own ids, then calls base.
    public virtual bool useSkill(string id)
    {
        PlayerSkill skill = getSkill(id);
        if (skill == null || skill.stock <= 0) return false;

        if (id == "ration" || id == "swift")
        {
            if (roundIsOver())
            {
                if (skill.armed) return false;
                skill.stock--;
                skill.unlocked = true;
                skill.armed = true;
                return true;
            }
            if (skill.active) return false;
            skill.stock--;
            skill.unlocked = true;
            skill.active = true;
            if (id == "swift") beginSwiftBoost();
            return true;
        }

        if (id == "shield")
        {
            skill.stock--;
            skill.unlocked = true;
            float duration = armedDuration(skill);
            skill.power = duration;
            if (roundIsOver()) skill.armed = true;
            else
            {
                skill.secondsLeft = duration;
                skill.active = true;
            }
            return true;
        }

        return false;
    }

    protected virtual void copyFrom(CrossLevelStatus other)
    {
        if (other == null) return;
        skinId = other.skinId;
        wallet = other.wallet;
        moveSpeed = other.moveSpeed;
        speedMultiplier = other.speedMultiplier;
        multiplierBeforeSwift = other.multiplierBeforeSwift;
        swiftBoostOn = other.swiftBoostOn;
        digestTime = other.digestTime;
        digestPerValue = other.digestPerValue;
        canBlock = other.canBlock;
        blockDuration = other.blockDuration;
        blockCooldown = other.blockCooldown;
        lives = other.lives;
        extraLives = other.extraLives;
        invincibleTime = other.invincibleTime;
        skills.Clear();
        skills.AddRange(other.skills);
        bankedLevel = other.bankedLevel;
    }

    void beginSwiftBoost()
    {
        if (swiftBoostOn) return;
        multiplierBeforeSwift = speedMultiplier;
        speedMultiplier *= SwiftBoost;
        swiftBoostOn = true;
    }

    void endSwiftBoost()
    {
        if (!swiftBoostOn) return;
        speedMultiplier = multiplierBeforeSwift;
        swiftBoostOn = false;
    }

    void tickSkills(float deltaTime)
    {
        for (int i = 0; i < skills.Count; i++)
        {
            if (skills[i].secondsLeft > 0f)
                skills[i].secondsLeft = Mathf.Max(0f, skills[i].secondsLeft - deltaTime);
        }
    }

    static bool roundIsOver()
    {
        return GameManager.Instance != null && !GameManager.Instance.IsPlaying;
    }
}

// One skill kept on the player. stock is unused count.
// active / secondsLeft is the effect in use. armed waits until the next level.
// power is whatever that skill needs to remember, such as a tongue length or a duration.
[Serializable]
public class PlayerSkill
{
    public string id;
    public int stock;
    public bool unlocked;
    public bool active;
    public bool armed;
    public float secondsLeft;
    public float power = 1f;
}
