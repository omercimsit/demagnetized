using UnityEngine;
using UnityEditor;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;

public class CreateIKBones
{
    // these are the IK target bones - CopyBone modifier sets their position each frame
    static readonly string[] IK_BONE_NAMES = {
        "ik_foot.r",
        "ik_foot.l",
        "ik_hand.r",
        "ik_hand.l"
    };

    // knee pole targets stay fixed, just tell the solver which way the knee should bend
    // positions are in root.x local space: -Y forward, +Z up, +X right
    static readonly (string name, Vector3 localPos)[] POLE_TARGET_BONES = {
        ("ik_knee_pole.r", new Vector3(0.15f, -1.0f, 0.55f)),
        ("ik_knee_pole.l", new Vector3(-0.15f, -1.0f, 0.55f)),
    };

    [MenuItem("Tools/Demagnetized/1. Create IK Bones")]
    static void Create()
    {
        var skeleton = Object.FindFirstObjectByType<CharacterSkeleton>();
        if (skeleton == null)
        {
            Debug.LogError("[CreateIK] No CharacterSkeleton found in scene!");
            return;
        }

        // IK bones MUST go under root.x (pelvis), NOT root (armature).
        // TIP counter-rotation spins the armature root, so anything under root.x
        // stays in sync with the visible skeleton. Learned this the hard way.
        var bones = skeleton.SkeletonBones;
        if (bones == null || bones.Count < 2)
        {
            Debug.LogError("[CreateIK] CharacterSkeleton has insufficient bones!");
            return;
        }

        // find root.x - should be index 1, child of armature root
        Transform ikParent = null;
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i].rigElement.name == "root.x")
            {
                ikParent = bones[i].transform;
                break;
            }
        }
        if (ikParent == null)
        {
            // fallback: just use bone index 1 and hope for the best
            ikParent = bones[1].transform;
        }
        Debug.Log("[CreateIK] IK parent bone: " + ikParent.name + " on " + skeleton.gameObject.name);

        int created = 0;
        foreach (var boneName in IK_BONE_NAMES)
        {
            Transform existing = ikParent.Find(boneName);
            if (existing != null)
            {
                Debug.Log("[CreateIK] '" + boneName + "' already exists - skipping");
                continue;
            }

            var go = new GameObject(boneName);
            go.transform.SetParent(ikParent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            created++;
            Debug.Log("[CreateIK] Created '" + boneName + "' under " + ikParent.name);
        }

        // TODO: might want to expose pole positions as a config file instead of hardcoding
        foreach (var (boneName, localPos) in POLE_TARGET_BONES)
        {
            Transform existing = ikParent.Find(boneName);
            if (existing != null)
            {
                Debug.Log("[CreateIK] '" + boneName + "' already exists - updating position");
                existing.localPosition = localPos;
                continue;
            }

            var go = new GameObject(boneName);
            go.transform.SetParent(ikParent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            created++;
            Debug.Log("[CreateIK] Created '" + boneName + "' at local " + localPos + " under " + ikParent.name);
        }

        skeleton.UpdateSkeleton();
        Debug.Log("[CreateIK] UpdateSkeleton called - new bones registered");

        // verify they actually made it into the skeleton
        bones = skeleton.SkeletonBones;
        var allBoneNames = new System.Collections.Generic.List<string>(IK_BONE_NAMES);
        foreach (var (boneName, _) in POLE_TARGET_BONES) allBoneNames.Add(boneName);

        foreach (var boneName in allBoneNames)
        {
            bool found = false;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].rigElement.name == boneName)
                {
                    found = true;
                    Debug.Log("[CreateIK] Verified: '" + boneName + "' at index " + i);
                    break;
                }
            }
            if (!found)
                Debug.LogError("[CreateIK] FAILED: '" + boneName + "' not found in skeleton after update!");
        }

        EditorUtility.SetDirty(skeleton);
        EditorUtility.SetDirty(skeleton.gameObject);

        UpdateSkeletonPrefab(skeleton);

        Debug.Log("[CreateIK] Done! Created " + created + " IK bones. Total skeleton bones: " + bones.Count);
        Debug.Log("[CreateIK] IMPORTANT: Now run 'Fix Step Bones REAL' and '6. Assign Bone References' to update PA assets!");
    }

    static void UpdateSkeletonPrefab(CharacterSkeleton skeleton)
    {
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(skeleton.gameObject);
        if (!string.IsNullOrEmpty(prefabPath))
        {
            var prefabType = PrefabUtility.GetPrefabAssetType(skeleton.gameObject);
            if (prefabType == PrefabAssetType.Model)
            {
                // can't apply to FBX model prefabs - bones become scene overrides instead
                Debug.Log("[CreateIK] Skipping prefab apply - Model Prefab (FBX). IK bones are scene overrides.");
            }
            else
            {
                PrefabUtility.ApplyPrefabInstance(
                    PrefabUtility.GetNearestPrefabInstanceRoot(skeleton.gameObject),
                    InteractionMode.AutomatedAction);
                Debug.Log("[CreateIK] Applied changes to prefab: " + prefabPath);
            }
        }

        // check known skeleton prefab paths just in case
        string[] knownPaths = {
            "Assets/Skeleton_Banana Man.prefab",
            "Assets/Skeleton_3D Game Character.prefab"
        };
        foreach (var path in knownPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                Debug.Log("[CreateIK] Found prefab at " + path + " - scene changes will be applied via prefab override or Apply");
            }
        }
    }
}
