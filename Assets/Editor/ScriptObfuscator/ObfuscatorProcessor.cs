using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ScriptObfuscator
{
    internal static class ObfuscatorProcessor
    {
        private const string TempRootRelative = "Temp/ScriptObfuscator";

        public static string BuildAndObfuscate(ObfuscatorConfig config)
        {
            return Build(config, null, false);
        }

        public static string BuildAndReplace(ObfuscatorConfig config, Func<string> selectBackupFolder)
        {
            if (selectBackupFolder == null)
            {
                throw new ArgumentNullException(nameof(selectBackupFolder));
            }

            return Build(config, selectBackupFolder, true);
        }

        private static string Build(ObfuscatorConfig config, Func<string> selectBackupFolder, bool replaceSources)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (!ConfuserExManager.IsInstalled)
            {
                throw new FileNotFoundException(
                    "Install ConfuserEx before obfuscating. Expected Confuser.CLI.exe at:",
                    ConfuserExManager.ExpectedCliPath);
            }

            List<string> sourceFiles = ExpandSourceFiles(config.sourceAssetPaths);
            if (sourceFiles.Count == 0)
            {
                throw new InvalidOperationException(GetEmptySourceError(config.sourceAssetPaths));
            }

            if (SourcesRequireUnityEditor(sourceFiles))
            {
                config.includeEditorReferences = true;
            }

            string assemblyName = config.SanitizedAssemblyName();
            string tempRoot = Path.Combine(ConfuserExManager.ProjectRoot, TempRootRelative);
            string projectDirectory = Path.Combine(tempRoot, "Project");
            string buildOutputDirectory = Path.Combine(tempRoot, "Build");
            string confuserOutputDirectory = Path.Combine(tempRoot, "Obfuscated");

            ResetDirectory(tempRoot);
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(buildOutputDirectory);
            Directory.CreateDirectory(confuserOutputDirectory);

            string csprojPath = CreateCsproj(projectDirectory, assemblyName, sourceFiles, config);
            RunDotnetBuild(csprojPath);

            string builtAssembly = Path.Combine(buildOutputDirectory, config.targetFramework, assemblyName + ".dll");
            if (!File.Exists(builtAssembly))
            {
                builtAssembly = Directory.GetFiles(buildOutputDirectory, assemblyName + ".dll", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(builtAssembly))
                {
                    throw new FileNotFoundException("Build completed, but the expected DLL was not produced.", Path.Combine(buildOutputDirectory, assemblyName + ".dll"));
                }
            }

            string confuserProject = ConfuserExManager.CreateProjectFile(
                builtAssembly,
                confuserOutputDirectory,
                Path.Combine(tempRoot, "Confuser"),
                config,
                GetConfuserProbePaths(config, sourceFiles, builtAssembly));

            ConfuserExManager.Run(confuserProject);

            string obfuscatedAssembly = Path.Combine(confuserOutputDirectory, assemblyName + ".dll");
            if (!File.Exists(obfuscatedAssembly))
            {
                throw new FileNotFoundException("ConfuserEx completed, but the obfuscated DLL was not produced.", obfuscatedAssembly);
            }

            if (replaceSources)
            {
                string selectedBackupRoot = selectBackupFolder();
                if (string.IsNullOrWhiteSpace(selectedBackupRoot))
                {
                    throw new OperationCanceledException("Replacement canceled. The obfuscated DLL was built, but no source files were changed.");
                }

                ValidateBackupFolder(selectedBackupRoot, sourceFiles, config.sourceAssetPaths);
                string backupPath = BackupSources(sourceFiles, assemblyName, selectedBackupRoot, config.sourceAssetPaths, false);
                string outputPath = CopyToSelectedLocation(obfuscatedAssembly, config);
                DeleteOriginalSources(sourceFiles);
                ConfigureImporterIfCurrentProjectAsset(outputPath, config.includeEditorReferences);
                AssetDatabase.Refresh();
                Debug.Log($"Script Obfuscator replaced sources with {outputPath}. Backup: {backupPath}");
                return outputPath;
            }

            string outputAssetPath = CopyToAssets(obfuscatedAssembly, config);
            ConfigurePluginImporter(outputAssetPath, config.includeEditorReferences);

            if (config.removeSourcesAfterSuccess)
            {
                ValidateBackupFolder(config.NormalizedBackupFolder(), sourceFiles, config.sourceAssetPaths);
                BackupSources(sourceFiles, assemblyName, config.NormalizedBackupFolder(), config.sourceAssetPaths, true);
            }

            AssetDatabase.Refresh();
            Debug.Log("Script Obfuscator created " + outputAssetPath);
            return outputAssetPath;
        }

        public static List<string> ExpandSourceFiles(IEnumerable<string> assetPaths)
        {
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string sourcePath in assetPaths ?? Enumerable.Empty<string>())
            {
                if (!TryNormalizeSourcePath(sourcePath, out string normalized))
                {
                    continue;
                }

                string absolutePath = SourcePathToAbsolutePath(normalized);

                if (File.Exists(absolutePath) && normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    AddSourceFileOrOwningAsmdef(files, absolutePath);
                }
                else if (Directory.Exists(absolutePath))
                {
                    AddSourceDirectoryOrOwningAsmdef(files, absolutePath);
                }
            }

            return files.ToList();
        }

        public static bool TryNormalizeSourcePath(string inputPath, out string sourcePath)
        {
            sourcePath = string.Empty;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return false;
            }

            string normalized = inputPath.Trim().Trim('"').Replace('\\', '/').TrimEnd('/');
            try
            {
                string absolutePath = Path.IsPathRooted(normalized)
                    ? Path.GetFullPath(normalized)
                    : Path.GetFullPath(Path.Combine(ConfuserExManager.ProjectRoot, normalized));

                string assetsRoot = Path.GetFullPath(Path.Combine(ConfuserExManager.ProjectRoot, "Assets"));
                if (IsPathWithin(absolutePath, assetsRoot))
                {
                    string relative = absolutePath.Substring(assetsRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    sourcePath = string.IsNullOrEmpty(relative) ? "Assets" : "Assets/" + relative;
                }
                else
                {
                    sourcePath = absolutePath.Replace('\\', '/');
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsValidSourcePath(string inputPath)
        {
            if (!TryNormalizeSourcePath(inputPath, out string normalized))
            {
                return false;
            }

            string absolutePath = SourcePathToAbsolutePath(normalized);
            return Directory.Exists(absolutePath) ||
                   (File.Exists(absolutePath) && absolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsProjectAssetSource(string sourcePath)
        {
            if (!TryNormalizeSourcePath(sourcePath, out string normalized))
            {
                return false;
            }

            return normalized.Equals("Assets", StringComparison.Ordinal) ||
                   normalized.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static void AddSourceFileOrOwningAsmdef(ISet<string> files, string sourceFile)
        {
            if (IsEditorScriptPath(sourceFile))
            {
                return;
            }

            string asmdefPath = FindNearestAsmdef(sourceFile);
            if (!string.IsNullOrWhiteSpace(asmdefPath))
            {
                AddRuntimeSourcesFromDirectory(files, Path.GetDirectoryName(asmdefPath) ?? Path.GetDirectoryName(sourceFile));
                return;
            }

            files.Add(Path.GetFullPath(sourceFile));
        }

        private static void AddSourceDirectoryOrOwningAsmdef(ISet<string> files, string sourceDirectory)
        {
            if (IsUnityProjectRoot(sourceDirectory))
            {
                AddRuntimeSourcesFromDirectory(files, Path.Combine(sourceDirectory, "Assets"));
                return;
            }

            string asmdefPath = FindNearestAsmdef(sourceDirectory);
            if (!string.IsNullOrWhiteSpace(asmdefPath))
            {
                AddRuntimeSourcesFromDirectory(files, Path.GetDirectoryName(asmdefPath));
                return;
            }

            AddRuntimeSourcesFromDirectory(files, sourceDirectory);
        }

        private static bool IsUnityProjectRoot(string directory)
        {
            return Directory.Exists(Path.Combine(directory, "Assets")) &&
                   Directory.Exists(Path.Combine(directory, "ProjectSettings"));
        }

        private static void AddRuntimeSourcesFromDirectory(ISet<string> files, string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsEditorScriptPath(file))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
        }

        public static int CountExpandedSourceFilesForDisplay(IEnumerable<string> assetPaths)
        {
            try
            {
                return ExpandSourceFiles(assetPaths).Count;
            }
            catch
            {
                int count = 0;
                foreach (string assetPath in assetPaths ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(assetPath))
                    {
                        continue;
                    }

                    if (!TryNormalizeSourcePath(assetPath, out string normalized))
                    {
                        continue;
                    }

                    string absolutePath = SourcePathToAbsolutePath(normalized);
                    count += File.Exists(absolutePath) ? 1 : 0;
                }

                return count;
            }
        }

        private static string CreateCsproj(
            string projectDirectory,
            string assemblyName,
            List<string> sourceFiles,
            ObfuscatorConfig config)
        {
            string csprojPath = Path.Combine(projectDirectory, assemblyName + ".csproj");
            HashSet<string> excludedSourceAssemblies = FindOwningAsmdefNames(sourceFiles);
            string references = string.Join("\n", CollectReferencePaths(config.includeEditorReferences, excludedSourceAssemblies, sourceFiles)
                .Select(path => $"    <Reference Include=\"{EscapeXml(Path.GetFileNameWithoutExtension(path))}\"><HintPath>{EscapeXml(path)}</HintPath><Private>false</Private></Reference>"));
            string compileItems = string.Join("\n", sourceFiles
                .Select(path => $"    <Compile Include=\"{EscapeXml(path)}\" />"));
            string constants = GetUnityDefineConstants(config.includeEditorReferences, sourceFiles);

            string xml =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                "  <PropertyGroup>\n" +
                "    <OutputType>Library</OutputType>\n" +
                $"    <TargetFramework>{EscapeXml(config.targetFramework)}</TargetFramework>\n" +
                $"    <AssemblyName>{EscapeXml(assemblyName)}</AssemblyName>\n" +
                "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
                "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>\n" +
                "    <AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>\n" +
                $"    <OutputPath>{EscapeXml(Path.Combine(projectDirectory, "..", "Build"))}</OutputPath>\n" +
                "    <LangVersion>latest</LangVersion>\n" +
                "    <Nullable>disable</Nullable>\n" +
                "    <NoWarn>$(NoWarn);0436</NoWarn>\n" +
                $"    <AllowUnsafeBlocks>{(config.allowUnsafeCode ? "true" : "false")}</AllowUnsafeBlocks>\n" +
                $"    <DefineConstants>{EscapeXml(constants)}</DefineConstants>\n" +
                "  </PropertyGroup>\n" +
                "  <ItemGroup>\n" +
                compileItems + "\n" +
                "  </ItemGroup>\n" +
                "  <ItemGroup>\n" +
                references + "\n" +
                "  </ItemGroup>\n" +
                "</Project>\n";

            File.WriteAllText(csprojPath, xml, new UTF8Encoding(false));
            return csprojPath;
        }

        private static void RunDotnetBuild(string csprojPath)
        {
            ProcessResult result = ConfuserExManager.RunProcess(
                "dotnet",
                "build " + ConfuserExManager.Quote(csprojPath) + " -c Release --nologo",
                ConfuserExManager.ProjectRoot);

            if (result.ExitCode != 0)
            {
                Debug.LogError("Script Obfuscator compilation failed.\n" + result.CombinedOutput);
                throw new InvalidOperationException(FormatCompilationFailure(result.CombinedOutput));
            }

            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
            {
                Debug.Log(result.CombinedOutput);
            }
        }

        internal static IEnumerable<string> CollectReferencePaths(
            bool includeEditorReferences,
            HashSet<string> excludedAssemblyNames,
            IEnumerable<string> sourceFiles)
        {
            var references = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string unityManaged = Path.Combine(EditorApplication.applicationContentsPath, "Managed");
            string unityEngineModules = Path.Combine(unityManaged, "UnityEngine");

            AddDlls(Path.Combine(unityEngineModules, "UnityEngine.dll"), references, excludedAssemblyNames);
            AddDlls(unityEngineModules, references, excludedAssemblyNames, SearchOption.TopDirectoryOnly, "UnityEngine*.dll");
            AddDlls(Path.Combine(unityManaged, "netstandard"), references, excludedAssemblyNames, SearchOption.AllDirectories);
            AddDlls(Path.Combine(ConfuserExManager.ProjectRoot, "Library", "ScriptAssemblies"), references, excludedAssemblyNames, SearchOption.TopDirectoryOnly, "*.dll", includeEditorReferences);

            if (includeEditorReferences)
            {
                AddDlls(unityManaged, references, excludedAssemblyNames, SearchOption.TopDirectoryOnly, "UnityEditor*.dll", true);
            }

            List<string> sourceProjectRoots = FindSourceUnityProjectRoots(sourceFiles).ToList();
            foreach (string sourceProjectRoot in sourceProjectRoots)
            {
                if (string.Equals(sourceProjectRoot, ConfuserExManager.ProjectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddExternalProjectReferences(sourceProjectRoot, references, excludedAssemblyNames, includeEditorReferences);
            }

            return references
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.FirstOrDefault(path =>
                    sourceProjectRoots.Any(root => IsPathWithin(path, root))) ?? group.First());
        }

        private static IEnumerable<string> GetConfuserProbePaths(
            ObfuscatorConfig config,
            List<string> sourceFiles,
            string builtAssembly)
        {
            HashSet<string> excludedSourceAssemblies = FindOwningAsmdefNames(sourceFiles);
            var probePaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            AddProbeDirectory(Path.GetDirectoryName(builtAssembly), probePaths);
            AddDotnetReferencePackProbe(config.targetFramework, probePaths);
            foreach (string referencePath in CollectReferencePaths(config.includeEditorReferences, excludedSourceAssemblies, sourceFiles))
            {
                AddProbeDirectory(Path.GetDirectoryName(referencePath), probePaths);
            }

            AddProbeDirectory(Path.Combine(ConfuserExManager.ProjectRoot, "Temp", "bin", "Release"), probePaths);
            AddProbeDirectory(Path.Combine(ConfuserExManager.ProjectRoot, "Library", "ScriptAssemblies"), probePaths);

            return probePaths;
        }

        internal static string FormatCompilationFailure(string output)
        {
            string[] lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> errors = lines
                .Where(line => line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (errors.Count > 0)
            {
                const int maxVisibleErrors = 12;
                var summary = new List<string>
                {
                    $"C# compilation failed with {errors.Count} error(s)."
                };
                summary.AddRange(errors.Take(maxVisibleErrors));
                if (errors.Count > maxVisibleErrors)
                {
                    summary.Add($"...and {errors.Count - maxVisibleErrors} more error(s). See the Unity Console for full output.");
                }

                return string.Join("\n\n", summary);
            }

            string[] usefulLines = lines
                .Where(line => line.IndexOf("warning ", StringComparison.OrdinalIgnoreCase) < 0)
                .ToArray();
            string[] usefulTail = usefulLines.Skip(Math.Max(0, usefulLines.Length - 20)).ToArray();
            return "C# compilation failed.\n" +
                   (usefulTail.Length > 0 ? string.Join("\n", usefulTail) : "See the Unity Console for full output.");
        }

        private static void AddDotnetReferencePackProbe(string targetFramework, ISet<string> probePaths)
        {
            if (string.IsNullOrWhiteSpace(targetFramework))
            {
                return;
            }

            string dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(GetDotnetExecutablePath()) ?? string.Empty));
            if (string.IsNullOrWhiteSpace(dotnetRoot) || !Directory.Exists(dotnetRoot))
            {
                return;
            }

            string packsRoot = Path.Combine(dotnetRoot, "packs", "NETStandard.Library.Ref");
            if (!Directory.Exists(packsRoot))
            {
                return;
            }

            foreach (string versionDirectory in Directory.GetDirectories(packsRoot).OrderByDescending(Path.GetFileName))
            {
                string referenceDirectory = Path.Combine(versionDirectory, "ref", targetFramework.Trim());
                if (File.Exists(Path.Combine(referenceDirectory, "netstandard.dll")))
                {
                    AddProbeDirectory(referenceDirectory, probePaths);
                    return;
                }
            }
        }

        private static string GetDotnetExecutablePath()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string defaultPath = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            return File.Exists(defaultPath) ? defaultPath : "dotnet";
        }

        private static void AddProbeDirectory(string directory, ISet<string> probePaths)
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                probePaths.Add(Path.GetFullPath(directory));
            }
        }

        private static void AddDlls(
            string path,
            ISet<string> references,
            HashSet<string> excludedAssemblyNames,
            SearchOption searchOption = SearchOption.TopDirectoryOnly,
            string searchPattern = "*.dll",
            bool includeEditorAssemblies = false)
        {
            if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                AddReferenceIfAllowed(path, references, excludedAssemblyNames, includeEditorAssemblies);
                return;
            }

            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string dll in Directory.GetFiles(path, searchPattern, searchOption))
            {
                AddReferenceIfAllowed(dll, references, excludedAssemblyNames, includeEditorAssemblies);
            }
        }

        private static void AddReferenceIfAllowed(
            string path,
            ISet<string> references,
            HashSet<string> excludedAssemblyNames,
            bool includeEditorAssemblies)
        {
            string fileName = Path.GetFileName(path);
            string assemblyName = Path.GetFileNameWithoutExtension(path);
            if (excludedAssemblyNames.Contains(assemblyName))
            {
                return;
            }

            if (!includeEditorAssemblies && fileName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            references.Add(path);
        }

        private static string CopyToAssets(string obfuscatedAssembly, ObfuscatorConfig config)
        {
            string outputFolder = config.NormalizedOutputFolder();
            EnsureAssetFolder(outputFolder);

            string outputAssetPath = outputFolder + "/" + Path.GetFileName(obfuscatedAssembly);
            if (!config.overwriteOutput)
            {
                outputAssetPath = AssetDatabase.GenerateUniqueAssetPath(outputAssetPath);
            }

            string absoluteOutputPath = Path.Combine(ConfuserExManager.ProjectRoot, outputAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath) ?? ConfuserExManager.ProjectRoot);
            File.Copy(obfuscatedAssembly, absoluteOutputPath, true);
            return outputAssetPath;
        }

        private static void ConfigurePluginImporter(string outputAssetPath, bool editorOnly)
        {
            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
            PluginImporter importer = AssetImporter.GetAtPath(outputAssetPath) as PluginImporter;
            if (importer == null)
            {
                return;
            }

            importer.SetCompatibleWithAnyPlatform(!editorOnly);
            importer.SetCompatibleWithEditor(true);
            importer.SaveAndReimport();
        }

        internal static bool SourcesRequireUnityEditor(IEnumerable<string> sourceFiles)
        {
            foreach (string sourceFile in sourceFiles)
            {
                try
                {
                    string source = File.ReadAllText(sourceFile);
                    if (source.IndexOf("using UnityEditor", StringComparison.Ordinal) >= 0 ||
                        source.IndexOf("UnityEditor.", StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // The compiler reports unreadable source files with their exact path.
                }
            }

            return false;
        }

        private static IEnumerable<string> FindSourceUnityProjectRoots(IEnumerable<string> sourceFiles)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sourceFile in sourceFiles ?? Enumerable.Empty<string>())
            {
                DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);
                while (directory != null)
                {
                    if (directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                        directory.Parent != null &&
                        Directory.Exists(Path.Combine(directory.Parent.FullName, "ProjectSettings")))
                    {
                        roots.Add(directory.Parent.FullName);
                        break;
                    }

                    directory = directory.Parent;
                }
            }

            return roots;
        }

        private static void AddExternalProjectReferences(
            string projectRoot,
            ISet<string> references,
            HashSet<string> excludedAssemblyNames,
            bool includeEditorReferences)
        {
            string scriptAssemblies = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
            if (Directory.Exists(scriptAssemblies))
            {
                foreach (string dll in Directory.GetFiles(scriptAssemblies, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    AddReferenceIfAllowed(dll, references, excludedAssemblyNames, includeEditorReferences);
                }
            }

            string assets = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assets))
            {
                foreach (string dll in Directory.GetFiles(assets, "*.dll", SearchOption.AllDirectories))
                {
                    AddReferenceIfAllowed(dll, references, excludedAssemblyNames, includeEditorReferences);
                }
            }
        }

        private static string BackupSources(
            IEnumerable<string> sourceFiles,
            string assemblyName,
            string selectedBackupRoot,
            IEnumerable<string> selectedInputs,
            bool deleteAfterBackup)
        {
            List<string> files = sourceFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
            {
                return string.Empty;
            }

            string backupRoot = Path.Combine(
                selectedBackupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + assemblyName + ".SourceBackup~");

            foreach (string sourceFile in files)
            {
                string relativePath = GetBackupRelativePath(sourceFile, selectedInputs);
                string backupPath = Path.Combine(backupRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? backupRoot);

                File.Copy(sourceFile, backupPath, true);
                string sourceMetaPath = sourceFile + ".meta";
                if (File.Exists(sourceMetaPath))
                {
                    File.Copy(sourceMetaPath, backupPath + ".meta", true);
                }

                if (!File.Exists(backupPath) || new FileInfo(backupPath).Length != new FileInfo(sourceFile).Length)
                {
                    throw new IOException("Backup verification failed for " + sourceFile);
                }
            }

            File.WriteAllText(
                Path.Combine(backupRoot, "README.txt"),
                "Original C# sources backed up by Script Obfuscator before replacement with an obfuscated DLL.\n",
                new UTF8Encoding(false));

            if (deleteAfterBackup)
            {
                DeleteOriginalSources(files);
            }

            return backupRoot;
        }

        public static void ValidateBackupFolder(string backupRoot)
        {
            ValidateBackupFolder(backupRoot, Array.Empty<string>(), Array.Empty<string>());
        }

        private static void ValidateBackupFolder(
            string backupRoot,
            IEnumerable<string> sourceFiles,
            IEnumerable<string> selectedInputs)
        {
            if (string.IsNullOrWhiteSpace(backupRoot))
            {
                throw new InvalidOperationException("Choose a source backup destination before building.");
            }

            // Any filesystem destination is valid. Backup folders end in '~' so Unity ignores them inside Assets.
            Path.GetFullPath(backupRoot);
        }

        internal static string GetSelectedOutputDirectory(IEnumerable<string> selectedInputs)
        {
            List<string> paths = (selectedInputs ?? Enumerable.Empty<string>())
                .Select(input => TryNormalizeSourcePath(input, out string normalized) ? SourcePathToAbsolutePath(normalized) : string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => File.Exists(path) ? Path.GetDirectoryName(path) : path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .ToList();
            if (paths.Count == 0)
            {
                throw new InvalidOperationException("Could not determine where to place the obfuscated DLL.");
            }

            string directory = paths.Count == 1 ? paths[0] : GetCommonDirectory(paths);
            if (Directory.Exists(Path.Combine(directory, "Assets")) && Directory.Exists(Path.Combine(directory, "ProjectSettings")))
            {
                directory = Path.Combine(directory, "Assets");
            }

            return directory;
        }

        private static string CopyToSelectedLocation(string obfuscatedAssembly, ObfuscatorConfig config)
        {
            string destinationDirectory = GetSelectedOutputDirectory(config.sourceAssetPaths);
            Directory.CreateDirectory(destinationDirectory);
            string outputPath = Path.Combine(destinationDirectory, Path.GetFileName(obfuscatedAssembly));
            if (!config.overwriteOutput && File.Exists(outputPath))
            {
                string name = Path.GetFileNameWithoutExtension(outputPath);
                string extension = Path.GetExtension(outputPath);
                int index = 1;
                do outputPath = Path.Combine(destinationDirectory, $"{name}-{index++}{extension}");
                while (File.Exists(outputPath));
            }

            File.Copy(obfuscatedAssembly, outputPath, true);
            return outputPath;
        }

        private static void ConfigureImporterIfCurrentProjectAsset(string outputPath, bool editorOnly)
        {
            string assetPath = ToAssetPath(outputPath);
            if (!string.IsNullOrWhiteSpace(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                ConfigurePluginImporter(assetPath.Replace('\\', '/'), editorOnly);
            }
        }

        private static void DeleteOriginalSources(IEnumerable<string> sourceFiles)
        {
            foreach (string sourceFile in sourceFiles)
            {
                string assetPath = ToAssetPath(sourceFile);
                if (!string.IsNullOrWhiteSpace(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!AssetDatabase.DeleteAsset(assetPath.Replace('\\', '/')))
                    {
                        throw new IOException("Failed to remove original source: " + sourceFile);
                    }
                    continue;
                }

                File.Delete(sourceFile);
                string metaPath = sourceFile + ".meta";
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
        }

        private static string GetBackupRelativePath(string sourceFile, IEnumerable<string> selectedInputs)
        {
            string fullSource = Path.GetFullPath(sourceFile);
            foreach (string input in selectedInputs ?? Enumerable.Empty<string>())
            {
                if (!TryNormalizeSourcePath(input, out string normalized)) continue;
                string absolute = SourcePathToAbsolutePath(normalized);
                if (File.Exists(absolute) && string.Equals(Path.GetFullPath(absolute), fullSource, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(fullSource);
                }
                if (Directory.Exists(absolute) && IsPathWithin(fullSource, absolute))
                {
                    string parent = Path.GetDirectoryName(Path.GetFullPath(absolute)) ?? absolute;
                    return MakeRelativePath(parent, fullSource);
                }
            }

            string projectRoot = FindSourceUnityProjectRoots(new[] { fullSource }).FirstOrDefault();
            return !string.IsNullOrWhiteSpace(projectRoot) ? MakeRelativePath(projectRoot, fullSource) : Path.GetFileName(fullSource);
        }

        private static string GetCommonDirectory(IReadOnlyList<string> paths)
        {
            string common = paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (!paths.All(path => IsPathWithin(path, common)))
            {
                common = Path.GetDirectoryName(common);
                if (string.IsNullOrWhiteSpace(common)) throw new InvalidOperationException("Selected sources must be on the same drive.");
            }
            return common;
        }

        private static string MakeRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            string[] parts = assetFolder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void ResetDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            Directory.CreateDirectory(directory);
        }

        private static bool IsEditorScriptPath(string path)
        {
            string normalized = Path.GetFullPath(path).Replace('\\', '/');
            return normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToAssetPath(string absolutePath)
        {
            string fullPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string root = ConfuserExManager.ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length)
                : string.Empty;
        }

        private static string SourcePathToAbsolutePath(string sourcePath)
        {
            return Path.IsPathRooted(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(ConfuserExManager.ProjectRoot, sourcePath));
        }

        private static bool IsPathWithin(string path, string root)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetEmptySourceError(IEnumerable<string> sourcePaths)
        {
            List<string> inputs = (sourcePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            if (inputs.Count == 0)
            {
                return "Add at least one C# script or folder.";
            }

            if (inputs.All(path => !IsValidSourcePath(path)))
            {
                return "None of the selected source paths exist or point to a C# file/folder.";
            }

            return "The selected sources contain no runtime C# files. Empty folders, non-C# files, and Editor folders are excluded.";
        }

        private static HashSet<string> FindOwningAsmdefNames(IEnumerable<string> sourceFiles)
        {
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string sourceFile in sourceFiles)
            {
                string asmdefPath = FindNearestAsmdef(sourceFile);
                if (string.IsNullOrWhiteSpace(asmdefPath))
                {
                    continue;
                }

                AsmdefInfo asmdef = ReadAsmdef(asmdefPath);
                if (!string.IsNullOrWhiteSpace(asmdef.name))
                {
                    assemblyNames.Add(asmdef.name);
                }
            }

            return assemblyNames;
        }

        private static string GetUnityDefineConstants(bool includeEditor, IEnumerable<string> sourceFiles)
        {
            var constants = new SortedSet<string>(StringComparer.Ordinal)
            {
                "UNITY_6000_3_OR_NEWER",
                "UNITY_6000_0_OR_NEWER",
                "UNITY_6000",
                "UNITY_5_3_OR_NEWER",
                "CSHARP_7_3_OR_NEWER",
                "NET_STANDARD_2_1"
            };

            if (includeEditor)
            {
                constants.Add("UNITY_EDITOR");
            }

            foreach (string sourceFile in sourceFiles)
            {
                string asmdefPath = FindNearestAsmdef(sourceFile);
                if (string.IsNullOrWhiteSpace(asmdefPath))
                {
                    continue;
                }

                AsmdefInfo asmdef = ReadAsmdef(asmdefPath);
                foreach (string define in asmdef.defineConstraints ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(define) && !define.StartsWith("!", StringComparison.Ordinal))
                    {
                        constants.Add(define);
                    }
                }

                foreach (VersionDefine versionDefine in asmdef.versionDefines ?? Array.Empty<VersionDefine>())
                {
                    if (!string.IsNullOrWhiteSpace(versionDefine.define))
                    {
                        constants.Add(versionDefine.define);
                    }
                }
            }

            return string.Join(";", constants);
        }

        private static string FindNearestAsmdef(string sourceFile)
        {
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? ConfuserExManager.ProjectRoot);
            string assetsRoot = Path.Combine(ConfuserExManager.ProjectRoot, "Assets").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            while (directory != null && IsPathWithin(directory.FullName, assetsRoot))
            {
                string asmdef = Directory.GetFiles(directory.FullName, "*.asmdef", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(asmdef))
                {
                    return asmdef;
                }

                directory = directory.Parent;
            }

            return string.Empty;
        }

        private static AsmdefInfo ReadAsmdef(string path)
        {
            try
            {
                return JsonUtility.FromJson<AsmdefInfo>(File.ReadAllText(path)) ?? new AsmdefInfo();
            }
            catch
            {
                return new AsmdefInfo();
            }
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

#pragma warning disable 0649
        [Serializable]
        private sealed class AsmdefInfo
        {
            public string name;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
        }

        [Serializable]
        private sealed class VersionDefine
        {
            public string define;
        }
#pragma warning restore 0649
    }
}
