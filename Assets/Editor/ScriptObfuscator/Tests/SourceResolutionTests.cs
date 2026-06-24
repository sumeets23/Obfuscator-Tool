using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ScriptObfuscator.Tests
{
    public sealed class SourceResolutionTests
    {
        private string externalRoot;
        private string assetRoot;

        [SetUp]
        public void SetUp()
        {
            externalRoot = Path.Combine(Path.GetTempPath(), "ScriptObfuscatorTests", Guid.NewGuid().ToString("N"));
            assetRoot = Path.Combine(ConfuserExManager.ProjectRoot, "Assets", "ScriptObfuscatorTestsTemp");
            Directory.CreateDirectory(externalRoot);
            Directory.CreateDirectory(assetRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(externalRoot))
            {
                Directory.Delete(externalRoot, true);
            }

            if (Directory.Exists(assetRoot))
            {
                Directory.Delete(assetRoot, true);
            }
        }

        [Test]
        public void Normalize_ProjectAbsoluteAndAssetPath_AreIdentical()
        {
            string absolute = WriteFile(Path.Combine(assetRoot, "RuntimeScript.cs"));

            Assert.That(ObfuscatorProcessor.TryNormalizeSourcePath(absolute, out string fromAbsolute), Is.True);
            Assert.That(ObfuscatorProcessor.TryNormalizeSourcePath("Assets/ScriptObfuscatorTestsTemp/RuntimeScript.cs", out string fromAsset), Is.True);
            Assert.That(fromAbsolute, Is.EqualTo(fromAsset));
            Assert.That(fromAsset, Is.EqualTo("Assets/ScriptObfuscatorTestsTemp/RuntimeScript.cs"));
            Assert.That(ObfuscatorProcessor.IsProjectAssetSource(fromAsset), Is.True);
        }

        [Test]
        public void Normalize_ExternalPath_RemainsAbsolute()
        {
            string external = WriteFile(Path.Combine(externalRoot, "ExternalScript.cs"));

            Assert.That(ObfuscatorProcessor.TryNormalizeSourcePath(external, out string normalized), Is.True);
            Assert.That(Path.IsPathRooted(normalized), Is.True);
            Assert.That(ObfuscatorProcessor.IsValidSourcePath(normalized), Is.True);
            Assert.That(ObfuscatorProcessor.IsProjectAssetSource(normalized), Is.False);
        }

        [Test]
        public void Expand_RecursiveFolder_DeduplicatesAndExcludesEditorScripts()
        {
            string runtime = WriteFile(Path.Combine(externalRoot, "Root", "Nested", "Runtime.cs"));
            WriteFile(Path.Combine(externalRoot, "Root", "Nested", "Editor", "EditorOnly.cs"));
            WriteFile(Path.Combine(externalRoot, "Root", "Readme.txt"), "not C#");

            List<string> expanded = ObfuscatorProcessor.ExpandSourceFiles(new[]
            {
                Path.Combine(externalRoot, "Root"),
                runtime,
                runtime
            });

            Assert.That(expanded, Has.Count.EqualTo(1));
            Assert.That(expanded.Single(), Is.EqualTo(Path.GetFullPath(runtime)).IgnoreCase);
        }

        [Test]
        public void Expand_UnityProjectRoot_OnlyScansAssets()
        {
            string projectRoot = Path.Combine(externalRoot, "UnityProject");
            string included = WriteFile(Path.Combine(projectRoot, "Assets", "Runtime", "Included.cs"));
            WriteFile(Path.Combine(projectRoot, "Library", "Generated.cs"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));

            List<string> expanded = ObfuscatorProcessor.ExpandSourceFiles(new[] { projectRoot });

            Assert.That(expanded, Has.Count.EqualTo(1));
            Assert.That(expanded.Single(), Is.EqualTo(Path.GetFullPath(included)).IgnoreCase);
            Assert.That(ObfuscatorProcessor.GetSelectedOutputDirectory(new[] { projectRoot }), Is.EqualTo(Path.Combine(projectRoot, "Assets")).IgnoreCase);
        }

        [Test]
        public void Validation_RejectsMissingAndNonCSharpFiles()
        {
            string textFile = WriteFile(Path.Combine(externalRoot, "Notes.txt"), "notes");

            Assert.That(ObfuscatorProcessor.IsValidSourcePath(textFile), Is.False);
            Assert.That(ObfuscatorProcessor.IsValidSourcePath(Path.Combine(externalRoot, "Missing.cs")), Is.False);
        }

        [Test]
        public void BackupValidation_AcceptsAssetsSourceFoldersAndExternalFolders()
        {
            Assert.DoesNotThrow(() =>
                ObfuscatorProcessor.ValidateBackupFolder(Path.Combine(ConfuserExManager.ProjectRoot, "Assets", "Backup")));
            Assert.DoesNotThrow(() => ObfuscatorProcessor.ValidateBackupFolder(assetRoot));
            Assert.DoesNotThrow(() => ObfuscatorProcessor.ValidateBackupFolder(externalRoot));
        }

        [Test]
        public void ExternalUnityProject_PrefersItsAssembliesAndPluginReferences()
        {
            string projectRoot = Path.Combine(externalRoot, "ExternalUnityProject");
            string source = WriteFile(Path.Combine(projectRoot, "Assets", "Feature", "Feature.cs"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            string scriptAssemblies = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
            Directory.CreateDirectory(scriptAssemblies);
            WriteFile(Path.Combine(scriptAssemblies, "Assembly-CSharp-firstpass.dll"), string.Empty);
            WriteFile(Path.Combine(scriptAssemblies, "Assembly-CSharp.dll"), string.Empty);
            string plugin = WriteFile(Path.Combine(projectRoot, "Assets", "Plugins", "DOTween.dll"), string.Empty);

            List<string> references = ObfuscatorProcessor.CollectReferencePaths(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new[] { source })
                .ToList();

            Assert.That(references.Any(path => Path.GetFileName(path).Equals("Assembly-CSharp-firstpass.dll", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(references.Any(path => Path.GetFileName(path).Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase) && path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(references, Does.Contain(plugin));
        }

        [Test]
        public void UnityEditorUsage_IsDetectedFromSelectedSources()
        {
            string source = WriteFile(Path.Combine(externalRoot, "EditorDependent.cs"), "using UnityEditor; public sealed class EditorDependent { }");
            Assert.That(ObfuscatorProcessor.SourcesRequireUnityEditor(new[] { source }), Is.True);
        }

        [Test]
        public void CompilationFailureSummary_ShowsErrorsAndOmitsWarnings()
        {
            string output =
                "Feature.cs(1,1): warning CS0436: duplicate type\n" +
                "Feature.cs(2,3): error CS0246: MissingType was not found\n" +
                "Build FAILED.";

            string summary = ObfuscatorProcessor.FormatCompilationFailure(output);

            Assert.That(summary, Does.Contain("1 error(s)"));
            Assert.That(summary, Does.Contain("CS0246"));
            Assert.That(summary, Does.Not.Contain("CS0436"));
        }

        private static string WriteFile(string path, string contents = "public sealed class TestType { }")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.dataPath);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
