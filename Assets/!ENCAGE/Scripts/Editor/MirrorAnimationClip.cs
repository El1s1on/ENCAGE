using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MirrorAnimationClip : EditorWindow
{
    private AnimationClip sourceClip;

    [MenuItem("Tools/Mirror Arm Animation")]
    static void ShowWindow()
    {
        GetWindow<MirrorAnimationClip>("Mirror Arm");
    }

    void OnGUI()
    {
        GUILayout.Label("Mirror Arm Animation (Swap Left/Right)", EditorStyles.boldLabel);
        sourceClip = (AnimationClip)EditorGUILayout.ObjectField("Source Clip", sourceClip, typeof(AnimationClip), false);

        if (sourceClip == null)
        {
            EditorGUILayout.HelpBox("Выбери анимацию для зеркалирования", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Create Mirrored Copy (Swap Hands)", GUILayout.Height(30)))
        {
            MirrorClip();
        }
    }

    void MirrorClip()
    {
        if (sourceClip == null) return;

        AnimationClip newClip = new AnimationClip();
        newClip.name = sourceClip.name + "_Mirrored";
        newClip.frameRate = sourceClip.frameRate;

        Dictionary<string, AnimationCurve> rightArmCurves = new Dictionary<string, AnimationCurve>();
        Dictionary<string, AnimationCurve> leftArmCurves = new Dictionary<string, AnimationCurve>();

        // Сначала собираем все кривые для правой и левой руки
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(sourceClip))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (curve == null) continue;

            string path = binding.path;

            if (path.Contains("_r") || path.Contains("right") || path.Contains("Right"))
            {
                string key = path + "|" + binding.propertyName;
                rightArmCurves[key] = curve;
            }
            else if (path.Contains("_l") || path.Contains("left") || path.Contains("Left"))
            {
                string key = path + "|" + binding.propertyName;
                leftArmCurves[key] = curve;
            }
            else
            {
                // Копируем кривые, не связанные с руками (spine, pelvis и т.д.)
                AnimationUtility.SetEditorCurve(newClip, binding, curve);
            }
        }

        // Меняем руки местами: правая -> левая, левая -> правая
        foreach (var kvp in rightArmCurves)
        {
            string originalPath = kvp.Key.Split('|')[0];
            string property = kvp.Key.Split('|')[1];
            AnimationCurve originalCurve = kvp.Value;

            // Меняем путь с _r на _l
            string newPath = originalPath.Replace("_r", "_l");
            newPath = newPath.Replace("_R", "_L");
            newPath = newPath.Replace("right", "left");
            newPath = newPath.Replace("Right", "Left");

            // Зеркалим кривую
            AnimationCurve mirroredCurve = MirrorCurve(originalCurve, property);

            EditorCurveBinding newBinding = new EditorCurveBinding();
            newBinding.path = newPath;
            newBinding.propertyName = property;
            newBinding.type = typeof(Transform);

            AnimationUtility.SetEditorCurve(newClip, newBinding, mirroredCurve);
        }

        foreach (var kvp in leftArmCurves)
        {
            string originalPath = kvp.Key.Split('|')[0];
            string property = kvp.Key.Split('|')[1];
            AnimationCurve originalCurve = kvp.Value;

            // Меняем путь с _l на _r
            string newPath = originalPath.Replace("_l", "_r");
            newPath = newPath.Replace("_L", "_R");
            newPath = newPath.Replace("left", "right");
            newPath = newPath.Replace("Left", "Right");

            // Зеркалим кривую
            AnimationCurve mirroredCurve = MirrorCurve(originalCurve, property);

            EditorCurveBinding newBinding = new EditorCurveBinding();
            newBinding.path = newPath;
            newBinding.propertyName = property;
            newBinding.type = typeof(Transform);

            AnimationUtility.SetEditorCurve(newClip, newBinding, mirroredCurve);
        }

        // Копируем настройки анимации
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
        AnimationUtility.SetAnimationClipSettings(newClip, settings);

        // Сохраняем
        string assetPath = AssetDatabase.GetAssetPath(sourceClip);
        string directory = System.IO.Path.GetDirectoryName(assetPath);
        string newPathFull = directory + "/" + newClip.name + ".anim";

        AssetDatabase.CreateAsset(newClip, newPathFull);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", $"Анимация создана: {newClip.name}\n\nПравая и левая руки поменялись местами", "OK");
    }

    AnimationCurve MirrorCurve(AnimationCurve originalCurve, string propertyName)
    {
        AnimationCurve newCurve = new AnimationCurve();

        // Инвертируем X позицию и вращения по Y/Z
        bool invert = propertyName.Contains("m_LocalPosition.x") ||
                      propertyName.Contains("localEulerAnglesRaw.y") ||
                      propertyName.Contains("localEulerAnglesRaw.z");

        foreach (Keyframe key in originalCurve.keys)
        {
            float newValue = invert ? -key.value : key.value;

            Keyframe newKey = new Keyframe(key.time, newValue);
            newKey.inTangent = invert ? -key.inTangent : key.inTangent;
            newKey.outTangent = invert ? -key.outTangent : key.outTangent;
            newKey.tangentMode = key.tangentMode;

            newCurve.AddKey(newKey);
        }

        return newCurve;
    }
}