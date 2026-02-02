#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Fixes purple objects by reassigning broken shaders to HDRP/Lit.
/// </summary>
public class HDRPMaterialFixer : EditorWindow
{
    [MenuItem("Tools/AAA Setup/🔴 FIX PURPLE OBJECTS", false, 1)]
    public static void ShowWindow()
    {
        var window = GetWindow<HDRPMaterialFixer>("Fix Purple Objects");
        window.minSize = new Vector2(400, 300);
    }
    
    private void OnGUI()
    {
        EditorGUILayout.Space(15);
        
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField("Fix Purple Objects", titleStyle);
        
        EditorGUILayout.Space(20);
        
        EditorGUILayout.HelpBox(
            "Purple objects = missing or incompatible shader.\n\n" +
            "This tool converts them to HDRP/Lit.", 
            MessageType.Info);
        
        EditorGUILayout.Space(15);
        
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button("Convert All Materials to HDRP", GUILayout.Height(50)))
        {
            ConvertAllMaterials();
        }
        GUI.backgroundColor = Color.white;
        
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Clear Shader Cache", GUILayout.Height(35)))
        {
            ClearShaderCache();
        }
        
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Assign HDRP Lit to Selected Materials", GUILayout.Height(35)))
        {
            AssignHDRPLitToSelected();
        }
    }
    
    private void ConvertAllMaterials()
    {
        // Find all materials in Assets
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int converted = 0;
        int total = materialGuids.Length;
        
        // Get HDRP Lit shader
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null)
        {
            EditorUtility.DisplayDialog("Error", "HDRP/Lit shader not found!", "OK");
            return;
        }

        foreach (var guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            
            if (mat == null) continue;
            
            // Check if shader is missing or error
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader" || mat.shader.name.Contains("Error"))
            {
                mat.shader = hdrpLit;
                EditorUtility.SetDirty(mat);
                converted++;
            }
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("Done",
            $"Scanned {total} materials.\n{converted} converted to HDRP/Lit.",
            "OK");

        Debug.Log($"[MaterialFixer] {converted}/{total} materials converted");
    }
    
    private void ClearShaderCache()
    {
        string libraryPath = Application.dataPath.Replace("/Assets", "/Library");
        string shaderCachePath = System.IO.Path.Combine(libraryPath, "ShaderCache");
        
        if (System.IO.Directory.Exists(shaderCachePath))
        {
            try
            {
                System.IO.Directory.Delete(shaderCachePath, true);
                Debug.Log("[MaterialFixer] ShaderCache cleared");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MaterialFixer] Failed to clear ShaderCache: {e.Message}");
            }
        }
        
        // Force reimport all shaders
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        
        EditorUtility.DisplayDialog("Done",
            "Shader cache cleared. Unity will recompile shaders.",
            "OK");
    }
    
    private void AssignHDRPLitToSelected()
    {
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null)
        {
            EditorUtility.DisplayDialog("Error", "HDRP/Lit shader not found!", "OK");
            return;
        }

        Object[] selected = Selection.objects;
        int count = 0;
        
        foreach (var obj in selected)
        {
            if (obj is Material mat)
            {
                mat.shader = hdrpLit;
                EditorUtility.SetDirty(mat);
                count++;
            }
        }
        
        AssetDatabase.SaveAssets();
        
        EditorUtility.DisplayDialog("Done",
            $"{count} materials converted to HDRP/Lit.",
            "OK");
    }
}
#endif
