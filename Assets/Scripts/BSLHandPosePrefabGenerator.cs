#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

// -----------------------------
// 1) ScriptableObject: HandRigMapping
// -----------------------------
[CreateAssetMenu(fileName = "HandRigMapping", menuName = "BSL/Hand Rig Mapping", order = 0)]
public class HandRigMapping : ScriptableObject
{
    [Header("Root / Wrist")]
    public Transform wrist;   // hand root in your FBX prefab

    [Serializable]
    public class FingerChain
    {
        public Transform proximal;   // MCP (or thumb CMC/MCP)
        public Transform intermediate; // PIP (or thumb IP)
        public Transform distal;     // DIP
    }

    [Header("Finger Chains (Right or Left hand model)")]
    public FingerChain thumb = new FingerChain();
    public FingerChain index = new FingerChain();
    public FingerChain middle = new FingerChain();
    public FingerChain ring = new FingerChain();
    public FingerChain little = new FingerChain();

    [Header("Curl Rotation Axis (local)")]
    public Vector3 thumbAxis = new Vector3(1, 0, 0);  // default X
    public Vector3 fingerAxis = new Vector3(1, 0, 0); // default X (index/middle/ring/little)

    [Header("Max Flexion Degrees (per bone)")]
    public float thumbProxMax = 35f;
    public float thumbIntMax = 45f;
    public float thumbDstMax = 80f;

    public float fingerProxMax = 65f;  // MCP
    public float fingerIntMax = 100f; // PIP
    public float fingerDstMax = 80f;  // DIP
}

// -----------------------------
// 2) EditorWindow: Generator
// -----------------------------
public class BSLHandPosePrefabGenerator : EditorWindow
{
    // Inputs
    [Header("Source Hand Prefabs (FBX or Prefab)")]
    public GameObject rightHandModelPrefab;
    public GameObject leftHandModelPrefab; // optional; if empty we’ll reuse right

    [Header("Rig Mapping (per model)")]
    public HandRigMapping rightHandMapping;
    public HandRigMapping leftHandMapping; // optional; if empty we’ll reuse right mapping

    [Header("Gesture Assets (any mix)")]
    public List<UnityEngine.Object> gestureAssets = new List<UnityEngine.Object>(); // XRHandPose, XRHandShape, DualHandGestureAsset

    [Header("Output")]
    public string outputFolder = "Assets/GeneratedHandPoses";

    [Header("Options")]
    public bool autoBindByName = true;
    public bool generateLeft = true;
    public bool generateRight = true;

    [MenuItem("Tools/BSL/Hand Pose Prefab Generator")]
    public static void ShowWindow()
    {
        var win = GetWindow<BSLHandPosePrefabGenerator>("Hand Pose Generator");
        win.minSize = new Vector2(520, 520);
    }

    private Vector2 _scroll;

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField("BSL Hand Pose Prefab Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Source Models
        EditorGUILayout.LabelField("Source Hand Prefabs", EditorStyles.boldLabel);
        rightHandModelPrefab = (GameObject)EditorGUILayout.ObjectField("Right Hand Prefab", rightHandModelPrefab, typeof(GameObject), false);
        leftHandModelPrefab = (GameObject)EditorGUILayout.ObjectField("Left Hand Prefab (optional)", leftHandModelPrefab, typeof(GameObject), false);

        EditorGUILayout.Space();

        // Mappings
        EditorGUILayout.LabelField("Rig Mappings", EditorStyles.boldLabel);
        rightHandMapping = (HandRigMapping)EditorGUILayout.ObjectField("Right Mapping", rightHandMapping, typeof(HandRigMapping), false);
        leftHandMapping = (HandRigMapping)EditorGUILayout.ObjectField("Left Mapping (optional)", leftHandMapping, typeof(HandRigMapping), false);

        if (rightHandModelPrefab && GUILayout.Button("Create Right Mapping (if needed)"))
            rightHandMapping = CreateMappingAssetAdjacent(rightHandModelPrefab, "Right");

        if (leftHandModelPrefab && GUILayout.Button("Create Left Mapping (if needed)"))
            leftHandMapping = CreateMappingAssetAdjacent(leftHandModelPrefab, "Left");

        if (autoBindByName && rightHandMapping && rightHandModelPrefab)
        {
            if (GUILayout.Button("Auto-Bind RIGHT Bones By Name"))
                AutoBindMapping(rightHandModelPrefab, rightHandMapping);
        }
        if (autoBindByName && leftHandMapping && leftHandModelPrefab)
        {
            if (GUILayout.Button("Auto-Bind LEFT Bones By Name"))
                AutoBindMapping(leftHandModelPrefab, leftHandMapping);
        }

        EditorGUILayout.Space();

        // Gestures
        EditorGUILayout.LabelField("Gesture Assets", EditorStyles.boldLabel);
        int toRemove = -1;
        for (int i = 0; i < gestureAssets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            gestureAssets[i] = EditorGUILayout.ObjectField(gestureAssets[i], typeof(UnityEngine.Object), false);
            if (GUILayout.Button("X", GUILayout.Width(24))) toRemove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (toRemove >= 0) gestureAssets.RemoveAt(toRemove);
        if (GUILayout.Button("Add Gesture Asset")) gestureAssets.Add(null);

        EditorGUILayout.Space();

        // Output + options
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        if (GUILayout.Button("Select...", GUILayout.Width(90)))
        {
            string p = EditorUtility.OpenFolderPanel("Choose Output Folder (within project)", Application.dataPath, "");
            if (!string.IsNullOrEmpty(p))
            {
                if (p.StartsWith(Application.dataPath))
                {
                    outputFolder = "Assets" + p.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Pick a folder inside this Unity project.", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        generateRight = EditorGUILayout.ToggleLeft("Generate Right Hand Prefabs", generateRight);
        generateLeft = EditorGUILayout.ToggleLeft("Generate Left Hand Prefabs", generateLeft);
        autoBindByName = EditorGUILayout.ToggleLeft("Auto-Bind By Name (heuristics)", autoBindByName);

        EditorGUILayout.Space();
        GUI.enabled = CanGenerate();
        if (GUILayout.Button("Generate Prefabs", GUILayout.Height(36)))
        {
            try
            {
                GenerateAll();
                EditorUtility.DisplayDialog("Done", "Generated hand pose prefabs.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[BSL] Generation failed:\n" + ex);
            }
        }
        GUI.enabled = true;

        EditorGUILayout.EndScrollView();
    }

    private bool CanGenerate()
    {
        if (!generateLeft && !generateRight) return false;
        if (gestureAssets.Count == 0) return false;
        if (generateRight && (!rightHandModelPrefab || !rightHandMapping)) return false;
        if (generateLeft && (!leftHandModelPrefab && !rightHandModelPrefab)) return false; // can reuse right
        return true;
    }

    // -----------------------------
    // Auto-binding heuristics
    // -----------------------------
    private void AutoBindMapping(GameObject handPrefab, HandRigMapping map)
    {
        if (!handPrefab || !map) return;

        // Build lookup by name (lowercased)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab);
        try
        {
            var all = instance.GetComponentsInChildren<Transform>(true);
            var dict = all.ToDictionary(t => t.name.ToLowerInvariant(), t => t);

            // Wrist/root: pick the first child under prefab root if obvious, else the root
            map.wrist = GuessByAny(dict, "wrist", "hand_wrist", "root", "hand_root", "hand");

            // Fingers
            AutoBindFinger(dict, map.thumb, "thumb", "tm", "th");
            AutoBindFinger(dict, map.index, "index", "idx", "i");
            AutoBindFinger(dict, map.middle, "middle", "mid", "m");
            AutoBindFinger(dict, map.ring, "ring", "r");
            AutoBindFinger(dict, map.little, "little", "pinky", "small", "l");

            // Default axes (X) are fine for many rigs, but you can tweak on the mapping asset.
            if (map.thumbAxis == Vector3.zero) map.thumbAxis = new Vector3(1, 0, 0);
            if (map.fingerAxis == Vector3.zero) map.fingerAxis = new Vector3(1, 0, 0);

            EditorUtility.SetDirty(map);
            Debug.Log("[BSL] Auto-bind done (heuristic). Please verify assignments in the HandRigMapping asset.");
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private static void AutoBindFinger(Dictionary<string, Transform> dict, HandRigMapping.FingerChain chain, params string[] keys)
    {
        // Try to find proximal/intermediate/distal by name priority and index numbers (1/2/3)
        // Very common patterns: <finger>_1 / <finger>_2 / <finger>_3 or proximal/intermediate/distal
        chain.proximal = GuessByAny(dict, Combine(keys, "proximal", "mcp", "1", "a"));
        chain.intermediate = GuessByAny(dict, Combine(keys, "intermediate", "pip", "2", "b"));
        chain.distal = GuessByAny(dict, Combine(keys, "distal", "dip", "3", "c"));

        // Fallbacks if missing: try broader search
        if (!chain.proximal) chain.proximal = GuessByAny(dict, keys);
        if (!chain.intermediate) chain.intermediate = GuessByAny(dict, keys.Select(k => k + "_2").ToArray());
        if (!chain.distal) chain.distal = GuessByAny(dict, keys.Select(k => k + "_3").ToArray());
    }

    private static string[] Combine(string[] a, params string[] b)
        => a.SelectMany(x => b.Select(y => x + "_" + y)).Concat(a).Concat(b).ToArray();

    private static Transform GuessByAny(Dictionary<string, Transform> dict, params string[] keys)
    {
        foreach (var k in keys)
        {
            var key = k.ToLowerInvariant();
            // exact
            if (dict.TryGetValue(key, out var t)) return t;
            // contains
            foreach (var kv in dict)
            {
                if (kv.Key.Contains(key))
                    return kv.Value;
            }
        }
        return null;
    }

    private HandRigMapping CreateMappingAssetAdjacent(GameObject modelPrefab, string tag)
    {
        string prefabPath = AssetDatabase.GetAssetPath(modelPrefab);
        string folder = Path.GetDirectoryName(prefabPath).Replace("\\", "/");
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/HandRigMapping_{tag}.asset");
        var map = ScriptableObject.CreateInstance<HandRigMapping>();
        AssetDatabase.CreateAsset(map, assetPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = map;
        return map;
    }

    // -----------------------------
    // Generation
    // -----------------------------
    private void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            // Try to create nested folders
            CreateFolderRecursive(outputFolder);
        }

        foreach (var obj in gestureAssets)
        {
            if (obj == null) continue;

            if (obj is DualHandGestureAsset dual)
            {
                if (generateRight) GenerateFromDual(dual, true);
                if (generateLeft) GenerateFromDual(dual, false);
            }
            else if (obj is XRHandPose xpose)
            {
                // XRHandPose references XRHandShape
                var shape = xpose.handShape;
                string baseName = string.IsNullOrEmpty(dualName(xpose)) ? xpose.name : dualName(xpose);
                // Try infer hand from name
                bool isLeft = NameSuggestsLeft(baseName);
                bool isRight = NameSuggestsRight(baseName);
                if (!isLeft && !isRight) { isLeft = generateLeft; isRight = generateRight; }

                if (isRight && generateRight) GenerateFromShape(shape, baseName, true);
                if (isLeft && generateLeft) GenerateFromShape(shape, baseName, false);
            }
            else if (obj is XRHandShape xshape)
            {
                string baseName = xshape.name;
                bool isLeft = NameSuggestsLeft(baseName);
                bool isRight = NameSuggestsRight(baseName);
                if (!isLeft && !isRight) { isLeft = generateLeft; isRight = generateRight; }

                if (isRight && generateRight) GenerateFromShape(xshape, baseName, true);
                if (isLeft && generateLeft) GenerateFromShape(xshape, baseName, false);
            }
            else
            {
                Debug.LogWarning($"[BSL] Unsupported asset type: {obj.GetType().Name} ({obj.name}). Skipping.");
            }
        }
    }

    private static string dualName(XRHandPose p) => p ? p.name : "";

    private bool NameSuggestsLeft(string name)
    {
        name = name.ToLowerInvariant();
        return name.Contains("left") || name.Contains("_l") || name.EndsWith(" (l)");
    }
    private bool NameSuggestsRight(string name)
    {
        name = name.ToLowerInvariant();
        return name.Contains("right") || name.Contains("_r") || name.EndsWith(" (r)");
    }

    private void CreateFolderRecursive(string path)
    {
        // path like "Assets/GeneratedHandPoses/Sub"
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

    // --- Extract curls from XRHandShape ---
    private struct Curls
    {
        public float thumb, index, middle, ring, little; // -1 means unknown
    }

    private Curls ExtractCurls(XRHandShape shape)
    {
        var c = new Curls { thumb = -1, index = -1, middle = -1, ring = -1, little = -1 };
        if (!shape) return c;
        if (shape.fingerShapeConditions == null) return c;

        foreach (var cond in shape.fingerShapeConditions)
        {
            float? fullCurl = null;
            if (cond.targets != null)
            {
                for (int i = 0; i < cond.targets.Length; i++)
                {
                    if (cond.targets[i].shapeType == XRFingerShapeType.FullCurl)
                    {
                        fullCurl = cond.targets[i].desired;
                        break;
                    }
                }
            }

            if (!fullCurl.HasValue) continue;

            switch (cond.fingerID)
            {
                case XRHandFingerID.Thumb: c.thumb = fullCurl.Value; break;
                case XRHandFingerID.Index: c.index = fullCurl.Value; break;
                case XRHandFingerID.Middle: c.middle = fullCurl.Value; break;
                case XRHandFingerID.Ring: c.ring = fullCurl.Value; break;
                case XRHandFingerID.Little: c.little = fullCurl.Value; break;
            }
        }
        return c;
    }

    // --- Extract curls from DualHandGestureAsset ---
    private Curls ExtractCurls(DualHandGestureAsset asset, bool rightHand)
    {
        var f = rightHand ? asset.singleFrame.right : asset.singleFrame.left;
        return new Curls
        {
            thumb = f.curls.thumb,
            index = f.curls.index,
            middle = f.curls.middle,
            ring = f.curls.ring,
            little = f.curls.little
        };
    }

    // --- Generators ---
    private void GenerateFromShape(XRHandShape shape, string baseName, bool rightHand)
    {
        if (!shape) return;
        var curls = ExtractCurls(shape);
        string leaf = MakeSafeName($"{(rightHand ? "RightHand" : "LeftHand")}_{baseName}");
        PoseAndPrefab(curls, leaf, rightHand);
    }

    private void GenerateFromDual(DualHandGestureAsset dual, bool rightHand)
    {
        if (!dual) return;
        var curls = ExtractCurls(dual, rightHand);
        string leaf = MakeSafeName($"{(rightHand ? "RightHand" : "LeftHand")}_{(string.IsNullOrEmpty(dual.gestureName) ? dual.name : dual.gestureName)}");
        PoseAndPrefab(curls, leaf, rightHand);
    }

    private string MakeSafeName(string n)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return n.Replace(' ', '_');
    }

    private void PoseAndPrefab(Curls curls, string prefabLeafName, bool rightHand)
    {
        // Choose model + mapping
        var model = rightHand ? rightHandModelPrefab : (leftHandModelPrefab ? leftHandModelPrefab : rightHandModelPrefab);
        var map = rightHand ? rightHandMapping : (leftHandMapping ? leftHandMapping : rightHandMapping);

        if (!model || !map)
        {
            Debug.LogError($"[BSL] Missing {(rightHand ? "RIGHT" : "LEFT")} model or mapping. Skipping {prefabLeafName}.");
            return;
        }

        // Instantiate a fresh copy
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = prefabLeafName;

        try
        {
            // Apply curls to finger chains
            ApplyFingerCurl(map.thumb, curls.thumb, map.thumbAxis, map.thumbProxMax, map.thumbIntMax, map.thumbDstMax);
            ApplyFingerCurl(map.index, curls.index, map.fingerAxis, map.fingerProxMax, map.fingerIntMax, map.fingerDstMax);
            ApplyFingerCurl(map.middle, curls.middle, map.fingerAxis, map.fingerProxMax, map.fingerIntMax, map.fingerDstMax);
            ApplyFingerCurl(map.ring, curls.ring, map.fingerAxis, map.fingerProxMax, map.fingerIntMax, map.fingerDstMax);
            ApplyFingerCurl(map.little, curls.little, map.fingerAxis, map.fingerProxMax, map.fingerIntMax, map.fingerDstMax);

            // Save as prefab
            string path = $"{outputFolder}/{prefabLeafName}.prefab";
            path = AssetDatabase.GenerateUniqueAssetPath(path);
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

    private void ApplyFingerCurl(HandRigMapping.FingerChain chain, float curl01, Vector3 localAxis, float proxMax, float intMax, float dstMax)
    {
        if (curl01 < 0f) return; // unknown → skip
        curl01 = Mathf.Clamp01(curl01);
        var axis = (localAxis.sqrMagnitude < 1e-6f) ? Vector3.right : localAxis.normalized;

        // Apply on top of rest localRotation
        if (chain.proximal)
            chain.proximal.localRotation = chain.proximal.localRotation * Quaternion.AngleAxis(curl01 * proxMax, axis);
        if (chain.intermediate)
            chain.intermediate.localRotation = chain.intermediate.localRotation * Quaternion.AngleAxis(curl01 * intMax, axis);
        if (chain.distal)
            chain.distal.localRotation = chain.distal.localRotation * Quaternion.AngleAxis(curl01 * dstMax, axis);
    }
}
#endif
