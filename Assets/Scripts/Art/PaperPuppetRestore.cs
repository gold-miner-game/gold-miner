using UnityEngine;

// Runs before every other script each frame: puts back what PaperPuppet posed for the last picture.
[DefaultExecutionOrder(-32000)]
public class PaperPuppetRestore : MonoBehaviour
{
    internal PaperPuppet puppet;
    void FixedUpdate() { if (puppet != null) puppet.Restore(); }   // physics steps come first in a frame
    void Update() { if (puppet != null) puppet.Restore(); }
}
