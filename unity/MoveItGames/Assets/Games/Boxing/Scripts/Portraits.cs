using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// The boxers' faces for the HUD (2026-11-19 overhaul: Wii Sports Boxing shows each boxer's face by their stamina):
    /// each boxer drawn once into a small texture by a camera of its own, looking at its head from the front, nothing but
    /// that boxer in it (a layer of its own while it's drawn), the background clear. Drawn again only when told
    /// (<see cref="Forget"/>: a new opponent, another outfit or gloves). The HUD shows them round (Tundu/Portrait).
    /// </summary>
    public sealed class Portraits : System.IDisposable
    {
        const int Size = 256, Layer = 31;
        readonly RenderTexture[] _rt = new RenderTexture[2];
        readonly bool[] _done = new bool[2];
        Camera _cam;

        /// <summary>The portrait of boxer <paramref name="index"/> (0 the hero, 1 the opponent), null until drawn.</summary>
        public Texture this[int index] => _done[index] ? _rt[index] : null;

        /// <summary>Boxer <paramref name="index"/> looks different now: draw it again.</summary>
        public void Forget(int index) => _done[index] = false;

        /// <summary>Draws boxer <paramref name="index"/>'s portrait if it isn't drawn yet.</summary>
        public void Ensure(int index, BoxerView boxer, Camera main)
        {
            // not before its first pose: a boxer made this frame stands in the model's import pose, its head near the floor
            // (the camera looked at the canvas: the opponents' portraits came out empty)
            if (_done[index] || boxer == null || boxer.Chibi == null || !boxer.Posed || !boxer.gameObject.activeInHierarchy) return;
            if (_cam == null)
            {
                var go = new GameObject("Portrait camera") { hideFlags = HideFlags.HideAndDontSave };
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _cam.cullingMask = 1 << Layer;
                _cam.fieldOfView = 26f;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 6f;
                _cam.allowHDR = false;
                _cam.allowMSAA = false;
            }
            if (_rt[index] == null)
            {
                _rt[index] = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = index == 0 ? "Hero portrait" : "Opponent portrait", antiAliasing = 2,
                };
                _rt[index].Create();
            }
            // the head from the front, a little from above, its shoulders in
            var head = boxer.HeadPosition;
            var forward = boxer.transform.forward;
            float scale = boxer.Size;
            _cam.transform.position = head + forward * 1.45f * scale + Vector3.up * 0.12f * scale;
            _cam.transform.LookAt(head + Vector3.down * 0.08f * scale);
            _cam.targetTexture = _rt[index];
            var renderers = boxer.GetComponentsInChildren<Renderer>();
            var layers = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                layers[i] = renderers[i].gameObject.layer;
                renderers[i].gameObject.layer = Layer;
            }
            // the hero is drawn see-through in the fight: the portrait shows it solid
            Material[] over = null;
            bool low = boxer.Chibi.Low;
            if (boxer.Chibi.OverrideMaterials != null)
            {
                over = boxer.Chibi.OverrideMaterials;
                boxer.Chibi.OverrideMaterials = null;
                boxer.Chibi.Low = false;
            }
            // a boxer made this frame has no colours yet (its own LateUpdate sets them, maybe after this one)
            boxer.Chibi.ApplyNow();
            _cam.Render();
            if (over != null)
            {
                boxer.Chibi.OverrideMaterials = over;
                boxer.Chibi.Low = low;
            }
            for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];
            _cam.targetTexture = null;
            _done[index] = true;
        }

        public void Dispose()
        {
            for (int i = 0; i < 2; i++)
                if (_rt[i] != null)
                {
                    _rt[i].Release();
                    Object.Destroy(_rt[i]);
                }
            if (_cam != null) Object.Destroy(_cam.gameObject);
        }
    }
}
