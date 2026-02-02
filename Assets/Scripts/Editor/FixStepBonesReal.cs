using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;

public class FixStepBonesReal
{
    // Comprehensive mapping: Banana Man bone names → 3D Game Character bone names
    static readonly Dictionary<string, string> BoneMap = new Dictionary<string, string>
    {
        // Pelvis
        {"Hips", "root.x"},

        // Spine chain
        {"Spine", "spine_01.x"},
        {"Spine 1", "spine_01.x"},   // Banana Man uses space before number!
        {"Spine1", "spine_02.x"},
        {"Spine 2", "spine_02.x"},   // Banana Man uses space before number!
        {"Chest", "spine_02.x"},
        {"Spine2", "spine_03.x"},
        {"Spine 3", "spine_03.x"},   // Banana Man uses space before number!
        {"Upper Chest", "spine_03.x"},
        {"UpperChest", "spine_03.x"},
        {"Neck", "neck.x"},
        {"Head", "head.x"},

        // Shoulders
        {"Right Shoulder", "shoulder.r"},
        {"Left Shoulder", "shoulder.l"},

        // Arms
        {"Right UpperArm", "arm_stretch.r"},
        {"Left UpperArm", "arm_stretch.l"},
        {"Right Arm", "arm_stretch.r"},
        {"Left Arm", "arm_stretch.l"},
        {"Right LowerArm", "forearm_stretch.r"},
        {"Left LowerArm", "forearm_stretch.l"},
        {"Right ForeArm", "forearm_stretch.r"},
        {"Left ForeArm", "forearm_stretch.l"},

        // Hands
        {"Right Hand", "hand.r"},
        {"Left Hand", "hand.l"},
        {"RightHand", "hand.r"},
        {"LeftHand", "hand.l"},

        // Finger tips (FullBodyIK uses these as hand chain tips → map to hand for proper arm IK)
        {"Right Hand Thumb 3_end", "hand.r"},
        {"Left Hand Thumb 3_end", "hand.l"},

        // Left hand fingers (Attach Left Hand modifier)
        {"Left Hand Index 1", "c_index1.l"},
        {"Left Hand Index 2", "c_index2.l"},
        {"Left Hand Index 3", "c_index3.l"},
        {"Left Hand Index 3_end", "c_index3.l"},
        {"Left Hand Middle 1", "c_middle1.l"},
        {"Left Hand Middle 2", "c_middle2.l"},
        {"Left Hand Middle 3", "c_middle3.l"},
        {"Left Hand Middle 3_end", "c_middle3.l"},
        {"Left Hand Ring 1", "c_ring1.l"},
        {"Left Hand Ring 2", "c_ring2.l"},
        {"Left Hand Ring 3", "c_ring3.l"},
        {"Left Hand Ring 3_end", "c_ring3.l"},
        {"Left Hand Pinky 1", "c_pinky1.l"},
        {"Left Hand Pinky 2", "c_pinky2.l"},
        {"Left Hand Pinky 3", "c_pinky3.l"},
        {"Left Hand Pinky 3_end", "c_pinky3.l"},
        {"Left Hand Thumb 1", "c_thumb1.l"},
        {"Left Hand Thumb 2", "c_thumb2.l"},
        {"Left Hand Thumb 3", "c_thumb3.l"},

        // Right hand fingers
        {"Right Hand Index 1", "c_index1.r"},
        {"Right Hand Index 2", "c_index2.r"},
        {"Right Hand Index 3", "c_index3.r"},
        {"Right Hand Index 3_end", "c_index3.r"},
        {"Right Hand Middle 1", "c_middle1.r"},
        {"Right Hand Middle 2", "c_middle2.r"},
        {"Right Hand Middle 3", "c_middle3.r"},
        {"Right Hand Middle 3_end", "c_middle3.r"},
        {"Right Hand Ring 1", "c_ring1.r"},
        {"Right Hand Ring 2", "c_ring2.r"},
        {"Right Hand Ring 3", "c_ring3.r"},
        {"Right Hand Ring 3_end", "c_ring3.r"},
        {"Right Hand Pinky 1", "c_pinky1.r"},
        {"Right Hand Pinky 2", "c_pinky2.r"},
        {"Right Hand Pinky 3", "c_pinky3.r"},
        {"Right Hand Pinky 3_end", "c_pinky3.r"},
        {"Right Hand Thumb 1", "c_thumb1.r"},
        {"Right Hand Thumb 2", "c_thumb2.r"},
        {"Right Hand Thumb 3", "c_thumb3.r"},

        // Legs
        {"Right UpperLeg", "thigh_stretch.r"},
        {"Left UpperLeg", "thigh_stretch.l"},
        {"Right Leg", "leg_stretch.r"},
        {"Left Leg", "leg_stretch.l"},
        {"Right LowerLeg", "leg_stretch.r"},
        {"Left LowerLeg", "leg_stretch.l"},
        {"RightLeg", "leg_stretch.r"},
        {"LeftLeg", "leg_stretch.l"},

        // Feet
        {"Right Foot", "foot.r"},
        {"Left Foot", "foot.l"},
        {"RightFoot", "foot.r"},
        {"LeftFoot", "foot.l"},

        // Toes
        {"Right Toes", "toes_01.r"},
        {"Left Toes", "toes_01.l"},
        {"RightToes", "toes_01.r"},
        {"LeftToes", "toes_01.l"},

        // IK targets → separate IK bones created by CreateIKBones tool
        {"IK foot_r", "ik_foot.r"},
        {"IK foot_l", "ik_foot.l"},
        {"IK hand_r", "ik_hand.r"},
        {"IK hand_l", "ik_hand.l"},
        {"IK hand_l_right", "ik_hand.l"},
    };

    // Known good 3D Game Character bone names OR weapon bones that can't be remapped (don't warn about these)
    static bool IsKnownGoodName(string name)
    {
        return string.IsNullOrEmpty(name) || name == "None"
            || name.Contains(".") || name.Contains("_0")
            || name.StartsWith("c_") || name.StartsWith("root")
            || name.StartsWith("spine") || name.StartsWith("neck")
            || name.StartsWith("head") || name.StartsWith("shoulder")
            || name.StartsWith("arm") || name.StartsWith("forearm")
            || name.StartsWith("hand") || name.StartsWith("thigh")
            || name.StartsWith("leg") || name.StartsWith("foot")
            || name.StartsWith("toes") || name.StartsWith("ik_")
            // Weapon/aim bones - no equivalent in 3D Game Character, created at runtime by FixFPSCamera
            || name.Contains("weapon_bone") || name == "aim_socket";
    }

    [MenuItem("Tools/Demagnetized/Fix Step Bones REAL")]
    static void Fix()
    {
        int fixedCount = 0;

        // Fix PA assets (generic remapper handles ALL modifier types)
        fixedCount += FixPAAsset("Assets/KBP01/FBX/PA_FPS_3DGameChar.asset");
        fixedCount += FixPAAsset("Assets/PA_Banana Man.asset");
        fixedCount += FixPAAsset("Assets/_Project/PA_FootIK_Fixed.asset");

        // Fix standalone Step assets
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
            if (step == null) continue;
            var so = new SerializedObject(step);
            int remapped = RemapAllBones(so, step.name);
            if (remapped > 0)
            {
                EditorUtility.SetDirty(step);
                fixedCount++;
            }
            Debug.Log($"[FixBones] {step.name}: remapped {remapped} bone refs");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FixBones] Done! Fixed {fixedCount} modifiers for 3D Game Character skeleton.");
    }

    static int FixPAAsset(string path)
    {
        var pa = AssetDatabase.LoadAssetAtPath<ProceduralAnimationSettings>(path);
        if (pa == null) { Debug.LogWarning($"[FixBones] PA not found: {path}"); return 0; }

        int count = 0;
        foreach (var mod in pa.modifiers)
        {
            if (mod == null) continue;
            var so = new SerializedObject(mod);
            int remapped = RemapAllBones(so, $"{path}/{mod.name}");
            if (remapped > 0)
            {
                EditorUtility.SetDirty(mod);
                count++;
            }
            Debug.Log($"[FixBones] {path} {mod.name} ({mod.GetType().Name}): remapped {remapped} bone refs");
        }

        EditorUtility.SetDirty(pa);
        return count;
    }

    /// <summary>
    /// Generic bone name remapper: walks ALL serialized properties,
    /// finds all KRigElement instances, and maps old Banana Man names
    /// to correct 3D Game Character names.
    /// </summary>
    static int RemapAllBones(SerializedObject so, string context)
    {
        int count = 0;
        var prop = so.GetIterator();
        bool enterChildren = true;

        while (prop.Next(enterChildren))
        {
            enterChildren = true;

            // Detect KRigElement: a struct with "name" (string) + "index" (int) children
            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                var nameProp = prop.FindPropertyRelative("name");
                var indexProp = prop.FindPropertyRelative("index");

                if (nameProp != null && nameProp.propertyType == SerializedPropertyType.String
                    && indexProp != null && indexProp.propertyType == SerializedPropertyType.Integer)
                {
                    string currentName = nameProp.stringValue;
                    if (BoneMap.TryGetValue(currentName, out string newName))
                    {
                        nameProp.stringValue = newName;
                        indexProp.intValue = -1;
                        count++;
                        Debug.Log($"[FixBones]   '{currentName}' → '{newName}' ({prop.propertyPath})");
                    }
                    else if (!IsKnownGoodName(currentName))
                    {
                        Debug.LogWarning($"[FixBones] UNKNOWN bone: '{currentName}' in {context} ({prop.propertyPath})");
                    }
                    enterChildren = false; // Skip KRigElement sub-properties
                }
            }
        }

        if (count > 0) so.ApplyModifiedProperties();
        return count;
    }
}
