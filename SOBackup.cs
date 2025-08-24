#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;

/// <summary>
/// Editor utility for backing up and restoring ScriptableObject assets as JSON files.
/// Creates one JSON per asset, now named as {AssetName}__{GUID}.json for readability,
/// while still keeping the GUID in the filename to preserve stable references.
/// Backup is limited (and configurable) to a root folder (default: Assets/Resources/ScriptableObject).
/// Uses EditorJsonUtility to mirror Inspector serialization.
/// 
/// GitHub repo: https://github.com/Ryadel/SOBackup/
/// demo and examples: https://www.ryadel.com/en/sobackup-backup-restore-scriptableobjects-json-unity-editor/
/// </summary>
public static class SOBackup
{
    /// <summary>
    /// Default folder name suggested when saving backups.
    /// </summary>
    private const string DefaultBackupFolderName = "_SOBackup";

    /// <summary>
    /// Default root folder (inside Assets) scanned during backup.
    /// </summary>
    private const string DefaultBackupRootFolder = "Assets/Resources/ScriptableObject";

    /// <summary>
    /// EditorPrefs key storing the backup root folder path.
    /// </summary>
    private const string BackupRootEditorPrefsKey = "SOBackup.BackupRootFolder";

    /// <summary>
    /// EditorPrefs key to remember the last backup output folder used from UI.
    /// </summary>
    private const string LastOutputFolderEditorPrefsKey = "SOBackup.LastOutputFolder";

    /// <summary>
    /// EditorPrefs key to remember the last backup input folder used for restore.
    /// </summary>
    private const string LastInputFolderEditorPrefsKey  = "SOBackup.LastInputFolder";

    /// <summary>
    /// Backs up all ScriptableObjects found under the configured root folder to a chosen directory.
    /// Each asset is serialized via EditorJsonUtility and saved as {AssetName}__{GUID}.json.
    /// </summary>
    [MenuItem("Tools/SOBackup/Backup")]
    public static void BackupAllSOToJson()
    {
        string[] inputFolders = new[] { GetBackupRootFolder() };

        string suggested = EditorPrefs.GetString(LastOutputFolderEditorPrefsKey, Application.dataPath + "/..");
        string outputFolder = EditorUtility.SaveFolderPanel(
            "Choose backup output folder",
            suggested,
            DefaultBackupFolderName
        );
        if (string.IsNullOrEmpty(outputFolder)) return;
        EditorPrefs.SetString(LastOutputFolderEditorPrefsKey, outputFolder);

        var guids = AssetDatabase.FindAssets("t:ScriptableObject", inputFolders);
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", $"No ScriptableObjects found under:\n{inputFolders[0]}", "OK");
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

                // Save as {AssetName}__{GUID}.json (readable + stable mapping on restore)
                string fileName = BuildBackupFileName(guid, so, path);
                string filePath = Path.Combine(outputFolder, fileName);
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

    /// <summary>
    /// Restores ScriptableObjects by loading JSON files from a chosen folder and overwriting values
    /// into existing assets matched by GUID in the filename. Supports both {GUID}.json and {Name}__{GUID}.json.
    /// Optional key remapping can be applied before deserialization.
    /// </summary>
    [MenuItem("Tools/SOBackup/Restore")]
    public static void RestoreAllSOFromJson()
    {
        string suggested = EditorPrefs.GetString(LastInputFolderEditorPrefsKey, Application.dataPath + "/..");
        string inputFolder = EditorUtility.OpenFolderPanel("Choose folder containing JSON backups", suggested, DefaultBackupFolderName);
        if (string.IsNullOrEmpty(inputFolder)) return;
        EditorPrefs.SetString(LastInputFolderEditorPrefsKey, inputFolder);

        /// <summary>
        /// Optional map of field renames to apply to JSON before restore (oldFieldName -> newFieldName).
        /// </summary>
        var keyRenames = new Dictionary<string, string>()
        {
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

                // Supports both {GUID}.json and {AnyPrefix}__{GUID}.json (case-insensitive)
                string nameNoExt = Path.GetFileNameWithoutExtension(file);
                if (!TryExtractGuidFromFileName(nameNoExt, out string guid))
                {
                    Debug.LogWarning($"[SOBackup] Skipping JSON without a GUID in filename: {file}");
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

    /// <summary>
    /// Opens a folder picker and stores the selected Assets-relative folder as the backup scan root in EditorPrefs.
    /// Defaults the picker near Assets/Resources when available.
    /// </summary>
    [MenuItem("Tools/SOBackup/Set Backup Root Folder…")]
    public static void SetBackupRootFolderMenu()
    {
        string startFolder = Directory.Exists(Path.Combine(Application.dataPath, "Resources"))
            ? Path.Combine(Application.dataPath, "Resources")
            : Application.dataPath;

        string picked = EditorUtility.OpenFolderPanel("Select backup root folder (inside this project's Assets)", startFolder, "");
        if (string.IsNullOrEmpty(picked)) return;

        string relative = MakeAssetsRelativePath(picked);
        if (string.IsNullOrEmpty(relative) || !AssetDatabase.IsValidFolder(relative))
        {
            EditorUtility.DisplayDialog("SOBackup", "Please select a folder inside this project's Assets directory.", "OK");
            return;
        }

        EditorPrefs.SetString(BackupRootEditorPrefsKey, relative.Replace('\\', '/'));
        EditorUtility.DisplayDialog("SOBackup", $"Backup root set to:\n{relative}", "OK");
        Debug.Log($"[SOBackup] Backup root set to {relative}");
    }

    // -------- Project window context menu: one‑click backup/restore for selected SOs --------

    /// <summary>
    /// Validate backup context action for selection.
    /// </summary>
    [MenuItem("Assets/SOBackup/Backup Selected SO(s) to JSON…", true)]
    private static bool Validate_BackupSelected()
    {
        return GetSelectedScriptableObjects().Any();
    }

    /// <summary>
    /// Backs up the currently selected ScriptableObjects to a chosen folder using the standard filename convention.
    /// Supports multi-selection and shows a progress bar for feedback.
    /// </summary>
    [MenuItem("Assets/SOBackup/Backup Selected SO(s) to JSON…")]
    private static void BackupSelectedSOToJson()
    {
        var selected = GetSelectedScriptableObjects().ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No ScriptableObject selected.", "OK");
            return;
        }

        string suggested = EditorPrefs.GetString(LastOutputFolderEditorPrefsKey, Application.dataPath + "/..");
        string outputFolder = EditorUtility.SaveFolderPanel("Choose backup output folder", suggested, DefaultBackupFolderName);
        if (string.IsNullOrEmpty(outputFolder)) return;
        EditorPrefs.SetString(LastOutputFolderEditorPrefsKey, outputFolder);

        int count = 0;
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var (guid, path, so) = selected[i];
                if (so == null) continue;

                EditorUtility.DisplayProgressBar("Backing up ScriptableObject", path, (float)i / selected.Count);

                string json = EditorJsonUtility.ToJson(so, prettyPrint: true);
                string fileName = BuildBackupFileName(guid, so, path);
                File.WriteAllText(Path.Combine(outputFolder, fileName), json);
                count++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog("SOBackup", $"Backed up {count} ScriptableObject(s) to:\n{outputFolder}", "OK");
        Debug.Log($"[SOBackup] Backed up {count} selected assets to {outputFolder}");
    }

    /// <summary>
    /// Validation handler for the "Restore Selected SO(s) from JSON Folder…" menu; enabled only when at least one ScriptableObject is selected.
    /// </summary>
    [MenuItem("Assets/SOBackup/Restore Selected SO(s) from JSON Folder…", true)]
    private static bool Validate_RestoreSelected()
    {
        return GetSelectedScriptableObjects().Any();
    }

    /// <summary>
    /// Restores the currently selected ScriptableObjects by matching their GUIDs to JSON files in a chosen folder.
    /// Works with {GUID}.json and {Name}__{GUID}.json. Shows a progress bar and saves assets when done.
    /// </summary>
    [MenuItem("Assets/SOBackup/Restore Selected SO(s) from JSON Folder…")]
    private static void RestoreSelectedSOFromJsonFolder()
    {
        var selected = GetSelectedScriptableObjects().ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No ScriptableObject selected.", "OK");
            return;
        }

        string suggested = EditorPrefs.GetString(LastInputFolderEditorPrefsKey, Application.dataPath + "/..");
        string inputFolder = EditorUtility.OpenFolderPanel("Pick folder containing JSON backups", suggested, DefaultBackupFolderName);
        if (string.IsNullOrEmpty(inputFolder)) return;
        EditorPrefs.SetString(LastInputFolderEditorPrefsKey, inputFolder);

        var files = Directory.GetFiles(inputFolder, "*.json", SearchOption.TopDirectoryOnly);
        var guidToFile = new Dictionary<string, string>();
        foreach (var file in files)
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(file);
            if (TryExtractGuidFromFileName(nameNoExt, out string guid) && !guidToFile.ContainsKey(guid))
                guidToFile[guid] = file;
        }

        if (guidToFile.Count == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No .json files with GUID in the filename were found in the folder.", "OK");
            return;
        }

        int restored = 0;
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var (guid, path, so) = selected[i];
                EditorUtility.DisplayProgressBar("Restoring ScriptableObject", path, (float)i / selected.Count);

                if (string.IsNullOrEmpty(guid) || so == null || !guidToFile.TryGetValue(guid, out string file))
                {
                    Debug.LogWarning($"[SOBackup] No JSON found for {path} (GUID {guid}).");
                    continue;
                }

                string json = File.ReadAllText(file);
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
        EditorUtility.DisplayDialog("SOBackup", $"Restored {restored}/{selected.Count} ScriptableObject(s) from:\n{inputFolder}", "OK");
        Debug.Log($"[SOBackup] Restored {restored}/{selected.Count} selected assets from {inputFolder}");
    }

    // -------- SODiff: compare selected SO(s) vs JSON backup --------

    /// <summary>
    /// Validate diff context action for selection.
    /// </summary>
    [MenuItem("Assets/SOBackup/Diff Selected SO(s) with JSON…", true)]
    private static bool Validate_DiffSelected()
    {
        return GetSelectedScriptableObjects().Any();
    }

    /// <summary>
    /// Diffs each selected ScriptableObject against its JSON backup found in a chosen folder.
    /// Shows differences per serialized property (current value vs backup value).
    /// </summary>
    [MenuItem("Assets/SOBackup/Diff Selected SO(s) with JSON…")]
    private static void DiffSelectedSOWithJson()
    {
        var selected = GetSelectedScriptableObjects().ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No ScriptableObject selected.", "OK");
            return;
        }

        string suggested = EditorPrefs.GetString(LastInputFolderEditorPrefsKey, Application.dataPath + "/..");
        string inputFolder = EditorUtility.OpenFolderPanel("Pick folder containing JSON backups", suggested, DefaultBackupFolderName);
        if (string.IsNullOrEmpty(inputFolder)) return;
        EditorPrefs.SetString(LastInputFolderEditorPrefsKey, inputFolder);

        var files = Directory.GetFiles(inputFolder, "*.json", SearchOption.TopDirectoryOnly);
        var guidToFile = new Dictionary<string, string>();
        foreach (var file in files)
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(file);
            if (TryExtractGuidFromFileName(nameNoExt, out string guid) && !guidToFile.ContainsKey(guid))
                guidToFile[guid] = file;
        }
        if (guidToFile.Count == 0)
        {
            EditorUtility.DisplayDialog("SOBackup", "No .json files with GUID in the filename were found in the folder.", "OK");
            return;
        }

        int assetsWithDiffs = 0;
        var reportLines = new List<string>();
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var (guid, path, so) = selected[i];
                EditorUtility.DisplayProgressBar("Diff ScriptableObject", path, (float)i / selected.Count);

                if (string.IsNullOrEmpty(guid) || so == null || !guidToFile.TryGetValue(guid, out string file))
                {
                    reportLines.Add($"[SODiff] No JSON found for {path} (GUID {guid}).");
                    continue;
                }

                string json = File.ReadAllText(file);

                // Create a temporary instance and apply JSON to get the "backup state"
                var temp = ScriptableObject.CreateInstance(so.GetType());
                temp.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(json, temp);

                    var diffs = DiffSerializedObjects(so, temp);
                    if (diffs.Count == 0)
                    {
                        reportLines.Add($"[SODiff] {path} -> no differences.");
                    }
                    else
                    {
                        assetsWithDiffs++;
                        reportLines.Add($"[SODiff] {path} -> {diffs.Count} change(s):");
                        foreach (var d in diffs)
                            reportLines.Add($"  - {d.PropertyPath}: current='{d.Current}'  backup='{d.Backup}'");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (assetsWithDiffs == 0)
        {
            EditorUtility.DisplayDialog("SODiff", "No differences found.", "OK");
        }
        Debug.Log(string.Join("\n", reportLines));
    }

    /// <summary>
    /// Gets the configured backup root folder from EditorPrefs, or falls back to the default if invalid.
    /// The returned path is Assets-relative.
    /// </summary>
    private static string GetBackupRootFolder()
    {
        string path = EditorPrefs.GetString(BackupRootEditorPrefsKey, DefaultBackupRootFolder);
        if (!AssetDatabase.IsValidFolder(path))
        {
            Debug.LogWarning($"[SOBackup] Configured backup root '{path}' is not a valid folder. Using default '{DefaultBackupRootFolder}'.");
            path = DefaultBackupRootFolder;
        }
        return path;
    }

    /// <summary>
    /// Converts an absolute filesystem path into an Assets-relative path ("Assets/…") when inside the project.
    /// Returns null if the path is outside the Assets folder.
    /// </summary>
    /// <param name="absolutePath">Absolute filesystem path.</param>
    private static string MakeAssetsRelativePath(string absolutePath)
    {
        string assetsAbs = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        string full = Path.GetFullPath(absolutePath).Replace('\\', '/');

        if (full.StartsWith(assetsAbs))
        {
            string rel = "Assets" + full.Substring(assetsAbs.Length);
            return rel.TrimEnd('/');
        }
        return null;
    }

    /// <summary>
    /// Enumerates the currently selected assets in the Project window that are ScriptableObjects.
    /// Yields tuples of (guid, assetPath, instance).
    /// </summary>
    private static IEnumerable<(string guid, string path, ScriptableObject so)> GetSelectedScriptableObjects()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so != null)
                    yield return (guid, path, so);
            }
        }
    }

    /// <summary>
    /// Returns true if the provided string is a Unity GUID (32 lowercase/uppercase hex characters, no dashes).
    /// </summary>
    private static bool IsGuid(string s)
    {
        return !string.IsNullOrEmpty(s) && s.Length == 32 && s.All(c => "0123456789abcdef".Contains(char.ToLowerInvariant(c)));
    }

    /// <summary>
    /// Tries to extract a Unity GUID from a filename (without extension). It matches either:
    /// - The whole filename if it is a 32-hex GUID, or
    /// - Any 32-hex substring (e.g., "{AssetName}__{GUID}").
    /// </summary>
    /// <param name="nameWithoutExtension">Filename without extension.</param>
    /// <param name="guid">Output GUID in lowercase when found.</param>
    /// <returns>True if a GUID was found; otherwise false.</returns>
    private static bool TryExtractGuidFromFileName(string nameWithoutExtension, out string guid)
    {
        guid = null;

        if (IsGuid(nameWithoutExtension))
        {
            guid = nameWithoutExtension.ToLowerInvariant();
            return true;
        }

        var m = Regex.Match(nameWithoutExtension, "([0-9a-fA-F]{32})");
        if (m.Success)
        {
            guid = m.Groups[1].Value.ToLowerInvariant();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the backup filename for a ScriptableObject as {SanitizedAssetName}__{GUID}.json.
    /// Keeping the GUID ensures stable mapping on restore even after renames or moves.
    /// </summary>
    /// <param name="guid">Asset GUID.</param>
    /// <param name="so">ScriptableObject instance.</param>
    /// <param name="assetPath">Asset path, used as fallback for name.</param>
    /// <returns>Filename including extension.</returns>
    private static string BuildBackupFileName(string guid, ScriptableObject so, string assetPath)
    {
        string baseName = so != null ? so.name : Path.GetFileNameWithoutExtension(assetPath);
        baseName = SanitizeFileName(baseName);
        // Ensure uniqueness and stable mapping by keeping the GUID in the filename
        return $"{baseName}__{guid}.json";
    }

    /// <summary>
    /// Replaces invalid filename characters with underscores and trims trailing dots and spaces.
    /// </summary>
    /// <param name="name">Original asset name.</param>
    /// <returns>Safe filename fragment.</returns>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            cleaned[i] = invalid.Contains(c) ? '_' : c;
        }
        // Optionally trim spaces/dots often problematic at end of filenames
        return new string(cleaned).Trim().TrimEnd('.');
    }

    /// <summary>
    /// Applies a simple JSON key rename map by replacing occurrences of "oldKey": with "newKey":.
    /// Useful when fields were renamed and [FormerlySerializedAs] is not available.
    /// </summary>
    /// <param name="json">Source JSON text.</param>
    /// <param name="map">Dictionary of oldKey -> newKey.</param>
    /// <returns>Transformed JSON text.</returns>
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

    // -------- SODiff internals --------

    /// <summary>
    /// One diff entry representing a value difference between current object and backup.
    /// </summary>
    private struct DiffEntry
    {
        public string PropertyPath;
        public string Current;
        public string Backup;
    }

    /// <summary>
    /// Computes a list of differences between two serialized objects, comparing leaf properties by propertyPath.
    /// </summary>
    private static List<DiffEntry> DiffSerializedObjects(UnityEngine.Object current, UnityEngine.Object backup)
    {
        var curMap = SnapshotSerialized(current);
        var bakMap = SnapshotSerialized(backup);

        var allKeys = new HashSet<string>(curMap.Keys);
        allKeys.UnionWith(bakMap.Keys);

        var diffs = new List<DiffEntry>();
        foreach (var key in allKeys)
        {
            curMap.TryGetValue(key, out var cv);
            bakMap.TryGetValue(key, out var bv);
            if (!EqualityComparer<string>.Default.Equals(cv, bv))
            {
                diffs.Add(new DiffEntry { PropertyPath = key, Current = cv, Backup = bv });
            }
        }
        return diffs.OrderBy(d => d.PropertyPath).ToList();
    }

    /// <summary>
    /// Creates a map propertyPath -> comparable string value for all leaf serialized properties of an object.
    /// Skips non-informative properties like m_Script.
    /// </summary>
    private static Dictionary<string, string> SnapshotSerialized(UnityEngine.Object obj)
    {
        var dict = new Dictionary<string, string>();
        var so = new SerializedObject(obj);
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;

            if (it.propertyPath == "m_Script") continue;

            if (TryGetComparableString(it, out string value))
            {
                dict[it.propertyPath] = value;
            }
        }
        return dict;
    }

    /// <summary>
    /// Converts a SerializedProperty into a comparable string representation when supported.
    /// Returns false for container/unsupported types.
    /// </summary>
    private static bool TryGetComparableString(SerializedProperty p, out string value)
    {
        value = null;
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.ArraySize:
            case SerializedPropertyType.Character:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.FixedBufferSize:
                value = p.intValue.ToString(CultureInfo.InvariantCulture);
                return true;

            case SerializedPropertyType.Boolean:
                value = p.boolValue ? "true" : "false";
                return true;

            case SerializedPropertyType.Float:
                value = p.floatValue.ToString("R", CultureInfo.InvariantCulture);
                return true;

            case SerializedPropertyType.String:
                value = p.stringValue ?? string.Empty;
                return true;

            case SerializedPropertyType.Color:
                {
                    var c = p.colorValue;
                    value = $"{c.r.ToString("R", CultureInfo.InvariantCulture)},{c.g.ToString("R", CultureInfo.InvariantCulture)},{c.b.ToString("R", CultureInfo.InvariantCulture)},{c.a.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }

            case SerializedPropertyType.Vector2:
                {
                    var v = p.vector2Value;
                    value = $"{v.x.ToString("R", CultureInfo.InvariantCulture)},{v.y.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
            case SerializedPropertyType.Vector3:
                {
                    var v = p.vector3Value;
                    value = $"{v.x.ToString("R", CultureInfo.InvariantCulture)},{v.y.ToString("R", CultureInfo.InvariantCulture)},{v.z.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
            case SerializedPropertyType.Vector4:
                {
                    var v = p.vector4Value;
                    value = $"{v.x.ToString("R", CultureInfo.InvariantCulture)},{v.y.ToString("R", CultureInfo.InvariantCulture)},{v.z.ToString("R", CultureInfo.InvariantCulture)},{v.w.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
            case SerializedPropertyType.Quaternion:
                {
                    var q = p.quaternionValue;
                    value = $"{q.x.ToString("R", CultureInfo.InvariantCulture)},{q.y.ToString("R", CultureInfo.InvariantCulture)},{q.z.ToString("R", CultureInfo.InvariantCulture)},{q.w.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
            case SerializedPropertyType.Rect:
                {
                    var r = p.rectValue;
                    value = $"{r.xMin.ToString("R", CultureInfo.InvariantCulture)},{r.yMin.ToString("R", CultureInfo.InvariantCulture)},{r.width.ToString("R", CultureInfo.InvariantCulture)},{r.height.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
            case SerializedPropertyType.Bounds:
                {
                    var b = p.boundsValue;
                    value = $"{b.center.x.ToString("R", CultureInfo.InvariantCulture)},{b.center.y.ToString("R", CultureInfo.InvariantCulture)},{b.center.z.ToString("R", CultureInfo.InvariantCulture)}|{b.size.x.ToString("R", CultureInfo.InvariantCulture)},{b.size.y.ToString("R", CultureInfo.InvariantCulture)},{b.size.z.ToString("R", CultureInfo.InvariantCulture)}";
                    return true;
                }
#if UNITY_2020_1_OR_NEWER
            case SerializedPropertyType.Vector2Int:
                {
                    var v = p.vector2IntValue;
                    value = $"{v.x},{v.y}";
                    return true;
                }
            case SerializedPropertyType.Vector3Int:
                {
                    var v = p.vector3IntValue;
                    value = $"{v.x},{v.y},{v.z}";
                    return true;
                }
            case SerializedPropertyType.RectInt:
                {
                    var r = p.rectIntValue;
                    value = $"{r.xMin},{r.yMin},{r.width},{r.height}";
                    return true;
                }
            case SerializedPropertyType.BoundsInt:
                {
                    var b = p.boundsIntValue;
                    value = $"{b.position.x},{b.position.y},{b.position.z}|{b.size.x},{b.size.y},{b.size.z}";
                    return true;
                }
#endif
            case SerializedPropertyType.ObjectReference:
                value = GetObjectRefKey(p.objectReferenceValue);
                return true;

            case SerializedPropertyType.AnimationCurve:
                {
                    var c = p.animationCurveValue;
                    if (c == null) { value = "null"; return true; }
                    value = $"curve:{c.length}";
                    return true;
                }
#if UNITY_2021_2_OR_NEWER
            case SerializedPropertyType.ManagedReference:
                value = p.managedReferenceFullTypename ?? string.Empty;
                return true;
#endif
            case SerializedPropertyType.Generic:
            default:
                return false; // container or unsupported; children will be compared
        }
    }

    /// <summary>
    /// Returns a stable identifier for an Object reference: guid:localId when available, else asset path, else name/null.
    /// </summary>
    private static string GetObjectRefKey(UnityEngine.Object obj)
    {
        if (obj == null) return "null";
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            return $"{guid}:{localId}";
        var path = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path)) return path;
        return obj.name ?? obj.GetInstanceID().ToString();
    }
}
#endif
