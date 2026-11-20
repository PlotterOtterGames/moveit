using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// The camera picture made into a flattering mirror (drawn by Tundu/Mirror). The box sees evening living rooms: the
    /// picture was dark, grey and noisy, and the clothes looked like stickers on it. Each new camera frame is denoised
    /// against the last result (still parts settle over a few frames, moving parts take the new frame at once), a
    /// blurred copy makes the room behind the player soft, and the body mask (drawn from the skeleton for the clothes'
    /// layering) is widened into where the player is, so the mirror can keep them sharp and dim the room round them.
    /// Quads drawn into small render textures by a command buffer, like the body mask (Hidden/Tundu/MirrorPrep).
    /// </summary>
    public sealed class MirrorLook : System.IDisposable
    {
        const int PassDenoise = 0, PassDown = 1, PassBlur = 2, PassPerson = 3;
        readonly Material _prep;
        readonly Mesh _quad;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "Vishu mirror" };
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        RenderTexture _a, _b, _down, _blur, _person;
        bool _aIsNewest;
        long _frame = long.MinValue;
        static readonly int MainId = Shader.PropertyToID("_MainTex"), PrevId = Shader.PropertyToID("_PrevTex"),
                            RegionId = Shader.PropertyToID("_Region"), PictureStId = Shader.PropertyToID("_PictureST");
        static readonly Vector4 Whole = new Vector4(0f, 0f, 1f, 1f);

        public MirrorLook(Material prep)
        {
            _prep = new Material(prep);
            _quad = new Mesh { name = "Mirror quad" };
            _quad.SetVertices(new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1), new Vector3(0, 1) });
            _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        }

        /// <summary>The denoised picture (null until the first frame).</summary>
        public Texture Picture => _frame == long.MinValue ? null : (_aIsNewest ? _a : _b);
        /// <summary>The room blurred (rgb) and where the player is (a), in the screen's space (Tundu/Mirror's _PersonMap).</summary>
        public Texture Person => _person;

        public void Dispose()
        {
            foreach (var rt in new[] { _a, _b, _down, _blur, _person })
                if (rt != null) rt.Release();
            Object.Destroy(_prep);
            Object.Destroy(_quad);
            _cmd.Release();
        }

        static RenderTexture Make(RenderTexture rt, int w, int h, string name)
        {
            if (rt != null && rt.width == w && rt.height == h) return rt;
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        void Draw(RenderTexture target, int pass, Texture main, Texture prev = null) => Draw(target, pass, main, prev, Whole);

        void Draw(RenderTexture target, int pass, Texture main, Texture prev, Vector4 region)
        {
            _props.Clear();
            if (main != null) _props.SetTexture(MainId, main);
            if (prev != null) _props.SetTexture(PrevId, prev);
            _props.SetVector(RegionId, region);
            _cmd.SetRenderTarget(target);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            _cmd.DrawMesh(_quad, Matrix4x4.identity, _prep, 0, pass, _props);
        }

        /// <summary>
        /// A camera frame is on the screen (its capture time tells a new one): denoise it, blur the room. Only the part of
        /// the picture the mirror shows (<paramref name="shown"/>, texture coordinates; a little more for the filtering at
        /// its edges) is worked on: the mirror is framed on the player, about half of the camera's picture, and the
        /// denoise at the camera's full size was the biggest of these passes on the box. The rest keeps older frames, which
        /// the denoise tells from the new frame as movement if the framing glides over there.
        /// </summary>
        public void NewFrame(Texture picture, long captureTime, Rect shown)
        {
            if (picture == null || captureTime == _frame) return;
            int w = picture.width, h = picture.height;
            bool first = _frame == long.MinValue || _a == null || _a.width != w || _a.height != h;
            _a = Make(_a, w, h, "Mirror A");
            _b = Make(_b, w, h, "Mirror B");
            _down = Make(_down, Mathf.Max(8, w / 4), Mathf.Max(8, h / 4), "Mirror down");
            _blur = Make(_blur, Mathf.Max(8, w / 8), Mathf.Max(8, h / 8), "Mirror blur");
            var dst = _aIsNewest ? _b : _a;
            var prev = _aIsNewest ? _a : _b;
            _cmd.Clear();
            const float Margin = 0.03f;
            float x0 = Mathf.Clamp01(shown.xMin - Margin), y0 = Mathf.Clamp01(shown.yMin - Margin);
            float x1 = Mathf.Clamp01(shown.xMax + Margin), y1 = Mathf.Clamp01(shown.yMax + Margin);
            var region = first || x1 <= x0 || y1 <= y0 ? Whole : new Vector4(x0, y0, x1 - x0, y1 - y0);
            // the first frame (or a new size) has nothing to average with: the frame itself is the previous one
            Draw(dst, PassDenoise, picture, first ? picture : prev, region);
            Draw(_down, PassDown, dst, null, region);
            Draw(_blur, PassBlur, _down);
            Graphics.ExecuteCommandBuffer(_cmd);
            _aIsNewest = dst == _a;
            _frame = captureTime;
        }

        /// <summary>
        /// Where the player is, widened and softened from this frame's body mask (after the outfit is built), and the room's
        /// blurred colour on the screen beside it: <paramref name="pictureST"/> is the mirror's own mapping from the screen
        /// (0..1) to the picture's texture coordinates (scale xy, offset zw).
        /// </summary>
        public void UpdatePerson(float screenWidth, Vector4 pictureST)
        {
            if (_blur == null) return;
            int w = Mathf.Max(16, Mathf.RoundToInt(screenWidth / 8f)), h = 1080 / 8;
            _person = Make(_person, w, h, "Mirror person");
            _cmd.Clear();
            _props.Clear();
            _props.SetTexture(MainId, _blur);
            _props.SetVector(RegionId, Whole);
            _props.SetVector(PictureStId, pictureST);
            _cmd.SetRenderTarget(_person);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            _cmd.DrawMesh(_quad, Matrix4x4.identity, _prep, 0, PassPerson, _props);
            Graphics.ExecuteCommandBuffer(_cmd);
        }
    }
}
