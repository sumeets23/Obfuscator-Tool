using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace ScriptObfuscator.Tests
{
    [Category("ScriptObfuscatorIntegration")]
    public sealed class ObfuscatorIntegrationTests
    {
        private const string AssetFixtureFolder = "Assets/ScriptObfuscatorIntegrationTemp";
        private string externalFixtureFolder;

        [SetUp]
        public void SetUp()
        {
            externalFixtureFolder = Path.Combine(Path.GetTempPath(), "ScriptObfuscatorIntegration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalFixtureFolder);
            Directory.CreateDirectory(Path.Combine(ConfuserExManager.ProjectRoot, AssetFixtureFolder));
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(AssetFixtureFolder);
            if (Directory.Exists(externalFixtureFolder))
            {
                Directory.Delete(externalFixtureFolder, true);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void Build_ProjectSingleScript_WithBasicProtection()
        {
            string originalSource = WriteAssetScript("Single.cs", "IntegrationSingle");
            string backupRoot = Path.Combine(externalFixtureFolder, "ChosenBackupFolder");
            string output = AssertReplacement(new[] { AssetFixtureFolder + "/Single.cs" }, "Integration.Basic", true, false, false, backupRoot);

            Assert.That(File.Exists(originalSource), Is.False);
            Assert.That(output, Is.EqualTo(Path.Combine(Path.GetDirectoryName(originalSource) ?? string.Empty, "Integration.Basic.dll")));
            Assert.That(File.Exists(output), Is.True);
            Assert.That(Directory.GetFiles(backupRoot, "Single.cs", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        }

        [Test]
        public void Build_ProjectNestedFolder_WithBalancedProtection()
        {
            string first = WriteAssetScript("Root/Nested/First.cs", "IntegrationNestedFirst");
            string second = WriteAssetScript("Root/Nested/Second.cs", "IntegrationNestedSecond");
            string backupRoot = Path.Combine(externalFixtureFolder, "NestedBackup");
            string selectedFolder = Path.Combine(ConfuserExManager.ProjectRoot, AssetFixtureFolder, "Root");

            string output = AssertReplacement(new[] { AssetFixtureFolder + "/Root" }, "Integration.Balanced", true, false, true, backupRoot);

            Assert.That(output, Is.EqualTo(Path.Combine(selectedFolder, "Integration.Balanced.dll")));
            Assert.That(File.Exists(first), Is.False);
            Assert.That(File.Exists(second), Is.False);
            Assert.That(Directory.GetFiles(backupRoot, "*.cs", SearchOption.AllDirectories), Has.Length.EqualTo(2));
        }

        [Test]
        public void Build_ExternalFolder_WithStrongProtection()
        {
            string externalSource = WriteExternalScript("Root/Nested/External.cs", "IntegrationExternal");
            string editorSource = WriteExternalScript("Root/Nested/Editor/Excluded.cs", "IntegrationExternalEditorOnly");
            string selectedFolder = Path.Combine(externalFixtureFolder, "Root");
            string backupRoot = selectedFolder;

            string output = AssertReplacement(new[] { selectedFolder }, "Integration.Strong", true, true, true, backupRoot);

            Assert.That(output, Is.EqualTo(Path.Combine(selectedFolder, "Integration.Strong.dll")));
            Assert.That(File.Exists(externalSource), Is.False);
            Assert.That(File.Exists(editorSource), Is.True, "Editor folders are intentionally excluded from runtime DLL replacement.");
            Assert.That(Directory.GetFiles(backupRoot, "External.cs", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            Assert.That(Directory.GetDirectories(backupRoot, "*.SourceBackup~", SearchOption.TopDirectoryOnly), Has.Length.EqualTo(1));
        }

        [Test]
        public void Build_CanceledBackupSelection_LeavesOriginalUntouched()
        {
            string originalSource = WriteExternalScript("Cancel/Original.cs", "IntegrationCanceled");
            var config = CreateConfig(new[] { originalSource }, "Integration.Canceled", true, false, false);

            Assert.Throws<OperationCanceledException>(() => ObfuscatorProcessor.BuildAndReplace(config, () => string.Empty));

            Assert.That(File.Exists(originalSource), Is.True);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(originalSource) ?? string.Empty, "Integration.Canceled.dll")), Is.False);
        }

        private static string AssertReplacement(
            IEnumerable<string> sources,
            string assemblyName,
            bool rename,
            bool controlFlow,
            bool constants,
            string backupFolder)
        {
            ObfuscatorConfig config = CreateConfig(sources, assemblyName, rename, controlFlow, constants);
            string output = ObfuscatorProcessor.BuildAndReplace(config, () => backupFolder);
            Assert.That(File.Exists(output), Is.True);
            return output;
        }

        private static ObfuscatorConfig CreateConfig(
            IEnumerable<string> sources,
            string assemblyName,
            bool rename,
            bool controlFlow,
            bool constants)
        {
            return new ObfuscatorConfig
            {
                sourceAssetPaths = new List<string>(sources),
                dllName = assemblyName,
                enableRename = rename,
                enableControlFlow = controlFlow,
                enableStringEncryption = constants,
                overwriteOutput = true,
                removeSourcesAfterSuccess = false
            };
        }

        private static string WriteAssetScript(string relativePath, string typeName)
        {
            string path = Path.Combine(ConfuserExManager.ProjectRoot, AssetFixtureFolder, relativePath);
            WriteScript(path, typeName);
            return path;
        }

        private string WriteExternalScript(string relativePath, string typeName)
        {
            string path = Path.Combine(externalFixtureFolder, relativePath);
            WriteScript(path, typeName);
            return path;
        }

        private static void WriteScript(string path, string typeName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ConfuserExManager.ProjectRoot);
            File.WriteAllText(path, "namespace ScriptObfuscatorIntegration { public sealed class " + typeName + " { public string Value => \"protected\"; } }");
        }
    }
}
