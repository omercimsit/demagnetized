using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;

public class CharacterTuningWindow : EditorWindow
{
    private GameObject player;
    private GameObject character;
    private Transform aimSocket;
    private CharacterController charController;
    private Animator animator;
    private string statusMsg = "";
    private Vector2 scroll;

    [MenuItem("Tools/Character Tuning Window")]
    static void Open() { GetWindow<CharacterTuningWindow>("Character Tuning"); }

    void FindReferences()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        player = playerObj;
        character = playerObj != null ? playerObj.transform.Find("3D Game Character")?.gameObject : null;
        if (player != null) charController = player.GetComponent<CharacterController>();
        animator = null; aimSocket = null;
        if (character != null)
        {
            animator = character.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) aimSocket = head.Find("aim_socket");
            }
        }
    }

    void OnEnable() { FindReferences(); }
    void OnFocus() { FindReferences(); }

    void MarkDirty(Object obj)
    {
        EditorUtility.SetDirty(obj);
        if (obj is Component comp)
            EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        else if (obj is GameObject go)
            EditorSceneManager.MarkSceneDirty(go.scene);
        else if (obj is Transform t)
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
    }

    void SetSocket(Vector3 pos)
    {
        if (aimSocket == null) return;
        Undo.RecordObject(aimSocket, "aim_socket");
        aimSocket.localPosition = pos;
        MarkDirty(aimSocket);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (GUILayout.Button("Refresh References", GUILayout.Height(22)))
            FindReferences();

        bool ok = player && character && aimSocket && charController;
        EditorGUILayout.LabelField("Status: " + (ok ? "All OK" : "MISSING refs - click Refresh"));
        EditorGUILayout.Space(8);

        // aim socket position
        EditorGUILayout.LabelField("Camera (aim_socket on Head)", EditorStyles.boldLabel);
        if (aimSocket != null)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 pos = EditorGUILayout.Vector3Field("Local Pos", aimSocket.localPosition);
            if (EditorGUI.EndChangeCheck())
                SetSocket(pos);

            if (GUILayout.Button("Eye Level (0, 0.1, 0.15)"))
                SetSocket(new Vector3(0, 0.1f, 0.15f));
            if (GUILayout.Button("Forehead (0, 0.15, 0.1)"))
                SetSocket(new Vector3(0, 0.15f, 0.1f));

            EditorGUILayout.LabelField("World: " + aimSocket.position.ToString("F3"));
        }
        else EditorGUILayout.HelpBox("aim_socket not found on Head bone", MessageType.Warning);

        EditorGUILayout.Space(8);

        // character controller tweaks
        EditorGUILayout.LabelField("Character Controller", EditorStyles.boldLabel);
        if (charController != null)
        {
            EditorGUI.BeginChangeCheck();
            float h = EditorGUILayout.FloatField("Height", charController.height);
            Vector3 c = EditorGUILayout.Vector3Field("Center", charController.center);
            float r = EditorGUILayout.FloatField("Radius", charController.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(charController, "CC");
                charController.height = h;
                charController.center = c;
                charController.radius = r;
                MarkDirty(charController);
            }
            if (GUILayout.Button("Auto-Fit to Character"))
            {
                if (animator != null && animator.isHuman)
                {
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    if (head && foot)
                    {
                        float height = head.position.y - foot.position.y + 0.15f;
                        Undo.RecordObject(charController, "Auto CC");
                        charController.height = height;
                        charController.center = new Vector3(0, height / 2f, 0);
                        charController.radius = 0.25f;
                        MarkDirty(charController);
                    }
                }
            }
        }

        EditorGUILayout.Space(8);

        // PA asset slot
        EditorGUILayout.LabelField("Procedural Animation", EditorStyles.boldLabel);
        if (character != null)
        {
            var proc = character.GetComponent<ProceduralAnimationComponent>();
            if (proc != null)
            {
                var so = new SerializedObject(proc);
                var prop = so.FindProperty("proceduralSettings");
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(prop, new GUIContent("PA Settings Asset"));
                if (EditorGUI.EndChangeCheck()) so.ApplyModifiedProperties();

                if (prop.objectReferenceValue != null)
                {
                    if (GUILayout.Button("Select PA Asset in Project"))
                    {
                        Selection.activeObject = prop.objectReferenceValue;
                        EditorGUIUtility.PingObject(prop.objectReferenceValue);
                    }
                }
            }
        }

        EditorGUILayout.Space(8);

        // camera settings - uses reflection since CharacterCamera might not be in the assembly
        EditorGUILayout.LabelField("CharacterCamera", EditorStyles.boldLabel);
        if (player != null)
        {
            var camT = player.transform.Find("Camera");
            if (camT != null)
            {
                foreach (var mb in camT.GetComponents<MonoBehaviour>())
                {
                    if (mb.GetType().Name != "CharacterCamera") continue;
                    var so = new SerializedObject(mb);
                    so.Update();
                    AutoToggle(so, "isFirstPerson", "First Person");
                    AutoToggle(so, "useRightShoulder", "Right Shoulder");
                    AutoSlider(so, "viewSmoothing", "View Smoothing", 0, 20);
                    AutoSlider(so, "traceRadius", "Trace Radius", 0, 1);
                    AutoProp(so, "firstPersonSocket", "FP Socket");
                    AutoProp(so, "cameraAnimationSource", "Anim Source");
                }
            }
        }

        EditorGUILayout.Space(8);

        // quick bone measurements
        if (animator != null && animator.isHuman)
        {
            EditorGUILayout.LabelField("Measurements", EditorStyles.boldLabel);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (head && foot) EditorGUILayout.LabelField("Height: " + (head.position.y - foot.position.y).ToString("F2") + "m");
            if (head) EditorGUILayout.LabelField("Head Y: " + head.position.y.ToString("F2"));
            if (hips) EditorGUILayout.LabelField("Hips Y: " + hips.position.y.ToString("F2"));
        }

        EditorGUILayout.Space(5);
        if (!string.IsNullOrEmpty(statusMsg))
            EditorGUILayout.HelpBox(statusMsg, MessageType.Info);

        // FIXME: doesn't work in edit mode - need to check Application.isPlaying before saving
        EditorGUI.BeginDisabledGroup(Application.isPlaying);
        if (GUILayout.Button("Save Scene", GUILayout.Height(28)))
        {
            EditorSceneManager.SaveOpenScenes();
            statusMsg = "Saved!";
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    void AutoToggle(SerializedObject so, string name, string label)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        bool v = EditorGUILayout.Toggle(label, p.boolValue);
        if (EditorGUI.EndChangeCheck()) { p.boolValue = v; so.ApplyModifiedProperties(); }
    }

    void AutoSlider(SerializedObject so, string name, string label, float min, float max)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        float v = EditorGUILayout.Slider(label, p.floatValue, min, max);
        if (EditorGUI.EndChangeCheck()) { p.floatValue = v; so.ApplyModifiedProperties(); }
    }

    void AutoProp(SerializedObject so, string name, string label)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(p, new GUIContent(label));
        if (EditorGUI.EndChangeCheck()) so.ApplyModifiedProperties();
    }
}
