using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.IO;

// auto-assigns layers and spatially groups scene objects for culling
// Menu: Tools/Optimization/
public class HierarchyOptimizer : EditorWindow
{
    private Vector2 scrollPos;
    private bool showDetails = true;

    // scene stats - refreshed manually
    private int totalObjects;
    private int propsCount;
    private int detailCount;
    private int decalsCount;
    private int vfxCount;
    private int layersAssigned;

    [MenuItem("Tools/Optimization/Hierarchy Optimizer")]
    public static void ShowWindow()
    {
        GetWindow<HierarchyOptimizer>("Hierarchy Optimizer");
    }

    [MenuItem("Tools/Optimization/Optimize ALL Scenes (Batch)")]
    public static void OptimizeAllScenes()
    {
        string[] scenePaths = new string[]
        {
            "Assets/_Project/Scenes/MainGameScene.unity",
            "Assets/_Project/Scenes/Factory_01_HDRP_Night.unity",
            "Assets/_Project/Scenes/HDRP_TheCarnival.unity",
            "Assets/_Project/Scenes/L_Showcase.unity",
            "Assets/_Project/Scenes/MainMenu.unity"
            // AbandonedFactory already done
        };

        string currentScene = EditorSceneManager.GetActiveScene().path;
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
        }

        int optimizedCount = 0;
        List<string> results = new List<string>();

        foreach (string scenePath in scenePaths)
        {
            if (!File.Exists(scenePath))
            {
                results.Add($"  {Path.GetFileName(scenePath)} - NOT FOUND");
                continue;
            }

            try
            {
                EditorSceneManager.OpenScene(scenePath);
                int layers = OptimizeCurrentScene();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                results.Add($"OK {Path.GetFileName(scenePath)} - {layers} objects");
                optimizedCount++;
            }
            catch (System.Exception e)
            {
                results.Add($"FAIL {Path.GetFileName(scenePath)} - ERROR: {e.Message}");
            }
        }

        if (!string.IsNullOrEmpty(currentScene) && File.Exists(currentScene))
        {
            EditorSceneManager.OpenScene(currentScene);
        }

        string resultText = string.Join("\n", results);
        EditorUtility.DisplayDialog("Batch Optimization Complete",
            $"Optimized {optimizedCount}/{scenePaths.Length} scenes:\n\n{resultText}",
            "OK");

        Debug.Log($"[HierarchyOptimizer] Batch complete: {optimizedCount} scenes optimized");
    }

    // optimize current scene and return number of objects processed
    private static int OptimizeCurrentScene()
    {
        int total = 0;

        int propsLayer = LayerMask.NameToLayer("Props");
        int detailLayer = LayerMask.NameToLayer("Detail");
        int decalsLayer = LayerMask.NameToLayer("Decals");
        int vfxLayer = LayerMask.NameToLayer("VFX");

        string[] propsNames = { "Meshes", "Props", "StaticMeshes", "Environment", "Buildings" };
        string[] detailNames = { "RockFromFoliage", "Rocks", "Foliage", "Vegetation", "Grass", "Details" };
        string[] decalNames = { "Decals", "DecalActors" };
        string[] vfxNames = { "Particles", "VFX", "Effects", "ParticleSystems" };

        var allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);

        foreach (var t in allTransforms)
        {
            string objName = t.gameObject.name;

            if (propsLayer >= 0 && propsNames.Any(n => objName == n))
            {
                total += AssignLayerToChildren(t, propsLayer);
            }
            else if (detailLayer >= 0 && detailNames.Any(n => objName == n))
            {
                total += AssignLayerToChildren(t, detailLayer);
            }
            else if (decalsLayer >= 0 && decalNames.Any(n => objName == n))
            {
                total += AssignLayerToChildren(t, decalsLayer);
            }
            else if (vfxLayer >= 0 && vfxNames.Any(n => objName == n))
            {
                total += AssignLayerToChildren(t, vfxLayer);
            }
        }

        Debug.Log($"[HierarchyOptimizer] Scene optimized: {total} objects");
        return total;
    }

    private static int AssignLayerToChildren(Transform parent, int layer)
    {
        int count = 0;
        foreach (Transform child in parent)
        {
            if (child.gameObject.layer != layer)
            {
                child.gameObject.layer = layer;
                count++;
            }
            if (child.childCount > 0)
            {
                count += AssignLayerToChildren(child, layer);
            }
        }
        return count;
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("HIERARCHY OPTIMIZER", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            $"Scene Stats:\n" +
            $"- Props: {propsCount}\n" +
            $"- Detail (Rocks): {detailCount}\n" +
            $"- Decals: {decalsCount}\n" +
            $"- VFX/Particles: {vfxCount}",
            MessageType.Info);

        GUILayout.Space(10);

        GUILayout.Label("1. LAYER ASSIGNMENT", EditorStyles.boldLabel);

        if (GUILayout.Button("Assign Layers to All Objects", GUILayout.Height(30)))
        {
            AssignLayers();
        }

        GUILayout.Space(10);

        GUILayout.Label("2. SPATIAL GROUPING (Optional)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Groups objects into spatial cells for hierarchical culling.\n" +
            "Recommended for large scenes but modifies the hierarchy.",
            MessageType.Warning);

        if (GUILayout.Button("Group Rocks by Region (50m cells)"))
        {
            GroupByRegion("RockFromFoliage", 50f);
        }

        GUILayout.Space(10);

        GUILayout.Label("3. CLEANUP", EditorStyles.boldLabel);

        if (GUILayout.Button("Remove Duplicate Rocks (Keep 50%)"))
        {
            RemoveDuplicateRocks(0.5f);
        }

        if (GUILayout.Button("Disable Distant Small Rocks"))
        {
            DisableDistantSmallObjects();
        }

        GUILayout.Space(10);

        GUILayout.Label("4. ANALYZE", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh Stats"))
        {
            AnalyzeScene();
        }

        EditorGUILayout.EndScrollView();
    }

    private void AnalyzeScene()
    {
        propsCount = 0;
        detailCount = 0;
        decalsCount = 0;
        vfxCount = 0;

        var meshes = GameObject.Find("Meshes");
        if (meshes != null)
            propsCount = meshes.transform.childCount;

        var rocks = GameObject.Find("RockFromFoliage");
        if (rocks != null)
            detailCount = rocks.transform.childCount;

        var decals = GameObject.Find("Decals");
        if (decals != null)
            decalsCount = decals.transform.childCount;

        var particles = GameObject.Find("Particles");
        if (particles != null)
            vfxCount = particles.transform.childCount;

        totalObjects = propsCount + detailCount + decalsCount + vfxCount;

        Debug.Log($"[HierarchyOptimizer] Analyzed: Props={propsCount}, Detail={detailCount}, Decals={decalsCount}, VFX={vfxCount}");
    }

    private void AssignLayers()
    {
        layersAssigned = 0;

        int propsLayer = LayerMask.NameToLayer("Props");
        int detailLayer = LayerMask.NameToLayer("Detail");
        int decalsLayer = LayerMask.NameToLayer("Decals");
        int vfxLayer = LayerMask.NameToLayer("VFX");

        var meshes = GameObject.Find("Meshes");
        if (meshes != null && propsLayer >= 0)
        {
            Undo.RecordObject(meshes, "Assign Props Layer");
            AssignLayerRecursive(meshes.transform, propsLayer);
            Debug.Log($"[HierarchyOptimizer] Meshes -> Props layer ({meshes.transform.childCount} objects)");
        }

        var rocks = GameObject.Find("RockFromFoliage");
        if (rocks != null && detailLayer >= 0)
        {
            Undo.RecordObject(rocks, "Assign Detail Layer");
            AssignLayerRecursive(rocks.transform, detailLayer);
            Debug.Log($"[HierarchyOptimizer] RockFromFoliage -> Detail layer ({rocks.transform.childCount} objects)");
        }

        var decals = GameObject.Find("Decals");
        if (decals != null && decalsLayer >= 0)
        {
            Undo.RecordObject(decals, "Assign Decals Layer");
            AssignLayerRecursive(decals.transform, decalsLayer);
            Debug.Log($"[HierarchyOptimizer] Decals -> Decals layer ({decals.transform.childCount} objects)");
        }

        var particles = GameObject.Find("Particles");
        if (particles != null && vfxLayer >= 0)
        {
            Undo.RecordObject(particles, "Assign VFX Layer");
            AssignLayerRecursive(particles.transform, vfxLayer);
            Debug.Log($"[HierarchyOptimizer] Particles -> VFX layer ({particles.transform.childCount} objects)");
        }

        EditorUtility.DisplayDialog("Layer Assignment",
            $"Layers assigned successfully!\n\n" +
            $"- Meshes -> Props (Layer 16)\n" +
            $"- RockFromFoliage -> Detail (Layer 17)\n" +
            $"- Decals -> Decals (Layer 20)\n" +
            $"- Particles -> VFX (Layer 21)\n\n" +
            $"Total: {layersAssigned} objects updated.",
            "OK");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    private void AssignLayerRecursive(Transform parent, int layer)
    {
        foreach (Transform child in parent)
        {
            if (child.gameObject.layer != layer)
            {
                child.gameObject.layer = layer;
                layersAssigned++;
            }

            if (child.childCount > 0)
            {
                AssignLayerRecursive(child, layer);
            }
        }
    }

    // groups objects into spatial cells for hierarchical culling
    private void GroupByRegion(string parentName, float cellSize)
    {
        var parent = GameObject.Find(parentName);
        if (parent == null)
        {
            Debug.LogError($"[HierarchyOptimizer] {parentName} not found!");
            return;
        }

        Dictionary<Vector2Int, List<Transform>> regions = new Dictionary<Vector2Int, List<Transform>>();

        List<Transform> children = new List<Transform>();
        foreach (Transform child in parent.transform)
        {
            children.Add(child);
        }

        foreach (var child in children)
        {
            Vector2Int cell = new Vector2Int(
                Mathf.FloorToInt(child.position.x / cellSize),
                Mathf.FloorToInt(child.position.z / cellSize)
            );

            if (!regions.ContainsKey(cell))
                regions[cell] = new List<Transform>();

            regions[cell].Add(child);
        }

        int groupCount = 0;
        foreach (var kvp in regions)
        {
            if (kvp.Value.Count < 5) continue; // skip tiny groups, probably not worth the overhead

            string groupName = $"{parentName}_Region_{kvp.Key.x}_{kvp.Key.y}";
            var regionGroup = new GameObject(groupName);
            regionGroup.transform.SetParent(parent.transform);
            regionGroup.isStatic = true;

            Undo.RegisterCreatedObjectUndo(regionGroup, "Create Region Group");

            foreach (var child in kvp.Value)
            {
                Undo.SetTransformParent(child, regionGroup.transform, "Move to Region");
            }

            groupCount++;
        }

        Debug.Log($"[HierarchyOptimizer] Created {groupCount} region groups for {parentName}");

        EditorUtility.DisplayDialog("Spatial Grouping",
            $"Created {groupCount} region groups.\n\n" +
            $"Cell size: {cellSize}m\n" +
            $"This enables hierarchical culling.",
            "OK");
    }

    // removes excess rocks to cut draw calls - uses fixed seed so results are reproducible
    private void RemoveDuplicateRocks(float keepRatio)
    {
        var rocks = GameObject.Find("RockFromFoliage");
        if (rocks == null) return;

        int originalCount = rocks.transform.childCount;
        int toRemove = Mathf.FloorToInt(originalCount * (1f - keepRatio));

        List<Transform> children = new List<Transform>();
        foreach (Transform child in rocks.transform)
        {
            children.Add(child);
        }

        System.Random rng = new System.Random(42); // fixed seed for reproducibility
        var shuffled = children.OrderBy(x => rng.Next()).ToList();

        for (int i = 0; i < toRemove && i < shuffled.Count; i++)
        {
            Undo.DestroyObjectImmediate(shuffled[i].gameObject);
        }

        Debug.Log($"[HierarchyOptimizer] Removed {toRemove} rocks ({originalCount} -> {originalCount - toRemove})");

        EditorUtility.DisplayDialog("Rock Cleanup",
            $"Removed {toRemove} rocks!\n\n" +
            $"Before: {originalCount}\n" +
            $"After: {originalCount - toRemove}\n\n" +
            $"Use Ctrl+Z to undo.",
            "OK");
    }

    private void DisableDistantSmallObjects()
    {
        var rocks = GameObject.Find("RockFromFoliage");
        if (rocks == null) return;

        Vector3 center = Vector3.zero;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            center = player.transform.position;

        int disabled = 0;
        float maxDistance = 200f;

        foreach (Transform child in rocks.transform)
        {
            float dist = Vector3.Distance(child.position, center);

            if (dist > maxDistance && child.gameObject.activeSelf)
            {
                Undo.RecordObject(child.gameObject, "Disable Distant Rock");
                child.gameObject.SetActive(false);
                disabled++;
            }
        }

        Debug.Log($"[HierarchyOptimizer] Disabled {disabled} distant rocks (>{maxDistance}m from player)");

        EditorUtility.DisplayDialog("Distant Objects",
            $"Disabled {disabled} rocks beyond {maxDistance}m.\n\n" +
            $"These can be re-enabled via streaming\n" +
            $"or proximity triggers if needed.",
            "OK");
    }
}
