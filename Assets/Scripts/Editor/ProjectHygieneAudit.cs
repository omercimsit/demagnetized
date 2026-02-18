using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProjectHygieneAudit
{
    // these three scenes in this order - if the build settings drift from this we'll know
    private static readonly string[] CanonicalBuildFlow =
    {
        "Assets/_Project/Scenes/MainMenu.unity",
        "Assets/_Project/Scenes/AbandonedFactory.unity",
        "Assets/_Project/Scenes/HDRP_TheCarnival.unity"
    };

    [MenuItem("Tools/CloneGame/Hygiene/Validate Build Flow")]
    public static void ValidateBuildFlow()
    {
        var configured = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        bool matches = configured.SequenceEqual(CanonicalBuildFlow);
        if (matches)
        {
            Debug.Log("[HygieneAudit] Build flow is aligned with canonical scene order.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[HygieneAudit] Build flow mismatch detected.");
        sb.AppendLine("Current:");
        foreach (var item in configured) sb.AppendLine(" - " + item);
        sb.AppendLine("Expected:");
        foreach (var item in CanonicalBuildFlow) sb.AppendLine(" - " + item);
        Debug.LogWarning(sb.ToString());
    }

    [MenuItem("Tools/CloneGame/Hygiene/Generate Asset Hygiene Report")]
    public static void GenerateAssetHygieneReport()
    {
        Directory.CreateDirectory("Assets/_Project/Reports");
        var reportPath = "Assets/_Project/Reports/AssetHygieneReport.txt";

        var scenePaths = AssetDatabase.FindAssets("t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();

        var duplicateSceneNames = scenePaths
            .GroupBy(Path.GetFileNameWithoutExtension)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToArray();

        var recoveryScenes = scenePaths.Where(p => p.StartsWith("Assets/_Recovery/", StringComparison.OrdinalIgnoreCase)).ToArray();
        var demoScenes = scenePaths.Where(p => p.IndexOf("/Demo/", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("CLONEPROJECT1 - Asset Hygiene Report");
        sb.AppendLine($"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("Canonical Build Flow:");
        foreach (var scene in CanonicalBuildFlow) sb.AppendLine(" - " + scene);
        sb.AppendLine();

        sb.AppendLine($"Total Scenes: {scenePaths.Length}");
        sb.AppendLine($"Recovery Scenes: {recoveryScenes.Length}");
        sb.AppendLine($"Demo Scenes: {demoScenes.Length}");
        sb.AppendLine($"Duplicate Scene Name Groups: {duplicateSceneNames.Length}");
        sb.AppendLine();

        sb.AppendLine("Duplicate Scene Name Groups:");
        foreach (var group in duplicateSceneNames)
        {
            sb.AppendLine($" * {group.Key} ({group.Count()})");
            foreach (var path in group) sb.AppendLine("   - " + path);
        }
        sb.AppendLine();

        sb.AppendLine("Recovery Scenes:");
        foreach (var path in recoveryScenes) sb.AppendLine(" - " + path);
        sb.AppendLine();

        sb.AppendLine("Demo Scenes:");
        foreach (var path in demoScenes) sb.AppendLine(" - " + path);

        File.WriteAllText(reportPath, sb.ToString());
        AssetDatabase.Refresh();

        Debug.Log($"[HygieneAudit] Report generated: {reportPath}");
    }

    [MenuItem("Tools/CloneGame/Hygiene/Audit Active Scene Missing Scripts")]
    public static void AuditActiveSceneMissingScripts()
    {
        Directory.CreateDirectory("Assets/_Project/Reports");
        const string reportPath = "Assets/_Project/Reports/MissingScripts_ActiveScene.txt";

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogWarning("[HygieneAudit] No active scene found.");
            return;
        }

        int totalMissing = 0;
        var report = new StringBuilder();
        report.AppendLine($"[HygieneAudit] Missing scripts scan for scene: {scene.path}");

        foreach (var root in scene.GetRootGameObjects())
            totalMissing += CollectMissingScriptData(root.transform, report);

        if (totalMissing == 0)
        {
            Debug.Log("[HygieneAudit] No missing script references found in active scene.");
            File.WriteAllText(reportPath, $"Scene: {scene.path}\nMissing scripts: 0");
            AssetDatabase.Refresh();
            return;
        }

        report.AppendLine($"Total missing script references: {totalMissing}");
        Debug.LogWarning(report.ToString());
        File.WriteAllText(reportPath, report.ToString());
        AssetDatabase.Refresh();
    }

    // TODO: this opens each scene one by one which is slow for large projects, maybe parallelize later
    [MenuItem("Tools/CloneGame/Hygiene/Audit Build Scenes Missing Scripts")]
    public static void AuditBuildScenesMissingScripts()
    {
        Directory.CreateDirectory("Assets/_Project/Reports");
        const string reportPath = "Assets/_Project/Reports/MissingScripts_BuildScenes.txt";

        var enabledScenes = EditorBuildSettings.scenes;
        var sceneWarnings = new StringBuilder();
        int total = 0;

        for (int i = 0; i < enabledScenes.Length; i++)
        {
            if (!enabledScenes[i].enabled) continue;
            var loaded = EditorSceneManager.OpenScene(enabledScenes[i].path, OpenSceneMode.Single);
            int missingInScene = 0;
            foreach (var root in loaded.GetRootGameObjects())
                missingInScene += CountMissingScriptsRecursive(root.transform);

            if (missingInScene > 0)
                sceneWarnings.AppendLine($" - {enabledScenes[i].path}: {missingInScene}");

            total += missingInScene;
        }

        if (total == 0)
        {
            Debug.Log("[HygieneAudit] No missing scripts found across enabled build scenes.");
            File.WriteAllText(reportPath, "Missing scripts across enabled build scenes: 0");
            AssetDatabase.Refresh();
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[HygieneAudit] Missing scripts detected in build scenes:");
        sb.Append(sceneWarnings);
        sb.AppendLine("Total missing script references: " + total);
        Debug.LogWarning(sb.ToString());
        File.WriteAllText(reportPath, sb.ToString());
        AssetDatabase.Refresh();
    }

    private static int CollectMissingScriptData(Transform node, StringBuilder report)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
        if (count > 0)
            report.AppendLine($" - {node.gameObject.name}: {count}");

        for (int i = 0; i < node.childCount; i++)
            count += CollectMissingScriptData(node.GetChild(i), report);

        return count;
    }

    private static int CountMissingScriptsRecursive(Transform node)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
        for (int i = 0; i < node.childCount; i++)
            count += CountMissingScriptsRecursive(node.GetChild(i));
        return count;
    }
}
