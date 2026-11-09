using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Sits in the Boot scene. Creates the persistent GameFlow (and the live player record through it),
// then opens the main menu. Boot is the scene the game launches into (first in Build Settings).
public class Boot : MonoBehaviour
{
    void Start()
    {
        GameFlow.Instance.OpenMenu();
    }
}
