using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Hands; // for XRHandSkeletonDriver

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RuntimeHandSnapshot : MonoBehaviour
{
    [Header("Assign your XR hand roots")]
    [Tooltip("Root that contains ALL bones for the LEFT hand (the hierarchy driven by XRHandSkeletonDriver).")]
    public Transform leftBoneRoot;

    [Tooltip("Root that contains ALL bones for the RIGHT hand.")]
    public Transform rightBoneRoot;

    [Header("Optional: Mesh roots (if your SkinnedMeshRenderers are not children of the bone root)")]
    [Tooltip("Object that contains SkinnedMeshRenderer(s) for the LEFT hand. Leave empty to auto-find.")]
    public Transform leftMeshRoot;

    [Tooltip("Object that contains SkinnedMeshRenderer(s) for the RIGHT hand. Leave empty to auto-find.")]
    public Transform rightMeshRoot;

    [Header("Controls")]
    public KeyCode snapshotLeftKey = KeyCode.Alpha1;
    public KeyCode snapshotRightKey = KeyCode.Alpha2;

    [Header("Output (Editor only)")]
    public bool savePrefabInEditor = true;
    public string prefabFolder = "Assets/GeneratedHandPoses/Runtime";

    [Header("Freeze options")]
    [Tooltip("Disable Animators and remove XR drivers on the cloned skeleton/meshes so they stay frozen.")]
    public bool freezeClone = true;

    [Tooltip("Bake SkinnedMeshRenderers to static meshes (optional).")]
    public bool bakeStaticMesh = false;

    [Header("Spawn parent (optional)")]
    public Transform snapshotParent;
    public string snapshotsParentName = "HandSnapshots";

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
        if (leftBoneRoot && Input.GetKeyDown(snapshotLeftKey))
            Snapshot(leftBoneRoot, leftMeshRoot, "Left");

        if (rightBoneRoot && Input.GetKeyDown(snapshotRightKey))
            Snapshot(rightBoneRoot, rightMeshRoot, "Right");
    }

    void Snapshot(Transform boneRoot, Transform meshRootHint, string tag)
    {
        if (!boneRoot)
        {
            Debug.LogWarning($"[RuntimeHandSnapshot] {tag}: Bone root not set.");
            return;
        }

        // 1) Clone bones (this captures the live pose)
        var bonesClone = Instantiate(boneRoot.gameObject);
        bonesClone.name = $"Snapshot_{tag}_Bones";
        bonesClone.transform.SetPositionAndRotation(boneRoot.position, boneRoot.rotation);
        bonesClone.transform.localScale = boneRoot.lossyScale;
        bonesClone.transform.SetParent(snapshotParent, true);

        // 2) Find meshes that are skinned to the original bones
        var smrs = FindRelevantSMRs(boneRoot, meshRootHint);
        if (smrs.Count == 0)
        {
            Debug.LogWarning($"[RuntimeHandSnapshot] {tag}: No SkinnedMeshRenderer found for this bone root. " +
                             "Assign Mesh Root or ensure your mesh references these bones.");
        }

        // 3) Clone mesh objects and retarget to cloned bones
        var meshClones = new List<GameObject>();
        foreach (var smr in smrs)
        {
            // Clone just the mesh object subtree
            var meshClone = Instantiate(smr.gameObject);
            meshClone.name = $"Snapshot_{tag}_Mesh_{smr.gameObject.name}";
            meshClone.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            meshClone.transform.localScale = smr.transform.lossyScale;
            meshClone.transform.SetParent(snapshotParent, true);
            meshClones.Add(meshClone);

            // Retarget all SMRs under this cloned subtree to the cloned bones
            var smrsInClone = meshClone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var csmr in smrsInClone)
            {
                RetargetSMR(csmr, boneRoot, bonesClone.transform);
                if (bakeStaticMesh)
                    BakeOneSMRToStatic(csmr);
            }
        }

        // 4) Freeze: stop further updates
        if (freezeClone)
        {
            // On bones clone
            foreach (var anim in bonesClone.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;
            foreach (var drv in bonesClone.GetComponentsInChildren<XRHandSkeletonDriver>(true))
#if UNITY_EDITOR
                DestroyImmediate(drv);
#else
                Destroy(drv);
#endif
            // On mesh clones
            foreach (var mc in meshClones)
            {
                foreach (var anim in mc.GetComponentsInChildren<Animator>(true))
                    anim.enabled = false;
            }
        }

        Debug.Log($"[RuntimeHandSnapshot] {tag}: Snapshot created. Bones: {bonesClone.name}, Meshes: {meshClones.Count}");

        // 5) Save prefab (Editor only)
        SavePrefabIfEditor(tag, bonesClone, meshClones);
    }

    // ---- Helpers ----

    // Find all SkinnedMeshRenderers that use any bone under boneRoot.
    List<SkinnedMeshRenderer> FindRelevantSMRs(Transform boneRoot, Transform meshRootHint)
    {
        var result = new HashSet<SkinnedMeshRenderer>();

        // A) Use the hint, if provided
        if (meshRootHint)
        {
            foreach (var s in meshRootHint.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                result.Add(s);
        }

        // B) Any SMR under the bone root (common if mesh is a child of bones)
        foreach (var s in boneRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            result.Add(s);

        // C) Global search: find any SMR in the scene that references bones under boneRoot
        var allSMR = FindObjectsOfType<SkinnedMeshRenderer>(true);
        foreach (var s in allSMR)
        {
            if (s.bones == null) continue;
            if (s.bones.Any(b => b && IsDescendantOf(b, boneRoot)))
                result.Add(s);
        }

        return result.ToList();
    }

    static bool IsDescendantOf(Transform t, Transform potentialAncestor)
    {
        var cur = t;
        while (cur != null)
        {
            if (cur == potentialAncestor) return true;
            cur = cur.parent;
        }
        return false;
    }

    // Retarget one SkinnedMeshRenderer to the cloned bones:
    // Map each original bone (relative to original boneRoot) to the matching Transform under clonedBoneRoot.
    void RetargetSMR(SkinnedMeshRenderer clonedSmr, Transform originalBoneRoot, Transform clonedBoneRoot)
    {
        var originalSmr = FindOriginalCounterpart(clonedSmr);
        // Prefer the original SMR (has correct bone refs). If null, use the cloned's current bones.
        var sourceBones = (originalSmr ? originalSmr.bones : clonedSmr.bones) ?? new Transform[0];

        var newBones = new Transform[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
            var src = sourceBones[i];
            if (!src) { newBones[i] = null; continue; }

            // Get path from originalBoneRoot to the source bone
            string relPath = GetRelativePath(src, originalBoneRoot);
            Transform dst = null;
            if (!string.IsNullOrEmpty(relPath))
                dst = clonedBoneRoot.Find(relPath);

            // Fallback: by name search (in case hierarchy differs slightly)
            if (!dst)
                dst = FindByNameInChildren(clonedBoneRoot, src.name);

            newBones[i] = dst ? dst : newBones[i]; // keep null if not found
        }
        clonedSmr.bones = newBones;

        // Root bone
        var srcRoot = (originalSmr ? originalSmr.rootBone : clonedSmr.rootBone);
        if (srcRoot)
        {
            string rootRel = GetRelativePath(srcRoot, originalBoneRoot);
            Transform dstRoot = null;
            if (!string.IsNullOrEmpty(rootRel))
                dstRoot = clonedBoneRoot.Find(rootRel);
            if (!dstRoot)
                dstRoot = FindByNameInChildren(clonedBoneRoot, srcRoot.name);
            clonedSmr.rootBone = dstRoot;
        }
    }

    // Try to find the original (non-cloned) SMR this clone came from (by shared mesh/material & name proximity)
    SkinnedMeshRenderer FindOriginalCounterpart(SkinnedMeshRenderer cloned)
    {
        var all = FindObjectsOfType<SkinnedMeshRenderer>(true);
        var mesh = cloned.sharedMesh;
        foreach (var s in all)
        {
            if (s == cloned) continue;
            if (s.sharedMesh == mesh && s.name == cloned.name.Replace("Snapshot_Left_Mesh_", "").Replace("Snapshot_Right_Mesh_", ""))
                return s;
        }
        // fallback by mesh only
        foreach (var s in all)
        {
            if (s == cloned) continue;
            if (s.sharedMesh == mesh) return s;
        }
        return null;
    }

    static string GetRelativePath(Transform t, Transform root)
    {
        if (!t || !root) return null;
        if (!IsDescendantOf(t, root)) return null;

        var parts = new List<string>();
        var cur = t;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static Transform FindByNameInChildren(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void BakeOneSMRToStatic(SkinnedMeshRenderer smr)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked);

        var go = new GameObject(smr.gameObject.name + "_Baked");
        go.transform.SetParent(smr.transform, false);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = baked;
        mr.sharedMaterials = smr.sharedMaterials;

        // Optionally disable the skinned mesh to leave only static
        smr.enabled = false;
    }

    void SavePrefabIfEditor(string tag, GameObject bonesClone, List<GameObject> meshClones)
    {
#if UNITY_EDITOR
        if (!savePrefabInEditor) return;

        // Put bones + meshes under a single container for the prefab
        var root = new GameObject($"Snapshot_{tag}_Root");
        root.transform.SetParent(snapshotParent, true);
        bonesClone.transform.SetParent(root.transform, true);
        foreach (var mc in meshClones) mc.transform.SetParent(root.transform, true);

        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder(prefabFolder))
        {
            var parts = prefabFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        var path = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder}/{root.name}.prefab");
        PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        if (ok) Debug.Log($"[RuntimeHandSnapshot] Saved prefab: {path}");
        else Debug.LogError($"[RuntimeHandSnapshot] Failed to save prefab: {path}");
#endif
    }
}
