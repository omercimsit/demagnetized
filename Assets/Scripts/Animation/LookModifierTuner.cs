#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;


public class LookModifierTuner : EditorWindow
{
    private float _pitch = 5f;
    private float _yaw = 8f;
    private float _roll = 3f;

    private LookModifierSettings _lookMod;
    private FullBodyIkSettings _fullBodyIk;
    private FootIkSettings _footIk;
    private StepModifierSettings _stepMod;
    private CopyBoneSettings _copyRightFoot;
    private CopyBoneSettings _copyLeftFoot;
    private SerializedObject _lookSO;
    private SerializedObject _fullBodyIkSO;
    private SerializedObject _footIkSO;
    private SerializedObject _stepModSO;
    private SerializedObject _copyRightSO;
    private SerializedObject _copyLeftSO;
    private string _paPath = "Assets/KBP01/FBX/PA_FPS_3DGameChar.asset";
    private ProceduralAnimationSettings _paAsset;

    // Foot IK
    private float _footIkWeight = 1f;
    private float _footIkPoleWeight = 1f;
    private float _footYawOffset = 0f;
    private bool _copyFootRotation = true;
    private bool _showFootIk = true;

    // Foot IK Trace
    private float _traceOffset = 0.58f;
    private float _traceRadius = 0.03f;
    private float _interpSpeed = 15f;
    private float _groundOffset = -0.01f;
    private bool _showFootTrace = true;

    // Step Modifier
    private float _maxOffsetAngle = 45f;
    private bool _showStep = false;


    [MenuItem("Tools/Demagnetized/LIVE Tuner")]
    static void Open()
    {
        var w = GetWindow<LookModifierTuner>("IK & Look Tuner");
        w.minSize = new Vector2(340, 420);
        w.Show();
    }

    void OnEnable()
    {
        FindModifiers();
    }

    void FindModifiers()
    {
        _paAsset = AssetDatabase.LoadAssetAtPath<ProceduralAnimationSettings>(_paPath);
        if (_paAsset == null) return;

        foreach (var mod in _paAsset.modifiers)
        {
            if (mod is LookModifierSettings look)
            {
                _lookMod = look;
                _lookSO = new SerializedObject(look);

                var pitch0 = _lookSO.FindProperty("pitchOffsetElements.Array.data[0].clampedAngle");
                if (pitch0 != null) _pitch = pitch0.vector2Value.x;

                var yaw0 = _lookSO.FindProperty("yawOffsetElements.Array.data[0].clampedAngle");
                if (yaw0 != null) _yaw = yaw0.vector2Value.x;

                var roll0 = _lookSO.FindProperty("rollOffsetElements.Array.data[0].clampedAngle");
                if (roll0 != null) _roll = roll0.vector2Value.x;
            }
            else if (mod is FullBodyIkSettings fbIk)
            {
                _fullBodyIk = fbIk;
                _fullBodyIkSO = new SerializedObject(fbIk);

                var w = _fullBodyIkSO.FindProperty("rightFootIk.weight");
                if (w != null) _footIkWeight = w.floatValue;

                var pw = _fullBodyIkSO.FindProperty("rightFootIk.poleWeight");
                if (pw != null) _footIkPoleWeight = pw.floatValue;

                var yaw = _fullBodyIkSO.FindProperty("footYawOffset");
                if (yaw != null) _footYawOffset = yaw.floatValue;
            }
            else if (mod is FootIkSettings fIk)
            {
                _footIk = fIk;
                _footIkSO = new SerializedObject(fIk);

                var to = _footIkSO.FindProperty("traceOffset");
                if (to != null) _traceOffset = to.floatValue;

                var tr = _footIkSO.FindProperty("traceRadius");
                if (tr != null) _traceRadius = tr.floatValue;

                var isp = _footIkSO.FindProperty("interpSpeed");
                if (isp != null) _interpSpeed = isp.floatValue;

                var go = _footIkSO.FindProperty("groundOffset");
                if (go != null) _groundOffset = go.floatValue;
            }
            else if (mod is StepModifierSettings step)
            {
                _stepMod = step;
                _stepModSO = new SerializedObject(step);

                var moa = _stepModSO.FindProperty("maxAllowedOffsetAngle");
                if (moa != null) _maxOffsetAngle = moa.floatValue;
            }
            else if (mod is CopyBoneSettings cb)
            {
                var so = new SerializedObject(cb);
                var toName = so.FindProperty("copyTo.name");
                if (toName != null && toName.stringValue == "ik_foot.r")
                {
                    _copyRightFoot = cb;
                    _copyRightSO = so;
                    var cr = so.FindProperty("copyRotation");
                    if (cr != null) _copyFootRotation = cr.boolValue;
                }
                else if (toName != null && toName.stringValue == "ik_foot.l")
                {
                    _copyLeftFoot = cb;
                    _copyLeftSO = so;
                }
            }
        }
    }

    void OnGUI()
    {
        if (_lookSO == null)
        {
            EditorGUILayout.HelpBox("PA_FPS_3DGameChar.asset not found or no Look Modifier!", MessageType.Error);
            if (GUILayout.Button("Retry")) FindModifiers();
            return;
        }

        bool isPlaying = Application.isPlaying;

        if (isPlaying)
        {
            EditorGUILayout.HelpBox("PLAY MODE - Changes apply in real-time!", MessageType.Info);
        }

        _lookSO.Update();

        // === LOOK MODIFIER ===
        EditorGUILayout.LabelField("LOOK MODIFIER (Spine Rotation)", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        bool lookChanged = false;

        EditorGUI.BeginChangeCheck();
        _pitch = EditorGUILayout.Slider("Pitch (up/down)", _pitch, 0f, 20f);
        EditorGUILayout.LabelField("  Total: " + (_pitch * 3f).ToString("F0") + "° (3 bones)", EditorStyles.miniLabel);
        if (EditorGUI.EndChangeCheck()) { SetAllElements("pitchOffsetElements", _pitch); lookChanged = true; }

        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        _yaw = EditorGUILayout.Slider("Yaw (left/right)", _yaw, 0f, 25f);
        EditorGUILayout.LabelField("  Total: " + (_yaw * 3f).ToString("F0") + "° (3 bones)", EditorStyles.miniLabel);
        if (EditorGUI.EndChangeCheck()) { SetAllElements("yawOffsetElements", _yaw); lookChanged = true; }

        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        _roll = EditorGUILayout.Slider("Roll (tilt)", _roll, 0f, 10f);
        EditorGUILayout.LabelField("  Total: " + (_roll * 3f).ToString("F0") + "° (3 bones)", EditorStyles.miniLabel);
        if (EditorGUI.EndChangeCheck()) { SetAllElements("rollOffsetElements", _roll); lookChanged = true; }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Subtle"))   { _pitch = 3; _yaw = 5; _roll = 2; ApplyLookAll(); lookChanged = true; }
        if (GUILayout.Button("Natural"))  { _pitch = 5; _yaw = 8; _roll = 3; ApplyLookAll(); lookChanged = true; }
        if (GUILayout.Button("Medium"))   { _pitch = 8; _yaw = 12; _roll = 4; ApplyLookAll(); lookChanged = true; }
        if (GUILayout.Button("Strong"))   { _pitch = 12; _yaw = 18; _roll = 5; ApplyLookAll(); lookChanged = true; }
        EditorGUILayout.EndHorizontal();

        // Apply to runtime if in Play Mode
        if (lookChanged && isPlaying)
        {
            PushToRuntime(_lookMod);
        }

        // === FULL BODY IK (FEET) ===
        EditorGUILayout.Space(16);
        _showFootIk = EditorGUILayout.Foldout(_showFootIk, "FULL BODY IK (Feet)", true, EditorStyles.foldoutHeader);

        if (_showFootIk && _fullBodyIkSO != null)
        {
            _fullBodyIkSO.Update();
            bool ikChanged = false;

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _footIkWeight = EditorGUILayout.Slider("Foot IK Weight", _footIkWeight, 0f, 1f);
            EditorGUILayout.LabelField("  0 = pure animation, 1 = full IK", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkWeight(_footIkWeight);
                ikChanged = true;
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _footIkPoleWeight = EditorGUILayout.Slider("Foot Rotation Weight", _footIkPoleWeight, 0f, 1f);
            EditorGUILayout.LabelField("  0 = animation rotation, 1 = IK target rotation", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkPoleWeight(_footIkPoleWeight);
                ikChanged = true;
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _footYawOffset = EditorGUILayout.Slider("Foot Yaw Offset", _footYawOffset, -30f, 30f);
            EditorGUILayout.LabelField("  + = toes outward (V), - = toes inward (pigeon)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootYawOffset(_footYawOffset);
                ikChanged = true;
            }

            if (ikChanged && isPlaying)
            {
                PushToRuntime(_fullBodyIk);
            }
        }

        // === FOOT IK TRACE ===
        EditorGUILayout.Space(16);
        _showFootTrace = EditorGUILayout.Foldout(_showFootTrace, "FOOT IK TRACE (Ground Detection)", true, EditorStyles.foldoutHeader);

        if (_showFootTrace && _footIkSO != null)
        {
            _footIkSO.Update();
            bool traceChanged = false;

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _traceOffset = EditorGUILayout.Slider("Trace Offset", _traceOffset, 0.1f, 1.5f);
            EditorGUILayout.LabelField("  How far below foot to search for ground (0.3=short, 0.8=good)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkTrace("traceOffset", _traceOffset);
                traceChanged = true;
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _traceRadius = EditorGUILayout.Slider("Trace Radius", _traceRadius, 0.01f, 0.2f);
            EditorGUILayout.LabelField("  Raycast thickness (0.05=thin, 0.1=stairs-friendly)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkTrace("traceRadius", _traceRadius);
                traceChanged = true;
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _interpSpeed = EditorGUILayout.Slider("Interp Speed", _interpSpeed, 1f, 50f);
            EditorGUILayout.LabelField("  How fast foot matches ground (20=smooth, 0=instant)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkTrace("interpSpeed", _interpSpeed);
                traceChanged = true;
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _groundOffset = EditorGUILayout.Slider("Ground Offset", _groundOffset, -0.05f, 0.05f);
            EditorGUILayout.LabelField("  Negative = feet pushed into ground (-0.01=default)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetFootIkTrace("groundOffset", _groundOffset);
                traceChanged = true;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Default")) { _traceOffset = 0.58f; _traceRadius = 0.03f; _interpSpeed = 15f; _groundOffset = -0.01f; ApplyAllTrace(); traceChanged = true; }
            if (GUILayout.Button("Better Ground")) { _traceOffset = 0.6f; _traceRadius = 0.08f; _interpSpeed = 20f; _groundOffset = -0.015f; ApplyAllTrace(); traceChanged = true; }
            if (GUILayout.Button("Stairs")) { _traceOffset = 0.8f; _traceRadius = 0.1f; _interpSpeed = 15f; _groundOffset = -0.02f; ApplyAllTrace(); traceChanged = true; }
            EditorGUILayout.EndHorizontal();

            if (traceChanged && isPlaying)
            {
                PushToRuntime(_footIk);
            }
        }

        // === STEP MODIFIER ===
        EditorGUILayout.Space(16);
        _showStep = EditorGUILayout.Foldout(_showStep, "STEP MODIFIER (Turn-in-Place)", true, EditorStyles.foldoutHeader);

        if (_showStep && _stepModSO != null)
        {
            _stepModSO.Update();
            bool stepChanged = false;

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _maxOffsetAngle = EditorGUILayout.Slider("Max Offset Angle", _maxOffsetAngle, 15f, 120f);
            EditorGUILayout.LabelField("  Degrees before feet snap during rotation (45=default)", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SetStepValue("maxAllowedOffsetAngle", _maxOffsetAngle);
                stepChanged = true;
            }

            if (stepChanged && isPlaying)
            {
                PushToRuntime(_stepMod);
            }
        }

        // === SAVE ===
        EditorGUILayout.Space(16);
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("Save All to Asset", GUILayout.Height(30)))
        {
            ApplyLookAll();
            if (_fullBodyIkSO != null)
            {
                SetFootIkWeight(_footIkWeight);
                SetFootIkPoleWeight(_footIkPoleWeight);
                SetFootYawOffset(_footYawOffset);
            }
            // Ensure copyRotation is always ON (required for correct IK rotation blending)
            if (_copyRightSO != null) SetCopyRotation(true);
            if (_footIkSO != null) ApplyAllTrace();
            if (_stepModSO != null) SetStepValue("maxAllowedOffsetAngle", _maxOffsetAngle);
            AssetDatabase.SaveAssets();
            Debug.Log("[Tuner] Saved! Look: P=" + _pitch + " Y=" + _yaw + " R=" + _roll +
                      " | FootIK: W=" + _footIkWeight + " RotW=" + _footIkPoleWeight +
                      " | Yaw=" + _footYawOffset +
                      " | Trace: off=" + _traceOffset + " rad=" + _traceRadius +
                      " interp=" + _interpSpeed + " ground=" + _groundOffset +
                      " | Step: angle=" + _maxOffsetAngle);
        }
        GUI.backgroundColor = Color.white;

        if (isPlaying)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Tip: Changes are LIVE during Play Mode.\n" +
                "Click 'Save All' to persist after stopping.",
                MessageType.None);
        }
    }

    void PushToRuntime(AnimationModifierSettings settings)
    {
        if (!Application.isPlaying || settings == null) return;

        var procAnims = Object.FindObjectsByType<ProceduralAnimationComponent>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var pa in procAnims)
        {
            pa.UpdateAnimationModifier(settings);
            count++;
        }

        if (count == 0)
            Debug.LogWarning("[Tuner] No ProceduralAnimationComponent found in scene!");
    }

    void ApplyLookAll()
    {
        SetAllElements("pitchOffsetElements", _pitch);
        SetAllElements("yawOffsetElements", _yaw);
        SetAllElements("rollOffsetElements", _roll);
    }

    void SetAllElements(string arrayName, float value)
    {
        _lookSO.Update();
        var arr = _lookSO.FindProperty(arrayName);
        if (arr == null) return;

        for (int i = 0; i < arr.arraySize; i++)
        {
            var clamped = arr.GetArrayElementAtIndex(i).FindPropertyRelative("clampedAngle");
            var cached = arr.GetArrayElementAtIndex(i).FindPropertyRelative("cachedClampedAngle");
            if (clamped != null) clamped.vector2Value = new Vector2(value, value);
            if (cached != null) cached.vector2Value = new Vector2(value, value);
        }

        _lookSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_lookMod);
    }

    void SetFootIkWeight(float weight)
    {
        _fullBodyIkSO.Update();
        string[] limbs = { "rightFootIk.weight", "leftFootIk.weight" };
        foreach (var path in limbs)
        {
            var prop = _fullBodyIkSO.FindProperty(path);
            if (prop != null) prop.floatValue = weight;
        }
        _fullBodyIkSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_fullBodyIk);
    }

    void SetFootIkPoleWeight(float weight)
    {
        _fullBodyIkSO.Update();
        string[] limbs = { "rightFootIk.poleWeight", "leftFootIk.poleWeight" };
        foreach (var path in limbs)
        {
            var prop = _fullBodyIkSO.FindProperty(path);
            if (prop != null) prop.floatValue = weight;
        }
        _fullBodyIkSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_fullBodyIk);
    }

    void SetFootYawOffset(float offset)
    {
        _fullBodyIkSO.Update();
        var prop = _fullBodyIkSO.FindProperty("footYawOffset");
        if (prop != null) prop.floatValue = offset;
        _fullBodyIkSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_fullBodyIk);
    }

    void SetFootIkTrace(string propName, float value)
    {
        _footIkSO.Update();
        var prop = _footIkSO.FindProperty(propName);
        if (prop != null) prop.floatValue = value;
        _footIkSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_footIk);
    }

    void ApplyAllTrace()
    {
        SetFootIkTrace("traceOffset", _traceOffset);
        SetFootIkTrace("traceRadius", _traceRadius);
        SetFootIkTrace("interpSpeed", _interpSpeed);
        SetFootIkTrace("groundOffset", _groundOffset);
    }

    void SetStepValue(string propName, float value)
    {
        _stepModSO.Update();
        var prop = _stepModSO.FindProperty(propName);
        if (prop != null) prop.floatValue = value;
        _stepModSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(_stepMod);
    }

    void SetCopyRotation(bool enabled)
    {
        if (_copyRightSO != null)
        {
            _copyRightSO.Update();
            var prop = _copyRightSO.FindProperty("copyRotation");
            if (prop != null) prop.boolValue = enabled;
            _copyRightSO.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_copyRightFoot);
        }
        if (_copyLeftSO != null)
        {
            _copyLeftSO.Update();
            var prop = _copyLeftSO.FindProperty("copyRotation");
            if (prop != null) prop.boolValue = enabled;
            _copyLeftSO.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_copyLeftFoot);
        }
    }
}
#endif
