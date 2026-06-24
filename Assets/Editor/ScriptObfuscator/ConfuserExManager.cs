using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ScriptObfuscator
{
    internal static class ConfuserExManager
    {
        private const string ReleaseUrl = "https://github.com/mkaring/ConfuserEx/releases/latest";
        private const string ToolFolder = "Tools/ConfuserEx";
        private const string CliName = "Confuser.CLI.exe";

        public static string ExpectedCliPath => Path.Combine(ProjectRoot, ToolFolder, CliName);
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static bool IsInstalled => File.Exists(ExpectedCliPath);

        public static void OpenSetupPage()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ToolFolder));
            Application.OpenURL(ReleaseUrl);
            EditorUtility.RevealInFinder(Path.Combine(ProjectRoot, ToolFolder));
        }

        public static string CreateProjectFile(
            string inputAssembly,
            string outputDirectory,
            string projectDirectory,
            ObfuscatorConfig config,
            IEnumerable<string> probePaths)
        {
            Directory.CreateDirectory(projectDirectory);
            string projectPath = Path.Combine(projectDirectory, "confuser.crproj");
            var protections = new List<string>();

            if (config.enableRename)
            {
                protections.Add("      <protection id=\"rename\" />");
            }

            if (config.enableControlFlow)
            {
                protections.Add("      <protection id=\"ctrl flow\" />");
            }

            if (config.enableStringEncryption)
            {
                protections.Add("      <protection id=\"constants\" />");
            }

            string probes = string.Join("\n", (probePaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => "  <probePath>" + EscapeXml(path) + "</probePath>"));

            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                $"<project outputDir=\"{EscapeXml(outputDirectory)}\" baseDir=\"{EscapeXml(Path.GetDirectoryName(inputAssembly) ?? string.Empty)}\">\n" +
                probes + (string.IsNullOrWhiteSpace(probes) ? string.Empty : "\n") +
                $"  <module path=\"{EscapeXml(inputAssembly)}\">\n" +
                "    <rule pattern=\"true\" preset=\"none\" inherit=\"false\">\n" +
                string.Join("\n", protections) + "\n" +
                "    </rule>\n" +
                "  </module>\n" +
                "</project>\n";

            File.WriteAllText(projectPath, xml, new UTF8Encoding(false));
            return projectPath;
        }

        public static void Run(string confuserProjectPath)
        {
            if (!IsInstalled)
            {
                throw new FileNotFoundException("ConfuserEx CLI was not found.", ExpectedCliPath);
            }

            ProcessResult result = RunProcess(ExpectedCliPath, "-n " + Quote(confuserProjectPath), ProjectRoot);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("ConfuserEx failed.\n" + result.CombinedOutput);
            }

            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
            {
                Debug.Log(result.CombinedOutput);
            }
        }

        public static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Could not start process: " + fileName);
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new ProcessResult(process.ExitCode, stdout, stderr);
            }
        }

        public static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }

    internal readonly struct ProcessResult
    {
        public readonly int ExitCode;
        public readonly string StandardOutput;
        public readonly string StandardError;

        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public string CombinedOutput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StandardError))
                {
                    return StandardOutput;
                }

                if (string.IsNullOrWhiteSpace(StandardOutput))
                {
                    return StandardError;
                }

                return StandardOutput + "\n" + StandardError;
            }
        }
    }
}
