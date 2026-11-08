using UnityEditor;
using UnityEngine;

namespace Parry.Editor
{
    // These are RGB vector data, decoded explicitly by SpriteNormalLit (not packed Unity normal maps).
    public sealed class CombatNormalMapImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Combat/") || !assetPath.EndsWith("_normal.png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 64;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
