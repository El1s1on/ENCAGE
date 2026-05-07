using UnityEditor;

namespace Ilumisoft.Rendering
{
    [CustomEditor(typeof(BlurRendererFeature))]
    public class BlurRendererFeatureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This effect is controlled via Post-processing Volumes. Add the Blur override to a Volume to adjust intensity.", MessageType.Info);
        }
    }
}