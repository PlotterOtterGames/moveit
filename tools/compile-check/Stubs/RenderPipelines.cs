// Stand-in for the parts of the Universal Render Pipeline (com.unity.render-pipelines.universal, and the volumes of its
// core package) the game code uses, so it compiles outside Unity (see check.py). Signatures only.
using UnityEngine.Rendering;

namespace UnityEngine.Rendering
{
    public class VolumeComponent : ScriptableObject
    {
        public bool active = true;
    }

    public class VolumeProfile : ScriptableObject
    {
        public bool TryGet<T>(out T component) where T : VolumeComponent
        {
            component = null;
            return false;
        }
    }

    public class Volume : MonoBehaviour
    {
        public VolumeProfile sharedProfile;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public bool supportsHDR { get; set; }
        public float renderScale { get; set; }
        public int mainLightShadowmapResolution { get; set; }
        public float shadowDistance { get; set; }
        public override RenderPipeline CreatePipeline() => null;   // public in the 2021.3 reference assemblies
    }

    public class UniversalAdditionalCameraData : MonoBehaviour
    {
        public bool renderShadows { get; set; }
        public bool renderPostProcessing { get; set; }
    }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera) => null;
    }

    public sealed class Bloom : VolumeComponent { }
    public sealed class Vignette : VolumeComponent { }
    public sealed class ColorAdjustments : VolumeComponent { }
}
