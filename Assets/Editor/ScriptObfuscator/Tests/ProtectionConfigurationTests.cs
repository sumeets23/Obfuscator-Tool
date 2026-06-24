using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace ScriptObfuscator.Tests
{
    public sealed class ProtectionConfigurationTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ScriptObfuscatorProtectionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [TestCase(true, false, false, true, false, false)]
        [TestCase(true, false, true, true, false, true)]
        [TestCase(true, true, true, true, true, true)]
        [TestCase(false, true, false, false, true, false)]
        public void ProjectXml_ContainsExactlyEnabledProtections(
            bool rename,
            bool controlFlow,
            bool constants,
            bool expectRename,
            bool expectControlFlow,
            bool expectConstants)
        {
            var config = new ObfuscatorConfig
            {
                enableRename = rename,
                enableControlFlow = controlFlow,
                enableStringEncryption = constants
            };

            string project = ConfuserExManager.CreateProjectFile(
                Path.Combine(tempRoot, "Input.dll"),
                Path.Combine(tempRoot, "Output"),
                Path.Combine(tempRoot, "Project"),
                config,
                new List<string>());
            string xml = File.ReadAllText(project);

            Assert.That(xml.Contains("protection id=\"rename\""), Is.EqualTo(expectRename));
            Assert.That(xml.Contains("protection id=\"ctrl flow\""), Is.EqualTo(expectControlFlow));
            Assert.That(xml.Contains("protection id=\"constants\""), Is.EqualTo(expectConstants));
        }
    }
}
