using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands; // for XRHandSkeletonDriver

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SimpleHandSnapshot : MonoBehaviour
{
    [Header("Assign the WHOLE hand assembly roots (contain bones + SkinnedMeshRenderer)")]
    public Transform leftHandAssemblyRoot;
    public Transform rightHandAssemblyRoot;

    [Header("Keys")]
    public KeyCode snapshotLeftKey = KeyCode.Alpha1; // press 1 = snapshot left
    public KeyCode snapshotRightKey = KeyCode.Alpha2; // press 2 = snapshot right

    [Header("Freeze & Visibility")]
    [Tooltip("Disable Animators and remove XR drivers on the clone so it stays frozen.")]
    public bool freezeClone = true;
    [Tooltip("Ensure SMRs render even when off-screen (prevents culling issues).")]
    public bool forceUpdateWhenOffscreen = true;
    [Tooltip("Optional: bake SkinnedMeshRenderers to static meshes (no bones).")]
    public bool bakeStaticMesh = false;

    [Header("Spawn parent (optional)")]
    public Transform snapshotParent;
    public string snapshotsParentName = "HandSnapshots";

    [Header("Editor-only prefab save")]
    public bool savePrefabInEditor = true;
    public string prefabFolder = "Assets/GeneratedHandPoses/Runtime";

    void Awake()
    {
        if (!snapshotParent)
        {
            var go = GameObject.Find(snapshotsParentName);
            if (!go) go = new GameObject(snapshotsParentName);
            snapshotParent = go.transform;
        }
    }

    void Update()
    {
        if (leftHandAssemblyRoot && Input.GetKeyDown(snapshotLeftKey))
            SnapshotWhole(leftHandAssemblyRoot, "Left");

        if (rightHandAssemblyRoot && Input.GetKeyDown(snapshotRightKey))
            SnapshotWhole(rightHandAssemblyRoot, "Right");
    }

    void SnapshotWhole(Transform assemblyRoot, string tag)
    {
        if (!assemblyRoot)
        {
            Debug.LogWarning($"[SimpleHandSnapshot] {tag}: Hand assembly root not set.");
            return;
        }

        // 1) Clone the ENTIRE assembly (bones + mesh)
        var clone = Instantiate(assemblyRoot.gameObject);
        clone.name = $"Snapshot_{tag}";
        clone.transform.SetPositionAndRotation(assemblyRoot.position, assemblyRoot.rotation);
        clone.transform.localScale = assemblyRoot.lossyScale;
        clone.transform.SetParent(snapshotParent, true);

        // 2) Make sure the mesh renders
        var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrs)
        {
            smr.enabled = true;
            if (forceUpdateWhenOffscreen) smr.updateWhenOffscreen = true;
        }

        // 3) Freeze (stop updates)
        if (freezeClone)
        {
            foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;

            foreach (var drv in clone.GetComponentsInChildren<XRHandSkeletonDriver>(true))
#if UNITY_EDITOR
                DestroyImmediate(drv);
#else
                Destroy(drv);
#endif

            // Remove other XR components if present (safe no-ops if absent)
            RemoveByTypeName(clone, "XRHandMeshController");
            RemoveByTypeName(clone, "XRHandTrackingEvents");
        }

        // 4) Optional: bake to static meshes
        if (bakeStaticMesh)
            BakeAllSMRsToStatic(clone);

        Debug.Log($"[SimpleHandSnapshot] {tag}: Snapshot created. Bones={clone.GetComponentsInChildren<Transform>(true).Length}, SMRs={smrs.Length}");

        // 5) Save as prefab (Editor only)
        SavePrefabIfEditor(clone, $"Snapshot_{tag}");
    }

    void BakeAllSMRsToStatic(GameObject root)
    {
        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrs)
        {
            var baked = new Mesh();
            smr.BakeMesh(baked);

            var go = new GameObject(smr.gameObject.name + "_Baked");
            go.transform.SetParent(smr.transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = baked;
            mr.sharedMaterials = smr.sharedMaterials;

            smr.enabled = false; // leave just the baked mesh visible
        }
    }

    void RemoveByTypeName(GameObject go, string typeName)
    {
        var comps = go.GetComponentsInChildren<Component>(true);
        var toRemove = new List<Component>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            if (c.GetType().Name == typeName) toRemove.Add(c);
        }
        foreach (var c in toRemove)
#if UNITY_EDITOR
            DestroyImmediate(c);
#else
            Destroy(c);
#endif
    }

    void SavePrefabIfEditor(GameObject clone, string baseName)
    {
#if UNITY_EDITOR
        if (!savePrefabInEditor) return;

        EnsureFolder(prefabFolder);
        var path = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder}/{baseName}.prefab");
        PrefabUtility.SaveAsPrefabAsset(clone, path, out bool ok);
        if (ok) Debug.Log($"[SimpleHandSnapshot] Saved prefab: {path}");
        else Debug.LogError($"[SimpleHandSnapshot] Failed to save prefab: {path}");
#endif
    }

#if UNITY_EDITOR
    static void EnsureFolder(string path)
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
#endif
}
