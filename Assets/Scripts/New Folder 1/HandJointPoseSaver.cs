#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class HandJointPoseSaver : MonoBehaviour
{
    [Header("Capture Settings")]
    public KeyCode saveKey = KeyCode.P;
    public string outputFolder = "Assets/GeneratedHandPoses/JointCaptures";
    public string gestureName = "A";
    public HandJointPoseAsset.HandSide side = HandJointPoseAsset.HandSide.Right;

    [Tooltip("Root of the hand rig in the scene (the parent of all 26 bones).")]
    public Transform rigRoot;

    [Tooltip("Capture only these bones (by name contains). Leave empty to capture ALL children under rigRoot.")]
    public string[] boneNameFilters = new string[] { }; // e.g., "Wrist", "Thumb", "Index", ...

    private void Update()
    {
        if (Input.GetKeyDown(saveKey))
            SaveNow();
    }

    public void SaveNow()
    {
        if (rigRoot == null)
        {
            Debug.LogError("[HandJointPoseSaver] rigRoot is null.");
            return;
        }

        var bones = new List<HandJointPoseAsset.BonePose>();
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == rigRoot) continue;
            if (!PassesFilter(t.name)) continue;

            var bp = new HandJointPoseAsset.BonePose
            {
                bonePath = GetRelativePath(t, rigRoot),
                localRotation = t.localRotation
            };
            bones.Add(bp);
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
            CreateFolderRecursive(outputFolder);

        var asset = ScriptableObject.CreateInstance<HandJointPoseAsset>();
        asset.gestureName = gestureName;
        asset.side = side;
        asset.sourceRootName = rigRoot.name;
        asset.bones = bones.ToArray();

        var path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{gestureName}_{side}_JointPose.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[HandJointPoseSaver] Saved {bones.Count} bones to: {path}");
    }

    private bool PassesFilter(string name)
    {
        if (boneNameFilters == null || boneNameFilters.Length == 0) return true;
        var n = name.ToLowerInvariant();
        foreach (var f in boneNameFilters)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (n.Contains(f.ToLowerInvariant())) return true;
        }
        return false;
    }

    private static string GetRelativePath(Transform t, Transform root)
    {
        var stack = new System.Text.StringBuilder(t.name);
        var cur = t.parent;
        while (cur != null && cur != root)
        {
            stack.Insert(0, cur.name + "/");
            cur = cur.parent;
        }
        return stack.ToString();
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
