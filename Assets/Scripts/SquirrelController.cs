using UnityEngine;
using UnityEngine.InputSystem;

// The squirrel (Scrat). Moves, eats and blinks like every character (PlayerCharacter), at the level's
// speed. It never flips; SquirrelFaceHeading and SquirrelTailHeading turn the face and tail instead.
// Its skill is the tail: while Space is held the see-through tail is firm, collects treasure with the
// body's digest and stops hooks (SquirrelTailCollector), and the squirrel moves at half speed.
// Penny's speed-up / slide-to-stop movement is saved in E:\Create with code\Squirrel Record.
public class SquirrelController : PlayerCharacter
{
    [Header("Tail (hold Space)")]
    [Tooltip("Speed while the tail is firm, times the level's speed")]
    [Range(0f, 1f)] public float firmSpeedMultiplier = .5f;

    public override string SkillHint => tail != null ? "[hold Space] firm tail" : "";
    public bool TailFirm => tail != null && tail.IsCollecting;

    // After the tutorial his record decides (the store can change it); before, the Inspector value
    public float FirmSpeedMultiplier => Record is SquirrelStatus s ? s.getFirmSpeedMultiplier() : firmSpeedMultiplier;

    protected override bool FlipsWhenTurning => false;

    private SquirrelTailCollector tail;

    protected override void Awake()
    {
        base.Awake();
        tail = GetComponent<SquirrelTailCollector>();
    }

    // A fresh record (just picked) starts from this prefab's numbers
    void Start()
    {
        if (Record is SquirrelStatus s) s.seed(firmSpeedMultiplier, tail != null ? tail.hookStall : 1f);
    }

    protected override void Update()
    {
        base.Update();
        Keyboard keyboard = Keyboard.current;
        if (tail != null) tail.SetFirm(IsPlaying && keyboard != null && keyboard.spaceKey.isPressed);
    }

    // Half speed while the tail is firm
    protected override Vector2 Velocity(Vector2 input)
    {
        return base.Velocity(input) * (TailFirm ? FirmSpeedMultiplier : 1f);
    }
}
