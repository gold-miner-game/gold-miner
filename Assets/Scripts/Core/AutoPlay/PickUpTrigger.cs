using UnityEngine;

// The automatic pick-up flow. CatchableEntity requires this component, so Unity
// adds it to every gold, diamond and pick-up together with its script.
// When the pig walks into the trigger collider, it raises the catch for the player.
[DisallowMultipleComponent]
public class PickUpTrigger : MonoBehaviour
{
    private CatchableEntity entity;

    void Awake()
    {
        entity = GetComponent<CatchableEntity>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (entity == null) return;
        if (other.GetComponent<PigController>() == null) return;

        CatchCommand.Execute(entity, Catcher.Player);
    }
}
