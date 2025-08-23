#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Editor utility for backing up and restoring ScriptableObject assets as JSON files.
/// Creates one JSON per asset, named by its GUID, so references remain stable even
/// if assets are moved or renamed. Backup/restore mirrors Unity's Inspector
/// serialization via EditorJsonUtility, preserving [SerializeField], lists, enums,
/// and asset references (guid/fileID).
/// 
/// GitHub repo: https://github.com/Ryadel/SOBackup/
/// demo and examples: https://www.ryadel.com/en/SOBackup-backup-restore-scriptableobjects-json-unity-editor/
/// </summary>
/// <remarks>
/// - Usage (Editor menu):
///     - Tools/SOBackup/Backup
///     - Tools/SOBackup/Restore
/// - Place this script under any "Editor" folder (or an editor-only asmdef).
/// - Restore overwrites values on existing assets and marks them dirty; it does not create new assets.
/// - Non-serialized members (static, [NonSerialized], computed properties) are not included.
/// - Optional field-name remapping (oldKey → newKey) can be applied during restore to survive refactors 
/// without relying solely on [FormerlySerializedAs].
/// </remarks>
/// <example>
/// // Backup all ScriptableObjects found under the selected folders (or entire Assets)
/// // Menu: Tools/Backup ScriptableObjects to JSON…
///
/// // Restore values from a folder of {GUID}.json files into existing assets
/// // Menu: Tools/Restore ScriptableObjects from JSON…
/// </example>
public static class SOBackup
{
    private const string DefaultBackupFolderName = "_SOBackup";

    [MenuItem("Tools/SOBackup/Backup")]
    public static void BackupAllSOToJson()
    {
        // Decide input folders: selected folders or entire Assets
        string[] inputFolders = GetSelectedProjectFoldersOrAssetsRoot();

        // Choose output folder outside Assets (recommended) or inside project
        string outputFolder = EditorUtility.SaveFolderPanel(
            "Choose backup output folder",
            Application.dataPath + "/..",
            DefaultBackupFolderName
        );
        if (string.IsNullOrEmpty(outputFolder)) return;

        var guids = AssetDatabase.FindAssets("t:ScriptableObject", inputFolders);
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No ScriptableObjects found in the selected scope.", "OK");
            return;
        }

        int count = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null) continue;

                EditorUtility.DisplayProgressBar("Backing up ScriptableObjects", path, (float)i / guids.Length);

                // Serialize to JSON (Inspector-accurate)
                string json = EditorJsonUtility.ToJson(so, prettyPrint: true);

                // Save as {guid}.json to guarantee correct mapping on restore
                string filePath = Path.Combine(outputFolder, guid + ".json");
                File.WriteAllText(filePath, json);
                count++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog("SOBackup", $"Backed up {count} ScriptableObjects to:\n{outputFolder}", "OK");
        Debug.Log($"[SOBackup] Backed up {count} assets to {outputFolder}");
    }

    [MenuItem("Tools/SOBackup/Restore")]
    public static void RestoreAllSOFromJson()
    {
        string inputFolder = EditorUtility.OpenFolderPanel("Choose folder containing JSON backups", Application.dataPath + "/..", DefaultBackupFolderName);
        if (string.IsNullOrEmpty(inputFolder)) return;

        // Optional: define key renames (oldFieldName -> newFieldName)
        // Edit this dictionary as needed before running restore.
        var keyRenames = new Dictionary<string, string>()
        {
            // Example:
            // { "damage", "baseDamage" },
            // { "rangeMeters", "range" },
        };

        var files = Directory.GetFiles(inputFolder, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No .json files found in the selected folder.", "OK");
            return;
        }

        int restored = 0;
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                EditorUtility.DisplayProgressBar("Restoring ScriptableObjects", Path.GetFileName(file), (float)i / files.Length);

                string guid = Path.GetFileNameWithoutExtension(file);
                if (!IsGuid(guid))
                {
                    Debug.LogWarning($"[SOBackup] Skipping JSON without GUID filename: {file}");
                    continue;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogWarning($"[SOBackup] No asset found for GUID {guid}. Skipping {file}");
                    continue;
                }

                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so == null)
                {
                    Debug.LogWarning($"[SOBackup] Asset at path {assetPath} is not a ScriptableObject. Skipping.");
                    continue;
                }

                string json = File.ReadAllText(file);

                // Apply field-name remapping if provided
                if (keyRenames != null && keyRenames.Count > 0)
                    json = ApplyKeyRenames(json, keyRenames);

                // Overwrite values into the existing asset
                EditorJsonUtility.FromJsonOverwrite(json, so);
                EditorUtility.SetDirty(so);
                restored++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("SOBackup", $"Restored {restored} ScriptableObjects from:\n{inputFolder}", "OK");
        Debug.Log($"[SOBackup] Restored {restored} assets from {inputFolder}");
    }

    private static string[] GetSelectedProjectFoldersOrAssetsRoot()
    {
        var selected = Selection.assetGUIDs;
        if (selected != null && selected.Length > 0)
        {
            var folders = new List<string>();
            foreach (var guid in selected)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    folders.Add(path);
            }
            if (folders.Count > 0) return folders.ToArray();
        }
        // default: whole Assets
        return new[] { "Assets" };
    }

    private static bool IsGuid(string s)
    {
        // Unity GUID is 32 hex chars (no dashes)
        return !string.IsNullOrEmpty(s) && s.Length == 32 && s.All(c => "0123456789abcdef".Contains(char.ToLowerInvariant(c)));
    }

    // Safely rename JSON keys: replaces "oldKey": with "newKey":
    private static string ApplyKeyRenames(string json, Dictionary<string, string> map)
    {
        foreach (var kv in map)
        {
            string oldKey = kv.Key;
            string newKey = kv.Value;
            // Match "oldKey" (with optional spaces) followed by colon
            var pattern = $"\"{Regex.Escape(oldKey)}\"\\s*:";
            var replacement = $"\"{newKey}\":";
            json = Regex.Replace(json, pattern, replacement);
        }
        return json;
    }
}
#endif
