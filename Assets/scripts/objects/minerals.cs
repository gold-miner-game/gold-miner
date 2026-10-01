using UnityEngine;

[CreateAssetMenu(fileName = "NewResourceType", menuName = "Miner Game/Resource Type")]
public class ResourceType : ScriptableObject
{
    public string materialName;
    
    [Header("Base Stats (Per Unit Volume)")]
    public float valuePerVolume;
    public float densityPerVolume; // Acts as weight

    [Header("Visuals")]
    public Color baseColor = Color.white;
    // This holds your diffuse/reflective material settings
}