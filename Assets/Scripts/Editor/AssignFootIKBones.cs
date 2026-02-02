#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls;

public class AssignFootIKBones : MonoBehaviour
{
    // Correct bone names for "3D Game Character" skeleton
    // Verified from DiagnoseCharacter skeleton dump
    private const string PELVIS = "root.x";
    private const string RIGHT_FOOT_IK = "ik_foot.r";   // Separate IK target bones created by CreateIKBones
    private const string LEFT_FOOT_IK = "ik_foot.l";
    private const string RIGHT_FOOT = "foot.r";
    private const string LEFT_FOOT = "foot.l";
    private const string RIGHT_LEG = "leg_stretch.r";
    private const string LEFT_LEG = "leg_stretch.l";
    private const string RIGHT_KNEE_POLE = "ik_knee_pole.r";  // Dedicated knee pole targets
    private const string LEFT_KNEE_POLE = "ik_knee_pole.l";
    private const string RIGHT_HAND_IK = "ik_hand.r";   // Separate IK target bones
    private const string LEFT_HAND_IK = "ik_hand.l";
    private const string RIGHT_HAND = "hand.r";
    private const string LEFT_HAND = "hand.l";
    private const string RIGHT_FOREARM = "forearm_stretch.r";
    private const string LEFT_FOREARM = "forearm_stretch.l";

    [MenuItem("Tools/Demagnetized/6. Assign Bone References")]
    static void Assign()
    {
        int fixed_count = 0;

        // Fix PA_FPS_3DGameChar.asset (the MAIN PA used by the player)
        fixed_count += FixPAAsset("Assets/KBP01/FBX/PA_FPS_3DGameChar.asset");

        // Fix legacy PA paths
        fixed_count += FixPAAsset("Assets/PA_Banana Man.asset");
        fixed_count += FixPAAsset("Assets/_Project/PA_FootIK_Fixed.asset");

        // Fix standalone Step assets (referenced by FPSExampleController)
        fixed_count += FixStandaloneSteps();

        AssetDatabase.SaveAssets();
        Debug.Log("[Bones] Done! Fixed " + fixed_count + " modifiers. IK targets: " + RIGHT_FOOT_IK + "/" + LEFT_FOOT_IK);
    }

    static int FixStandaloneSteps()
    {
        string[] stepPaths = {
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Turn_Left.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Turn_Right.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_InPlace.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Crouch.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_Uncrouch.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_WalkStart.asset",
            "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Steps/Step_WalkStop.asset"
        };

        int count = 0;
        foreach (var path in stepPaths)
        {
            var step = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(path);
            if (step == null) continue;
            var so = new SerializedObject(step);
            SetBone(so, "pelvis", PELVIS);
            SetBone(so, "rightFootIkTarget", RIGHT_FOOT_IK);
            SetBone(so, "leftFootIkTarget", LEFT_FOOT_IK);
            SetBone(so, "rightFoot", RIGHT_FOOT);
            SetBone(so, "leftFoot", LEFT_FOOT);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(step);
            Debug.Log("[Bones] Step: " + step.name + " IK=" + RIGHT_FOOT_IK + "/" + LEFT_FOOT_IK);
            count++;
        }
        return count;
    }

    static int FixPAAsset(string path)
    {
        var pa = AssetDatabase.LoadAssetAtPath<ProceduralAnimationSettings>(path);
        if (pa == null) { Debug.LogWarning("[Bones] PA not found: " + path); return 0; }

        int count = 0;

        foreach (var mod in pa.modifiers)
        {
            if (mod == null) continue;
            var so = new SerializedObject(mod);

            if (mod is StepModifierSettings)
            {
                SetBone(so, "pelvis", PELVIS);
                SetBone(so, "rightFootIkTarget", RIGHT_FOOT_IK);
                SetBone(so, "leftFootIkTarget", LEFT_FOOT_IK);
                SetBone(so, "rightFoot", RIGHT_FOOT);
                SetBone(so, "leftFoot", LEFT_FOOT);
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " Step: pelvis=" + PELVIS + ", feet=" + RIGHT_FOOT + "/" + LEFT_FOOT);
                count++;
            }
            else if (mod is PivotModifierSettings)
            {
                SetBone(so, "pelvis", PELVIS);
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " Pivot: pelvis=" + PELVIS);
                count++;
            }
            else if (mod is CopyBoneSettings)
            {
                if (mod.name.Contains("Right"))
                {
                    SetBone(so, "copyFrom", RIGHT_FOOT);
                    SetBone(so, "copyTo", RIGHT_FOOT_IK);
                }
                else if (mod.name.Contains("Left"))
                {
                    SetBone(so, "copyFrom", LEFT_FOOT);
                    SetBone(so, "copyTo", LEFT_FOOT_IK);
                }
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " CopyBone: " + mod.name);
                count++;
            }
            else if (mod is FootIkSettings)
            {
                SetBone(so, "pelvis", PELVIS);
                SetBone(so, "rightFootIk", RIGHT_FOOT_IK);
                SetBone(so, "leftFootIk", LEFT_FOOT_IK);
                so.FindProperty("layerMask").intValue = 1;
                so.FindProperty("traceOffset").floatValue = 0.58f;
                so.FindProperty("traceRadius").floatValue = 0.03f;
                so.FindProperty("interpSpeed").floatValue = 15f;
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " FootIK: pelvis=" + PELVIS + ", IK=" + RIGHT_FOOT_IK + "/" + LEFT_FOOT_IK);
                count++;
            }
            else if (mod is TwoBoneIkSettings)
            {
                var tipProp = so.FindProperty("tip");
                string curTip = tipProp != null ? (tipProp.FindPropertyRelative("name")?.stringValue ?? "") : "";
                bool isHand = mod.name.ToLower().Contains("hand") || curTip.ToLower().Contains("hand");
                bool isRight = mod.name.Contains("Right") || curTip.Contains("Right")
                            || curTip.EndsWith(".r") || curTip.Contains("_r");
                bool isLeft = mod.name.Contains("Left") || curTip.Contains("Left")
                           || curTip.EndsWith(".l") || curTip.Contains("_l");

                if (isRight)
                {
                    if (isHand)
                    {
                        // Hands use world targets (weapon grips), NOT bone IK targets
                        SetBone(so, "tip", RIGHT_HAND);
                        SetBone(so, "target", RIGHT_HAND);
                        SetBone(so, "poleTarget", RIGHT_FOREARM);
                    }
                    else
                    {
                        SetBone(so, "tip", RIGHT_FOOT);
                        SetBone(so, "target", RIGHT_FOOT_IK);
                        SetBone(so, "poleTarget", RIGHT_LEG);
                    }
                }
                else if (isLeft)
                {
                    if (isHand)
                    {
                        // Hands use world targets (weapon grips), NOT bone IK targets
                        SetBone(so, "tip", LEFT_HAND);
                        SetBone(so, "target", LEFT_HAND);
                        SetBone(so, "poleTarget", LEFT_FOREARM);
                    }
                    else
                    {
                        SetBone(so, "tip", LEFT_FOOT);
                        SetBone(so, "target", LEFT_FOOT_IK);
                        SetBone(so, "poleTarget", LEFT_LEG);
                    }
                }
                else
                {
                    Debug.LogWarning("[Bones] Unknown TwoBoneIK side: " + mod.name + " tip='" + curTip + "' - skipping");
                }
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " TwoBoneIK: " + mod.name);
                count++;
            }
            else if (mod is FullBodyIkSettings)
            {
                // Hand IK: target=same as tip (hands use world targets, not bone targets)
                SetBone(so, "rightHandIk.tip", RIGHT_HAND);
                SetBone(so, "rightHandIk.target", RIGHT_HAND);
                SetBone(so, "rightHandIk.poleTarget", RIGHT_FOREARM);
                SetBone(so, "leftHandIk.tip", LEFT_HAND);
                SetBone(so, "leftHandIk.target", LEFT_HAND);
                SetBone(so, "leftHandIk.poleTarget", LEFT_FOREARM);
                // Right Foot IK: foot → lower leg → thigh chain, knee pole for bend direction
                SetBone(so, "rightFootIk.tip", RIGHT_FOOT);
                SetBone(so, "rightFootIk.target", RIGHT_FOOT_IK);
                SetBone(so, "rightFootIk.poleTarget", RIGHT_KNEE_POLE);
                // Left Foot IK
                SetBone(so, "leftFootIk.tip", LEFT_FOOT);
                SetBone(so, "leftFootIk.target", LEFT_FOOT_IK);
                SetBone(so, "leftFootIk.poleTarget", LEFT_KNEE_POLE);
                so.ApplyModifiedProperties();
                Debug.Log("[Bones] " + path + " FullBodyIK: hands=" + RIGHT_HAND + "/" + LEFT_HAND + ", feet=" + RIGHT_FOOT + "/" + LEFT_FOOT);
                count++;
            }
        }

        EditorUtility.SetDirty(pa);
        return count;
    }

    static void SetBone(SerializedObject so, string propName, string boneName)
    {
        var prop = so.FindProperty(propName);
        if (prop == null)
        {
            Debug.LogWarning("[Bones] Property '" + propName + "' not found!");
            return;
        }

        var nameProp = prop.FindPropertyRelative("name");
        var indexProp = prop.FindPropertyRelative("index");

        if (nameProp != null) nameProp.stringValue = boneName;
        if (indexProp != null) indexProp.intValue = -1;
    }
}
#endif
