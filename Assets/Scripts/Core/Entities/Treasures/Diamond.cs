// Diamond prefab: small but worth the most
public class Diamond : Treasure
{
    // Defaults when the script is added in the Inspector
    void Reset()
    {
        type = TreasureType.Diamond;
        value = 600;
    }
}
