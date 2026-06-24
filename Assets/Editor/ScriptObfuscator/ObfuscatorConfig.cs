using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScriptObfuscator
{
    [Serializable]
    internal sealed class ObfuscatorConfig
    {
        private const string EditorPrefsKey = "ScriptObfuscator.Config.v1";

        public List<string> sourceAssetPaths = new List<string>();
        public string dllName = "MyPackage.Core";
        public string outputFolder = "Assets/Plugins/Obfuscated";
        public string targetFramework = "netstandard2.1";
        public bool enableRename = true;
        public bool enableControlFlow = true;
        public bool enableStringEncryption = true;
        public bool includeEditorReferences;
        public bool allowUnsafeCode;
        public bool overwriteOutput = true;
        public bool removeSourcesAfterSuccess;
        public string sourceBackupFolder = "ScriptObfuscatorBackups";

        public static ObfuscatorConfig Load()
        {
            string json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ObfuscatorConfig();
            }

            try
            {
                ObfuscatorConfig config = JsonUtility.FromJson<ObfuscatorConfig>(json);
                return config ?? new ObfuscatorConfig();
            }
            catch
            {
                return new ObfuscatorConfig();
            }
        }

        public void Save()
        {
            EditorPrefs.SetString(EditorPrefsKey, JsonUtility.ToJson(this));
        }

        public string SanitizedAssemblyName()
        {
            string name = string.IsNullOrWhiteSpace(dllName) ? "ObfuscatedAssembly" : dllName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            return string.IsNullOrWhiteSpace(name) ? "ObfuscatedAssembly" : name;
        }

        public string NormalizedOutputFolder()
        {
            string folder = string.IsNullOrWhiteSpace(outputFolder) ? "Assets/Plugins/Obfuscated" : outputFolder.Trim();
            folder = folder.Replace('\\', '/').TrimEnd('/');
            return folder.StartsWith("Assets/", StringComparison.Ordinal) || folder == "Assets"
                ? folder
                : "Assets/Plugins/Obfuscated";
        }

        public string NormalizedBackupFolder()
        {
            string folder = string.IsNullOrWhiteSpace(sourceBackupFolder)
                ? "ScriptObfuscatorBackups"
                : sourceBackupFolder.Trim().Trim('"');
            return Path.GetFullPath(Path.IsPathRooted(folder)
                ? folder
                : Path.Combine(ConfuserExManager.ProjectRoot, folder));
        }
    }
}
