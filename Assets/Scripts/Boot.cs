using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Sits in the Boot scene. Creates the persistent GameFlow (and the live player record through it),
// then opens the map. Boot is the scene the game launches into.
public class Boot : MonoBehaviour
{
    void Start()
    {
        GameFlow.Instance.OpenMap();
    }
}
