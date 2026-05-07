using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ilumisoft.Rendering
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Blur")]
    [VolumeRequiresRendererFeatures(typeof(BlurRendererFeature))]
    public class BlurVolumeComponent : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        protected override void OnEnable()
        {
            base.OnEnable();

            displayName = "Blur";
        }
    }
}