using UnityEditor;
using UnityEngine;

// Art: import settings for everything under Assets/Resources/Art, so new painted sprites need no
// manual setup. 256 px = 1 unit, like the old primitive shapes, so a swapped sprite keeps its size.
public class ArtImportRules : AssetPostprocessor
{
    const string Root = "Assets/Resources/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spritePixelsPerUnit = 256;
        settings.spriteMeshType = SpriteMeshType.Tight;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.wrapMode = TextureWrapMode.Clamp;

        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        if (file == "MineFloor" || assetPath.Contains("/Tiles/"))
        {
            // Tiled backdrop: one 1024 px tile spans 8 units of floor.
            settings.spritePixelsPerUnit = 128;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.wrapMode = TextureWrapMode.Repeat;
        }
        else if (assetPath.Contains("/UI/"))
        {
            // 9-slice cards for uGUI: 32 px corners (rounded edge + stitching) stay unstretched.
            settings.spritePixelsPerUnit = 100;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteBorder = new Vector4(32, 32, 32, 32);
        }
        else if (assetPath.Contains("/Objects/") && (file.StartsWith("Gold") || file.StartsWith("Diamond")))
        {
            // Treasure: the sprite's bounds are read by gameplay (Hook.HoldInClaw places a carried
            // treasure by them), so they must equal the old look's exactly: full rect, canvas = the old
            // footprint. Gold: the 1-unit circle (256 px). Diamond: the old gem as drawn, 0.9333 x 0.8 units
            // (its tight mesh hugged the 112 x 96 px gem at 120 px per unit; here 224 x 192 px at 240).
            settings.spriteMeshType = SpriteMeshType.FullRect;
            if (file.StartsWith("Diamond")) settings.spritePixelsPerUnit = 240;
        }
        else if (assetPath.Contains("/Slices/"))
        {
            // 9-slice strips for small world-space pieces (SpriteRenderer drawMode Sliced): 1024 px = 1
            // unit, so a 46 px rounded corner is about 0.045 units and fits a bar a fifth of a unit tall.
            settings.spritePixelsPerUnit = 1024;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteBorder = new Vector4(46, 46, 46, 46);
        }
        importer.SetTextureSettings(settings);
    }
}
