using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// The table of random values Tundu/Sky draws its clouds from (global <c>_TunduNoise</c>, 256x256, made once): one
    /// texture sample per noise octave instead of four sin() hashes. Call <see cref="Ensure"/> before showing clouds.
    /// </summary>
    public static class SkyNoise
    {
        const int Size = 256;
        static Texture2D _texture;

        public static void Ensure()
        {
            if (_texture != null) return;
            _texture = new Texture2D(Size, Size, TextureFormat.R8, false, true)
            {
                name = "Tundu Sky Noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var values = new byte[Size * Size];
            new System.Random(1234).NextBytes(values);
            _texture.SetPixelData(values, 0);
            _texture.Apply(false, true);
            Shader.SetGlobalTexture("_TunduNoise", _texture);
        }
    }
}
