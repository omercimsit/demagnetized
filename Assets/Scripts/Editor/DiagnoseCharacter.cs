using UnityEngine;
using UnityEditor;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;

// Forces recompilation - file content is correct, Unity bee cache was stale
public class DiagnoseCharacter
{
    [MenuItem("Tools/Demagnetized/Diagnose Character Setup")]
    static void Run()
    {
        Debug.Log("[Diag] Starting character diagnostic...");

        var animator = Object.FindFirstObjectByType<Animator>();
        if (animator == null) { Debug.LogError("[Diag] No Animator"); return; }

        Debug.Log("[Diag] Animator: " + animator.gameObject.name + " isHuman=" + animator.isHuman + " avatar=" + (animator.avatar != null ? animator.avatar.name : "NULL"));

        DiagnoseSkeleton(animator);

        var pa = animator.GetComponent<ProceduralAnimationComponent>();
        Debug.Log("[Diag] PA Component: " + (pa != null ? "Found" : "NULL"));

        if (pa != null)
        {
            DiagnosePA(pa);
        }

        DiagnosePAAsset(animator);

        // find FPSExampleController by type name to avoid compile errors when CAS Demo isn't imported
        MonoBehaviour fps = null;
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb.GetType().Name == "FPSExampleController") { fps = mb; break; }
        }
        if (fps != null)
        {
            var fso = new SerializedObject(fps);
            string[] fields = { "stepTurnLeft", "stepTurnRight", "stepInPlace", "startMoving", "stopMoving", "stepCrouch", "stepUncrouch" };
            foreach (var f in fields)
            {
                var prop = fso.FindProperty(f);
                string val = (prop != null && prop.objectReferenceValue != null) ? prop.objectReferenceValue.name : "NULL";
                Debug.Log("[Diag] " + f + "=" + val);
            }
        }

        DiagnoseStepAssets();

        Debug.Log("[Diag] Diagnostic complete.");
    }

    static void DiagnoseSkeleton(Animator animator)
    {
        var skeleton = animator.GetComponent<CharacterSkeleton>();
        if (skeleton == null)
            skeleton = animator.GetComponentInChildren<CharacterSkeleton>();
        if (skeleton == null)
            skeleton = animator.GetComponentInParent<CharacterSkeleton>();

        if (skeleton == null)
        {
            skeleton = Object.FindFirstObjectByType<CharacterSkeleton>();
        }

        if (skeleton == null)
        {
            Debug.LogWarning("[Diag] No CharacterSkeleton found in scene!");
            return;
        }

        var bones = skeleton.SkeletonBones;
        Debug.Log("[Diag] CharacterSkeleton on: " + skeleton.gameObject.name);
        Debug.Log("[Diag] Total skeleton bones: " + bones.Count);

        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            string hasTransform = bone.transform != null ? bone.transform.name : "NULL_TRANSFORM";
            Debug.Log("[Diag]   Bone[" + i + "] name=\"" + bone.rigElement.name + "\" idx=" + bone.rigElement.index + " depth=" + bone.rigElement.depth + " transform=" + hasTransform);
        }

        // these are the key bones we care about for "3D Game Character"
        Debug.Log("[Diag] --- KEY BONE CHECK ---");
        string[] keyBones = {
            "root.x", "spine_01", "spine_02", "spine_03", "neck",
            "foot.r", "foot.l", "leg_stretch.r", "leg_stretch.l",
            "thigh_stretch.r", "thigh_stretch.l",
            "toes_01.r", "toes_01.l",
            "hand.r", "hand.l", "arm_stretch.r", "arm_stretch.l"
        };
        foreach (var boneName in keyBones)
        {
            bool found = false;
            int foundIndex = -1;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].rigElement.name == boneName)
                {
                    found = true;
                    foundIndex = i;
                    break;
                }
            }
            string status = found ? "FOUND at index " + foundIndex : "NOT FOUND";
            if (found)
                Debug.Log("[Diag]   \"" + boneName + "\": " + status);
            else
                Debug.LogWarning("[Diag]   \"" + boneName + "\": " + status + " <<<");
        }
    }

    static void DiagnosePA(ProceduralAnimationComponent pa)
    {
        var so = new SerializedObject(pa);

        var settingsRef = so.FindProperty("proceduralSettings");
        if (settingsRef != null && settingsRef.objectReferenceValue != null)
        {
            Debug.Log("[Diag] PA Asset: " + settingsRef.objectReferenceValue.name + " (" + AssetDatabase.GetAssetPath(settingsRef.objectReferenceValue) + ")");
        }
        else
        {
            Debug.LogWarning("[Diag] PA Asset: NULL or not found!");
        }
    }

    static void DiagnosePAAsset(Animator animator)
    {
        // check all PA assets we know about
        string[] paAssetPaths = {
            "Assets/KBP01/FBX/PA_FPS_3DGameChar.asset",
            "Assets/PA_Banana Man.asset",
            "Assets/_Project/PA_FootIK_Fixed.asset"
        };

        foreach (var path in paAssetPaths)
        {
            var pa = AssetDatabase.LoadAssetAtPath<ProceduralAnimationSettings>(path);
            if (pa == null) continue;

            Debug.Log("[Diag] PA Asset: " + path);
            foreach (var mod in pa.modifiers)
            {
                if (mod == null) continue;
                if (mod is StepModifierSettings step)
                {
                    Debug.Log("[Diag]   Step Modifier: " + mod.name);
                    Debug.Log("[Diag]     pelvis=\"" + step.pelvis.name + "\" (idx=" + step.pelvis.index + ")");
                    Debug.Log("[Diag]     rightFootIkTarget=\"" + step.rightFootIkTarget.name + "\" (idx=" + step.rightFootIkTarget.index + ")");
                    Debug.Log("[Diag]     leftFootIkTarget=\"" + step.leftFootIkTarget.name + "\" (idx=" + step.leftFootIkTarget.index + ")");
                    Debug.Log("[Diag]     rightFoot=\"" + step.rightFoot.name + "\" (idx=" + step.rightFoot.index + ")");
                    Debug.Log("[Diag]     leftFoot=\"" + step.leftFoot.name + "\" (idx=" + step.leftFoot.index + ")");
                }
            }
        }
    }

    static void DiagnoseStepAssets()
    {
        Debug.Log("[Diag] --- STEP ASSET BONE NAMES ---");
        string[] stepPaths = {
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Turn_Left.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Turn_Right.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_InPlace.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Crouch.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Uncrouch.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_WalkStart.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_WalkStop.asset"
        };

        foreach (var path in stepPaths)
        {
            var step = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(path);
            if (step == null) { Debug.LogWarning("[Diag] Step NOT FOUND: " + path); continue; }

            Debug.Log("[Diag] " + step.name + ": pelvis=\"" + step.pelvis.name + "\" rIK=\"" + step.rightFootIkTarget.name + "\" lIK=\"" + step.leftFootIkTarget.name + "\" rFoot=\"" + step.rightFoot.name + "\" lFoot=\"" + step.leftFoot.name + "\"");
        }
    }
}
