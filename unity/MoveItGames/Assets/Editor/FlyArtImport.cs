using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Fluturo!'s city textures (<c>Assets/Games/Fly/Art/fly_city.png</c>, made by <c>tools/fly/city_textures.py</c>):
    /// the 4x4 tiles become one 16-slice 2D texture array, so every tile repeats on its own and one material (Tundu/City)
    /// draws a whole city block. Mipmaps and 2x anisotropy keep the roads and facades clean at a glancing angle.
    /// </summary>
    sealed class FlyArtImport : AssetPostprocessor
    {
        public const string CityTexture = "Assets/Games/Fly/Art/fly_city.png";

        void OnPreprocessTexture()
        {
            if (assetPath != CityTexture) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;   // alpha is the material (wall / fixed / glass), not coverage
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureShape = TextureImporterShape.Texture2DArray;
            settings.flipbookColumns = 4;
            settings.flipbookRows = 4;
            importer.SetTextureSettings(settings);
        }
    }
}
