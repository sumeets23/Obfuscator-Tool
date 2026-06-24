using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptObfuscator
{
    internal sealed class ObfuscatorWindow : EditorWindow
    {
        private const int CardRadius = 8;

        private ObfuscatorConfig config;
        private VisualElement sourceList;
        private HelpBox setupBox;
        private HelpBox selectionBox;
        private HelpBox sourcePolicyBox;
        private HelpBox statusBox;
        private Button runButton;
        private Label installedBadge;

        [MenuItem("Tools/Script Obfuscator")]
        public static void Open()
        {
            ObfuscatorWindow window = GetWindow<ObfuscatorWindow>();
            window.titleContent = new GUIContent("Script Obfuscator");
            window.minSize = new Vector2(860, 640);
        }

        private void OnEnable()
        {
            config = ObfuscatorConfig.Load();
        }

        private void OnDisable()
        {
            config?.Save();
        }

        public void CreateGUI()
        {
            if (config == null)
            {
                config = ObfuscatorConfig.Load();
            }

            rootVisualElement.Clear();
            rootVisualElement.styleSheets.Clear();
            BuildLayout(rootVisualElement);
            RefreshAll();
        }

        private void BuildLayout(VisualElement root)
        {
            root.style.backgroundColor = ColorFromHex("#f5f7fb");
            root.style.paddingLeft = 18;
            root.style.paddingRight = 18;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1
                }
            };
            root.Add(scrollView);

            VisualElement header = Row();
            header.style.marginBottom = 14;
            header.style.alignItems = Align.Center;

            VisualElement titleBlock = new VisualElement { style = { flexGrow = 1 } };
            Label title = new Label("Script Obfuscator");
            title.style.fontSize = 24;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorFromHex("#18202f");
            titleBlock.Add(title);

            Label subtitle = new Label("Compile selected Unity C# assemblies into an obfuscated DLL.");
            subtitle.style.fontSize = 12;
            subtitle.style.color = ColorFromHex("#647084");
            subtitle.style.marginTop = 3;
            titleBlock.Add(subtitle);
            header.Add(titleBlock);

            installedBadge = new Label();
            installedBadge.style.paddingLeft = 10;
            installedBadge.style.paddingRight = 10;
            installedBadge.style.paddingTop = 5;
            installedBadge.style.paddingBottom = 5;
            installedBadge.style.borderTopLeftRadius = 999;
            installedBadge.style.borderTopRightRadius = 999;
            installedBadge.style.borderBottomLeftRadius = 999;
            installedBadge.style.borderBottomRightRadius = 999;
            installedBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(installedBadge);
            scrollView.Add(header);

            VisualElement grid = Row();
            grid.style.alignItems = Align.Stretch;
            scrollView.Add(grid);

            VisualElement leftColumn = Column();
            leftColumn.style.flexGrow = 1.25f;
            leftColumn.style.marginRight = 12;
            grid.Add(leftColumn);

            VisualElement rightColumn = Column();
            rightColumn.style.flexGrow = 1;
            grid.Add(rightColumn);

            leftColumn.Add(BuildSetupCard());
            leftColumn.Add(BuildSourcesCard());
            rightColumn.Add(BuildOutputCard());
            rightColumn.Add(BuildProtectionCard());
            rightColumn.Add(BuildActionCard());
        }

        private VisualElement BuildSetupCard()
        {
            VisualElement card = Card("ConfuserEx Setup");

            TextField cliPath = new TextField("Expected CLI");
            cliPath.value = ConfuserExManager.ExpectedCliPath;
            cliPath.isReadOnly = true;
            StyleField(cliPath);
            card.Add(cliPath);

            setupBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            setupBox.style.marginTop = 8;
            card.Add(setupBox);

            Button setupButton = PrimaryButton("Download / Setup");
            setupButton.style.marginTop = 8;
            setupButton.clicked += () =>
            {
                ConfuserExManager.OpenSetupPage();
                RefreshAll();
            };
            card.Add(setupButton);

            return card;
        }

        private VisualElement BuildSourcesCard()
        {
            VisualElement card = Card("Source Selection");

            VisualElement dropZone = new VisualElement();
            dropZone.style.height = 92;
            dropZone.style.borderTopWidth = 1;
            dropZone.style.borderRightWidth = 1;
            dropZone.style.borderBottomWidth = 1;
            dropZone.style.borderLeftWidth = 1;
            dropZone.style.borderTopColor = ColorFromHex("#b8c2d6");
            dropZone.style.borderRightColor = ColorFromHex("#b8c2d6");
            dropZone.style.borderBottomColor = ColorFromHex("#b8c2d6");
            dropZone.style.borderLeftColor = ColorFromHex("#b8c2d6");
            dropZone.style.borderTopLeftRadius = CardRadius;
            dropZone.style.borderTopRightRadius = CardRadius;
            dropZone.style.borderBottomLeftRadius = CardRadius;
            dropZone.style.borderBottomRightRadius = CardRadius;
            dropZone.style.backgroundColor = ColorFromHex("#eef4ff");
            dropZone.style.alignItems = Align.Center;
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.marginBottom = 10;

            Label dropTitle = new Label("Drop C# files or folders");
            dropTitle.style.fontSize = 14;
            dropTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            dropTitle.style.color = ColorFromHex("#23324a");
            dropZone.Add(dropTitle);

            Label dropSubtitle = new Label("Asmdef sources expand automatically so dependencies compile together.");
            dropSubtitle.style.fontSize = 11;
            dropSubtitle.style.color = ColorFromHex("#62718a");
            dropSubtitle.style.marginTop = 3;
            dropZone.Add(dropSubtitle);

            dropZone.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            dropZone.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
                {
                    AddAssetPath(AssetDatabase.GetAssetPath(draggedObject));
                }

                foreach (string path in DragAndDrop.paths)
                {
                    AddAssetPath(path);
                }

                evt.StopPropagation();
            });
            card.Add(dropZone);

            VisualElement buttons = Row();
            Button addScript = SecondaryButton("Add Script");
            addScript.clicked += () => AddAbsolutePath(EditorUtility.OpenFilePanel("Add C# Script", Application.dataPath, "cs"));
            buttons.Add(addScript);

            Button addFolder = SecondaryButton("Add Folder");
            addFolder.clicked += () => AddAbsolutePath(EditorUtility.OpenFolderPanel("Add Folder", Application.dataPath, string.Empty));
            buttons.Add(addFolder);

            Button clear = SecondaryButton("Clear");
            clear.style.maxWidth = 86;
            clear.clicked += () =>
            {
                config.sourceAssetPaths.Clear();
                SaveAndRefresh();
            };
            buttons.Add(clear);
            card.Add(buttons);

            sourceList = new VisualElement();
            sourceList.style.marginTop = 10;
            card.Add(sourceList);

            selectionBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            selectionBox.style.marginTop = 8;
            card.Add(selectionBox);

            return card;
        }

        private VisualElement BuildOutputCard()
        {
            VisualElement card = Card("Build Output");

            TextField dllName = new TextField("DLL Name") { value = config.dllName };
            StyleField(dllName);
            dllName.RegisterValueChangedCallback(evt =>
            {
                config.dllName = evt.newValue;
                SaveOnly();
            });
            card.Add(dllName);

            TextField targetFramework = new TextField("Target Framework") { value = config.targetFramework };
            StyleField(targetFramework);
            targetFramework.RegisterValueChangedCallback(evt =>
            {
                config.targetFramework = evt.newValue;
                SaveOnly();
            });
            card.Add(targetFramework);

            card.Add(ToggleField("Include UnityEditor references", config.includeEditorReferences, value =>
            {
                config.includeEditorReferences = value;
                SaveAndRefresh();
            }));
            card.Add(ToggleField("Allow unsafe code", config.allowUnsafeCode, value =>
            {
                config.allowUnsafeCode = value;
                SaveOnly();
            }));
            card.Add(ToggleField("Overwrite existing DLL", config.overwriteOutput, value =>
            {
                config.overwriteOutput = value;
                SaveOnly();
            }));
            sourcePolicyBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            sourcePolicyBox.style.marginTop = 8;
            card.Add(sourcePolicyBox);

            return card;
        }

        private VisualElement BuildProtectionCard()
        {
            VisualElement card = Card("Protections");

            card.Add(ToggleField("Rename symbols", config.enableRename, value =>
            {
                config.enableRename = value;
                SaveOnly();
            }));
            card.Add(ToggleField("Control flow", config.enableControlFlow, value =>
            {
                config.enableControlFlow = value;
                SaveOnly();
            }));
            card.Add(ToggleField("String encryption / constants", config.enableStringEncryption, value =>
            {
                config.enableStringEncryption = value;
                SaveOnly();
            }));

            HelpBox note = new HelpBox(
                "Anti-tamper and anti-dump are intentionally excluded because they commonly break Unity assembly loading or IL2CPP builds.",
                HelpBoxMessageType.Info);
            note.style.marginTop = 8;
            card.Add(note);

            return card;
        }

        private VisualElement BuildActionCard()
        {
            VisualElement card = Card("Run");

            statusBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            statusBox.style.display = DisplayStyle.None;
            card.Add(statusBox);

            runButton = PrimaryButton("Build Obfuscated DLL");
            runButton.style.marginTop = 10;
            runButton.style.height = 42;
            runButton.clicked += Run;
            card.Add(runButton);

            Label footer = new Label("The DLL is obfuscated. The source files must be moved out of Assets if you do not want them in the package.");
            footer.style.fontSize = 11;
            footer.style.color = ColorFromHex("#66758a");
            footer.style.whiteSpace = WhiteSpace.Normal;
            footer.style.marginTop = 8;
            card.Add(footer);

            return card;
        }

        private void RefreshAll()
        {
            bool installed = ConfuserExManager.IsInstalled;
            installedBadge.text = installed ? "ConfuserEx Ready" : "Setup Required";
            installedBadge.style.backgroundColor = installed ? ColorFromHex("#dff5e7") : ColorFromHex("#fff1d7");
            installedBadge.style.color = installed ? ColorFromHex("#146c3e") : ColorFromHex("#8a5a00");

            setupBox.text = installed
                ? "ConfuserEx CLI found. The tool can build and obfuscate DLLs."
                : "ConfuserEx is not installed. Extract the release ZIP so Confuser.CLI.exe is directly inside Tools/ConfuserEx.";
            setupBox.messageType = installed ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;

            if (runButton != null)
            {
                runButton.SetEnabled(installed);
            }

            RefreshSourceList();
            RefreshPolicyMessage();
        }

        private void RefreshSourceList()
        {
            sourceList.Clear();

            for (int i = 0; i < config.sourceAssetPaths.Count; i++)
            {
                int index = i;
                VisualElement row = Row();
                row.style.backgroundColor = Color.white;
                row.style.borderTopWidth = 1;
                row.style.borderRightWidth = 1;
                row.style.borderBottomWidth = 1;
                row.style.borderLeftWidth = 1;
                row.style.borderTopColor = ColorFromHex("#e3e8f0");
                row.style.borderRightColor = ColorFromHex("#e3e8f0");
                row.style.borderBottomColor = ColorFromHex("#e3e8f0");
                row.style.borderLeftColor = ColorFromHex("#e3e8f0");
                row.style.borderTopLeftRadius = 6;
                row.style.borderTopRightRadius = 6;
                row.style.borderBottomLeftRadius = 6;
                row.style.borderBottomRightRadius = 6;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 6;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.marginBottom = 6;
                row.style.alignItems = Align.Center;

                Label path = new Label(config.sourceAssetPaths[i]);
                path.style.flexGrow = 1;
                path.style.color = ColorFromHex("#2f3b4f");
                path.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(path);

                Button remove = SecondaryButton("Remove");
                remove.style.maxWidth = 78;
                remove.clicked += () =>
                {
                    config.sourceAssetPaths.RemoveAt(index);
                    SaveAndRefresh();
                };
                row.Add(remove);
                sourceList.Add(row);
            }

            List<string> expanded = ObfuscatorProcessor.ExpandSourceFiles(config.sourceAssetPaths);
            string expansionText = config.sourceAssetPaths.Count == expanded.Count
                ? expanded.Count + " runtime C# file(s) selected."
                : config.sourceAssetPaths.Count + " selected item(s) expand to " + expanded.Count + " runtime C# file(s).";
            selectionBox.text = expansionText + " Files inside an asmdef compile together; Editor folders are skipped.";
        }

        private void RefreshPolicyMessage()
        {
            if (config.includeEditorReferences)
            {
                sourcePolicyBox.text = "UnityEditor references make the DLL editor-only. Disable this for runtime/player packages.";
                sourcePolicyBox.messageType = HelpBoxMessageType.Warning;
                return;
            }

            sourcePolicyBox.text = "After obfuscation succeeds, choose a backup folder. The DLL is placed at the selected source location before originals are removed.";
            sourcePolicyBox.messageType = HelpBoxMessageType.Warning;
        }

        private void Run()
        {
            SaveOnly();

            try
            {
                SetStatus("Building and obfuscating selected sources...", HelpBoxMessageType.Info);
                EditorUtility.DisplayProgressBar("Script Obfuscator", "Building and obfuscating scripts...", 0.5f);
                string output = ObfuscatorProcessor.BuildAndReplace(config, SelectBackupFolderAfterBuild);
                SetStatus("Created " + output + ". Original sources were backed up and replaced.", HelpBoxMessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, HelpBoxMessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshAll();
            }
        }

        private static string SelectBackupFolderAfterBuild()
        {
            EditorUtility.ClearProgressBar();
            return EditorUtility.OpenFolderPanel(
                "Obfuscation Complete - Choose Original Source Backup Folder",
                ConfuserExManager.ProjectRoot,
                string.Empty);
        }

        private void AddAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            if (!ObfuscatorProcessor.TryNormalizeSourcePath(absolutePath, out string sourcePath))
            {
                SetStatus("The selected path could not be read.", HelpBoxMessageType.Warning);
                return;
            }

            AddAssetPath(sourcePath);
        }

        private void AddAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            if (!ObfuscatorProcessor.TryNormalizeSourcePath(assetPath, out string normalized) ||
                !ObfuscatorProcessor.IsValidSourcePath(normalized))
            {
                SetStatus("Select an existing C# file or folder. Assets paths and absolute external paths are supported.", HelpBoxMessageType.Warning);
                return;
            }

            if (!config.sourceAssetPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                config.sourceAssetPaths.Add(normalized);
                config.sourceAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
                SaveAndRefresh();
            }
        }

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            statusBox.text = message;
            statusBox.messageType = messageType;
            statusBox.style.display = DisplayStyle.Flex;
        }

        private void SaveAndRefresh()
        {
            SaveOnly();
            RefreshAll();
        }

        private void SaveOnly()
        {
            config.Save();
        }

        private static VisualElement Card(string title)
        {
            VisualElement card = Column();
            card.style.backgroundColor = Color.white;
            card.style.borderTopLeftRadius = CardRadius;
            card.style.borderTopRightRadius = CardRadius;
            card.style.borderBottomLeftRadius = CardRadius;
            card.style.borderBottomRightRadius = CardRadius;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 14;
            card.style.marginBottom = 12;
            card.style.borderTopWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderTopColor = ColorFromHex("#dde4ef");
            card.style.borderRightColor = ColorFromHex("#dde4ef");
            card.style.borderBottomColor = ColorFromHex("#dde4ef");
            card.style.borderLeftColor = ColorFromHex("#dde4ef");

            Label heading = new Label(title);
            heading.style.fontSize = 14;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = ColorFromHex("#1f2a3d");
            heading.style.marginBottom = 8;
            card.Add(heading);

            return card;
        }

        private static VisualElement Row()
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };
        }

        private static VisualElement Column()
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column
                }
            };
        }

        private static Button PrimaryButton(string text)
        {
            Button button = new Button { text = text };
            button.style.backgroundColor = ColorFromHex("#2563eb");
            button.style.color = Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.height = 32;
            return button;
        }

        private static Button SecondaryButton(string text)
        {
            Button button = new Button { text = text };
            button.style.backgroundColor = ColorFromHex("#eef2f7");
            button.style.color = ColorFromHex("#26364d");
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopColor = ColorFromHex("#d8e0eb");
            button.style.borderRightColor = ColorFromHex("#d8e0eb");
            button.style.borderBottomColor = ColorFromHex("#d8e0eb");
            button.style.borderLeftColor = ColorFromHex("#d8e0eb");
            button.style.height = 30;
            button.style.flexGrow = 1;
            button.style.marginRight = 6;
            return button;
        }

        private static Toggle ToggleField(string text, bool value, Action<bool> onChanged)
        {
            Toggle toggle = new Toggle(text) { value = value };
            toggle.style.marginTop = 5;
            toggle.style.marginBottom = 5;
            toggle.style.color = ColorFromHex("#26364d");
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return toggle;
        }

        private static void StyleField(TextField field)
        {
            field.style.marginBottom = 7;
            field.style.color = ColorFromHex("#26364d");
        }

        private static Color ColorFromHex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }
    }
}
