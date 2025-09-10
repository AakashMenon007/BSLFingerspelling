#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class HandPrefabBakerFromJointPose : EditorWindow
{
    public GameObject handModelPrefab;
    public HandJointPoseAsset jointPoseAsset;
    public string outputFolder = "Assets/GeneratedHandPoses/BakedPrefabs";
    public string outputNameOverride = ""; // optional

    [MenuItem("Tools/BSL/Bake Prefab From Joint Pose")]
    public static void ShowWindow()
    {
        GetWindow<HandPrefabBakerFromJointPose>("Bake Hand Prefab");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Bake Hand Prefab From Joint Pose", EditorStyles.boldLabel);
        handModelPrefab = (GameObject)EditorGUILayout.ObjectField("Hand Model Prefab", handModelPrefab, typeof(GameObject), false);
        jointPoseAsset = (HandJointPoseAsset)EditorGUILayout.ObjectField("Joint Pose Asset", jointPoseAsset, typeof(HandJointPoseAsset), false);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        outputNameOverride = EditorGUILayout.TextField("Output Name (optional)", outputNameOverride);

        EditorGUILayout.Space();
        GUI.enabled = handModelPrefab && jointPoseAsset;
        if (GUILayout.Button("Bake Prefab", GUILayout.Height(30)))
        {
            try
            {
                Bake();
            }
            catch (Exception ex)
            {
                Debug.LogError("[BSL] Bake failed:\n" + ex);
            }
        }
        GUI.enabled = true;
    }

    private void Bake()
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
            CreateFolderRecursive(outputFolder);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(handModelPrefab);
        instance.name = string.IsNullOrEmpty(outputNameOverride)
            ? $"{jointPoseAsset.gestureName}_{jointPoseAsset.side}"
            : outputNameOverride;

        try
        {
            // Apply saved local rotations by relative path
            foreach (var bp in jointPoseAsset.bones)
            {
                var t = FindByRelativePath(instance.transform, bp.bonePath);
                if (t == null)
                {
                    Debug.LogWarning($"[BSL] Bone not found: {bp.bonePath} (skipped)");
                    continue;
                }

                t.localRotation = bp.localRotation;
            }

            // Save
            var path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{instance.name}.prefab");
            PrefabUtility.SaveAsPrefabAsset(instance, path, out bool success);
            if (success)
                Debug.Log($"[BSL] Saved prefab: {path}");
            else
                Debug.LogError($"[BSL] Failed to save prefab: {path}");
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private static Transform FindByRelativePath(Transform root, string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return root;
        var parts = relPath.Split('/');
        Transform cur = root;
        foreach (var p in parts)
        {
            cur = cur.Find(p);
            if (cur == null) return null;
        }
        return cur;
    }

    private static void CreateFolderRecursive(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
