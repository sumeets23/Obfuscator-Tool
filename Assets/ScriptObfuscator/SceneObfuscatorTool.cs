using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ScriptObfuscator.SceneUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class SceneObfuscatorTool : MonoBehaviour
    {
        [SerializeField] private string dllName = "MyPackage.Core";
        [SerializeField] private string outputFolder = "Assets/Plugins/Obfuscated";
        [SerializeField] private string targetFramework = "netstandard2.1";
        [SerializeField] private ProtectionLevel protectionLevel = ProtectionLevel.Strong;
        [SerializeField] private bool enableRename = true;
        [SerializeField] private bool enableControlFlow = true;
        [SerializeField] private bool enableStringEncryption = true;
        [SerializeField] private bool allowUnsafeCode;
        [SerializeField] private bool overwriteOutput = true;
        [SerializeField] private bool moveSourcesOutOfAssets;
        [SerializeField] private string sourceBackupFolder = "ScriptObfuscatorBackups";
        [SerializeField] private string sourcePathInput = string.Empty;
        [SerializeField] private List<string> sourceAssetPaths = new List<string>();
        [SerializeField] private List<GameObject> selectedSceneRoots = new List<GameObject>();

        private UIDocument document;
        private VisualElement sourceList;
        private Label selectionSummary;
        private Label statusLabel;
        private ScrollView statusScroll;
        private TextField pathField;
        private Button runButton;
        private Button basicProtectionButton;
        private Button balancedProtectionButton;
        private Button strongProtectionButton;
        private Label protectionLevelSummary;
        private Font uiFont;
        private bool buildQueued;
        private int editorBuildAttempts;

        private enum ProtectionLevel
        {
            Basic,
            Balanced,
            Strong,
            Custom
        }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            EnsurePanelSettings();
            ConfigureDocumentForGameView();
        }

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            EnsurePanelSettings();
            ConfigureDocumentForGameView();

            if (!Application.isEditor)
            {
                if (document != null)
                {
                    document.enabled = false;
                }

                enabled = false;
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RepairEditorSceneInput();
            }

            Selection.selectionChanged -= OnEditorSelectionChanged;
            Selection.selectionChanged += OnEditorSelectionChanged;
            CaptureEditorSelection();
#endif

            QueueBuildUi();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            Selection.selectionChanged -= OnEditorSelectionChanged;
#endif
        }

        private void OnValidate()
        {
            if (!Application.isPlaying && selectionSummary != null)
            {
                Refresh();
            }
        }

        private void Start()
        {
            QueueBuildUi();
        }

        private void QueueBuildUi()
        {
            if (buildQueued)
            {
                return;
            }

            buildQueued = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                editorBuildAttempts = 0;
                EditorApplication.delayCall += DelayedEditorBuildUi;
                return;
            }
#endif

            StartCoroutine(BuildWhenDocumentIsReady());
        }

        private System.Collections.IEnumerator BuildWhenDocumentIsReady()
        {
            for (int i = 0; i < 30; i++)
            {
                if (TryBuildUi())
                {
                    yield break;
                }

                yield return null;
            }

            buildQueued = false;
            Debug.LogError("[SceneObfuscatorTool] UIDocument rootVisualElement was not ready. Make sure the GameObject has a UIDocument with Panel Settings.");
        }

#if UNITY_EDITOR
        private void DelayedEditorBuildUi()
        {
            if (this == null)
            {
                return;
            }

            if (TryBuildUi())
            {
                return;
            }

            editorBuildAttempts++;
            if (editorBuildAttempts < 30)
            {
                buildQueued = true;
                EditorApplication.delayCall += DelayedEditorBuildUi;
                return;
            }

            Debug.LogError("[SceneObfuscatorTool] UIDocument rootVisualElement was not ready. Make sure the GameObject has a UIDocument with Panel Settings.");
        }
#endif

        private bool TryBuildUi()
        {
            document = GetComponent<UIDocument>();
            EnsurePanelSettings();
            ConfigureDocumentForGameView();

            if (document == null || document.panelSettings == null || document.rootVisualElement == null)
            {
                buildQueued = false;
                return false;
            }

            buildQueued = false;
            BuildUi();
            return true;
        }

        private void EnsurePanelSettings()
        {
            if (document == null || document.panelSettings != null)
            {
                return;
            }

#if UNITY_EDITOR
            const string panelSettingsPath = "Assets/ScriptObfuscator/SceneObfuscatorPanelSettings.asset";
            PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PanelSettings>();
                asset.name = "SceneObfuscatorPanelSettings";
                asset.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                asset.referenceResolution = new Vector2Int(1280, 720);
                AssetDatabase.CreateAsset(asset, panelSettingsPath);
                AssetDatabase.SaveAssets();
            }

            document.panelSettings = asset;
#else
            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Scene Obfuscator Panel Settings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 720);
            document.panelSettings = panelSettings;
#endif
        }

        private void ConfigureDocumentForGameView()
        {
            if (document == null)
            {
                return;
            }

            document.enabled = true;
#if UNITY_EDITOR
            SetPropertyIfAvailable(document, "sortingOrder", 1000);
            SetEnumPropertyIfAvailable(document, "position", "Overlay", "ScreenSpaceOverlay", "PanelSettings");
#endif
        }

        private void BuildUi()
        {
            document = GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = document.rootVisualElement;
            root.Clear();
            ApplyTextDefaults(root);
            root.style.backgroundColor = ColorFromHex("#eef2f7");
            root.style.flexGrow = 1;
            root.style.width = Length.Percent(100);
            root.style.height = Length.Percent(100);
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.paddingLeft = 20;
            root.style.paddingRight = 20;
            root.style.paddingTop = 18;
            root.style.paddingBottom = 18;

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            VisualElement shell = new VisualElement();
            shell.style.maxWidth = 1440;
            shell.style.alignSelf = Align.Center;
            shell.style.width = Length.Percent(100);
            scroll.Add(shell);

            VisualElement header = Row();
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 18;
            shell.Add(header);

            Label title = new Label("Obfuscator tool");
            title.style.fontSize = 25;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorFromHex("#172033");
            header.Add(title);

            VisualElement grid = Row();
            grid.style.alignItems = Align.FlexStart;
            shell.Add(grid);

            VisualElement left = Column();
            left.style.flexGrow = 1.2f;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;
            left.style.marginRight = 16;
            grid.Add(left);

            VisualElement right = Column();
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            right.style.minWidth = 0;
            grid.Add(right);

            left.Add(BuildSourceCard());
            right.Add(BuildOutputCard());
            right.Add(BuildProtectionCard());
            right.Add(BuildRunCard());

            ApplyTextDefaults(root);
            root.schedule.Execute(() => ApplyTextDefaults(root)).ExecuteLater(50);
            Refresh();
        }

        private VisualElement BuildSourceCard()
        {
            VisualElement card = Card("Sources");

            pathField = new TextField();
            pathField.value = sourcePathInput;
            pathField.RegisterValueChangedCallback(evt => sourcePathInput = evt.newValue);
            StyleTextField(pathField);
            card.Add(LabeledRow("Asset path", pathField));

            VisualElement pathButtons = Row();
            pathButtons.style.marginTop = 2;
            pathButtons.style.marginBottom = 12;
            Button addPath = SecondaryButton("Add Path");
            addPath.clicked += () => AddAssetPath(pathField.value);
            pathButtons.Add(addPath);

            Button addScript = SecondaryButton("Browse Script");
            addScript.clicked += BrowseScript;
            pathButtons.Add(addScript);

            Button addFolder = SecondaryButton("Browse Folder");
            addFolder.clicked += BrowseFolder;
            pathButtons.Add(addFolder);
            card.Add(pathButtons);

            Button addSelection = SecondaryButton("Add Selected GameObject");
            addSelection.style.marginBottom = 12;
            addSelection.clicked += AddSelectedObjects;
            card.Add(addSelection);

            VisualElement dropZone = new VisualElement();
            dropZone.style.height = 96;
            dropZone.style.marginTop = 0;
            dropZone.style.marginBottom = 12;
            dropZone.style.backgroundColor = ColorFromHex("#f0f6ff");
            dropZone.style.borderTopWidth = 1;
            dropZone.style.borderRightWidth = 1;
            dropZone.style.borderBottomWidth = 1;
            dropZone.style.borderLeftWidth = 1;
            dropZone.style.borderTopColor = ColorFromHex("#b8c7e2");
            dropZone.style.borderRightColor = ColorFromHex("#b8c7e2");
            dropZone.style.borderBottomColor = ColorFromHex("#b8c7e2");
            dropZone.style.borderLeftColor = ColorFromHex("#b8c7e2");
            dropZone.style.borderTopLeftRadius = 7;
            dropZone.style.borderTopRightRadius = 7;
            dropZone.style.borderBottomLeftRadius = 7;
            dropZone.style.borderBottomRightRadius = 7;
            dropZone.style.alignItems = Align.Center;
            dropZone.style.justifyContent = Justify.Center;
            dropZone.Add(StrongLabel("Drop scripts, folders, or GameObjects here"));
            Label dropNote = new Label("GameObjects add MonoBehaviour scripts from their children.");
            dropNote.style.fontSize = 11;
            dropNote.style.color = ColorFromHex("#637086");
            dropNote.style.marginTop = 2;
            dropZone.Add(dropNote);
            RegisterDropZone(dropZone);
            card.Add(dropZone);

            sourceList = new VisualElement();
            card.Add(sourceList);

            selectionSummary = InfoLabel();
            selectionSummary.style.marginTop = 2;
            card.Add(selectionSummary);

            Button clear = SecondaryButton("Clear Sources");
            clear.style.marginTop = 10;
            clear.style.flexGrow = 0;
            clear.style.width = 150;
            clear.clicked += () =>
            {
                sourceAssetPaths.Clear();
                selectedSceneRoots.Clear();
                Refresh();
            };
            card.Add(clear);

            return card;
        }

        private VisualElement BuildOutputCard()
        {
            VisualElement card = Card("Output");
            card.Add(BoundTextField("DLL Name", dllName, value => dllName = value));
            card.Add(BoundTextField("Target Framework", targetFramework, value => targetFramework = value));
            card.Add(BoundToggle("Allow unsafe code", allowUnsafeCode, value => allowUnsafeCode = value));
            card.Add(BoundToggle("Overwrite existing DLL", overwriteOutput, value => overwriteOutput = value));
            Label destinationNote = InfoLabel();
            destinationNote.text = "The DLL replaces selected sources at their current location. Backup destination is requested after obfuscation succeeds.";
            destinationNote.style.marginTop = 6;
            card.Add(destinationNote);
            return card;
        }

        private VisualElement BuildProtectionCard()
        {
            VisualElement card = Card("Protections");

            Label levelLabel = new Label("Protection Level");
            levelLabel.style.fontSize = 12;
            levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            levelLabel.style.color = ColorFromHex("#26364d");
            levelLabel.style.marginBottom = 6;
            card.Add(levelLabel);

            VisualElement levels = Row();
            levels.style.marginBottom = 16;
            basicProtectionButton = LevelButton("Basic");
            basicProtectionButton.clicked += () => ApplyProtectionLevel(ProtectionLevel.Basic);
            levels.Add(basicProtectionButton);

            balancedProtectionButton = LevelButton("Balanced");
            balancedProtectionButton.clicked += () => ApplyProtectionLevel(ProtectionLevel.Balanced);
            levels.Add(balancedProtectionButton);

            strongProtectionButton = LevelButton("Strong");
            strongProtectionButton.clicked += () => ApplyProtectionLevel(ProtectionLevel.Strong);
            levels.Add(strongProtectionButton);
            card.Add(levels);

            protectionLevelSummary = InfoLabel();
            protectionLevelSummary.style.marginBottom = 12;
            card.Add(protectionLevelSummary);

            Label advancedLabel = new Label("Advanced");
            advancedLabel.style.fontSize = 12;
            advancedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            advancedLabel.style.color = ColorFromHex("#26364d");
            advancedLabel.style.marginTop = 0;
            advancedLabel.style.marginBottom = 8;
            card.Add(advancedLabel);

            card.Add(BoundToggle("Rename symbols", enableRename, value =>
            {
                protectionLevel = ProtectionLevel.Custom;
                enableRename = value;
                RefreshProtectionLevelVisuals();
            }));
            card.Add(BoundToggle("Control flow", enableControlFlow, value =>
            {
                protectionLevel = ProtectionLevel.Custom;
                enableControlFlow = value;
                RefreshProtectionLevelVisuals();
            }));
            card.Add(BoundToggle("String encryption / constants", enableStringEncryption, value =>
            {
                protectionLevel = ProtectionLevel.Custom;
                enableStringEncryption = value;
                RefreshProtectionLevelVisuals();
            }));

            RefreshProtectionLevelVisuals();

            return card;
        }

        private VisualElement BuildRunCard()
        {
            VisualElement card = Card("Run");

            statusScroll = new ScrollView(ScrollViewMode.Vertical);
            statusScroll.style.display = DisplayStyle.None;
            statusScroll.style.height = 240;
            statusScroll.style.maxHeight = 240;
            statusScroll.style.overflow = Overflow.Hidden;
            statusScroll.contentViewport.style.overflow = Overflow.Hidden;
            statusScroll.style.marginBottom = 10;
            statusLabel = InfoLabel();
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.flexShrink = 0;
            statusScroll.Add(statusLabel);
            card.Add(statusScroll);

            runButton = PrimaryButton("Build Obfuscated DLL");
            runButton.style.height = 48;
            runButton.style.marginTop = 10;
            runButton.clicked += RunObfuscator;
            card.Add(runButton);

            return card;
        }

        private void Refresh()
        {
            RefreshSourceList();
        }

        private void RefreshSourceList()
        {
            if (sourceList == null)
            {
                return;
            }

            sourceList.Clear();
#if UNITY_EDITOR
            RemoveMissingSceneRoots();
#endif

            foreach (GameObject sceneRoot in selectedSceneRoots)
            {
                if (sceneRoot == null)
                {
                    continue;
                }

                GameObject capturedRoot = sceneRoot;
                VisualElement row = Row();
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 6;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.backgroundColor = ColorFromHex("#eef6ff");
                row.style.borderTopLeftRadius = 6;
                row.style.borderTopRightRadius = 6;
                row.style.borderBottomLeftRadius = 6;
                row.style.borderBottomRightRadius = 6;

                Label path = new Label("Scene Root: " + capturedRoot.name);
                path.style.flexGrow = 1;
                path.style.color = ColorFromHex("#26364d");
                row.Add(path);

                Button remove = SecondaryButton("Remove");
                remove.style.width = 96;
                remove.style.minWidth = 96;
                remove.style.whiteSpace = WhiteSpace.NoWrap;
                remove.clicked += () =>
                {
                    selectedSceneRoots.Remove(capturedRoot);
                    Refresh();
                };
                row.Add(remove);
                sourceList.Add(row);
            }

            for (int i = 0; i < sourceAssetPaths.Count; i++)
            {
                int index = i;
                VisualElement row = Row();
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 6;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.backgroundColor = ColorFromHex("#f8fafc");
                row.style.borderTopLeftRadius = 6;
                row.style.borderTopRightRadius = 6;
                row.style.borderBottomLeftRadius = 6;
                row.style.borderBottomRightRadius = 6;

                Label path = new Label(sourceAssetPaths[i]);
                path.style.flexGrow = 1;
                path.style.color = ColorFromHex("#26364d");
                row.Add(path);

                Button remove = SecondaryButton("Remove");
                remove.style.width = 96;
                remove.style.minWidth = 96;
                remove.style.whiteSpace = WhiteSpace.NoWrap;
                remove.clicked += () =>
                {
                    sourceAssetPaths.RemoveAt(index);
                    Refresh();
                };
                row.Add(remove);
                sourceList.Add(row);
            }

            int inputCount = selectedSceneRoots.Count + sourceAssetPaths.Count;
            int expanded = CountExpandedSources();
            string text = inputCount == expanded
                ? expanded + " runtime C# file(s) selected."
                : inputCount + " selected item(s) expand to " + expanded + " runtime C# file(s).";
            selectionSummary.text = text + " Source .cs files are not obfuscated; the compiled DLL is.";
        }

        private void AddAssetPath(string assetPath)
        {
            if (!TryNormalizeSourcePath(assetPath, out string normalizedSourcePath))
            {
                SetStatus("Enter an Assets path or an absolute path to a C# file/folder.", true);
                return;
            }

            if (!IsValidSourcePath(normalizedSourcePath))
            {
                string absolutePath = SourcePathToAbsolutePath(normalizedSourcePath);
                string error = File.Exists(absolutePath)
                    ? "The selected file is not a C# script: " + normalizedSourcePath
                    : "The selected source path does not exist: " + normalizedSourcePath;
                SetStatus(error, true);
                return;
            }

            if (!sourceAssetPaths.Contains(normalizedSourcePath, StringComparer.OrdinalIgnoreCase))
            {
                sourceAssetPaths.Add(normalizedSourcePath);
                sourceAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
                SetStatus("Added " + normalizedSourcePath, false);
            }

            Refresh();
        }

        private void BrowseScript()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Add C# Script", Application.dataPath, "cs");
            AddAbsolutePath(path);
#else
            SetStatus("Browsing project scripts is available only in the Unity Editor.", true);
#endif
        }

        private void BrowseFolder()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFolderPanel("Add Folder", Application.dataPath, string.Empty);
            AddAbsolutePath(path);
#else
            SetStatus("Browsing project folders is available only in the Unity Editor.", true);
#endif
        }

        private void BrowseBackupFolder()
        {
#if UNITY_EDITOR
            string initialFolder = ResolveBackupFolderForPicker();
            string path = EditorUtility.OpenFolderPanel("Choose Source Backup Folder", initialFolder, string.Empty);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (IsPathWithin(path, Application.dataPath))
            {
                SetStatus("Choose a backup folder outside Assets so Unity does not compile the moved scripts.", true);
                return;
            }

            sourceBackupFolder = Path.GetFullPath(path);
            QueueBuildUi();
            SetStatus("Source backups will be stored under " + sourceBackupFolder, false);
#else
            SetStatus("Choosing a backup folder is available only in the Unity Editor.", true);
#endif
        }

        private string ResolveBackupFolderForPicker()
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(sourceBackupFolder)
                    ? Path.Combine(Application.dataPath, "..", "ScriptObfuscatorBackups")
                    : sourceBackupFolder;
                string fullPath = Path.GetFullPath(Path.IsPathRooted(folder)
                    ? folder
                    : Path.Combine(Application.dataPath, "..", folder));
                return Directory.Exists(fullPath) ? fullPath : Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch (Exception)
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
        }

        private void AddSelectedObjects()
        {
#if UNITY_EDITOR
            int before = sourceAssetPaths.Count + selectedSceneRoots.Count;
            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                AddObjectReference(selectedObject);
            }

            if (sourceAssetPaths.Count + selectedSceneRoots.Count == before)
            {
                SetStatus("Select a GameObject with MonoBehaviour scripts, or select a C# script/folder asset.", true);
            }
#else
            SetStatus("Adding selected objects is available only in the Unity Editor.", true);
#endif
        }

        private void AddAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

#if UNITY_EDITOR
            if (!TryNormalizeSourcePath(absolutePath, out string sourcePath))
            {
                SetStatus("The selected path could not be read.", true);
                return;
            }

            AddAssetPath(sourcePath);
#endif
        }

        private void RegisterDropZone(VisualElement dropZone)
        {
#if UNITY_EDITOR
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
                    AddObjectReference(draggedObject);
                }

                foreach (string path in DragAndDrop.paths)
                {
                    AddAbsolutePath(path);
                }

                evt.StopPropagation();
            });
#endif
        }

#if UNITY_EDITOR
        private void AddObjectReference(UnityEngine.Object objectReference)
        {
            if (objectReference == null)
            {
                return;
            }

            if (objectReference is GameObject gameObject)
            {
                AddSceneRoot(gameObject);
                return;
            }

            if (objectReference is MonoScript monoScript)
            {
                AddAssetPath(AssetDatabase.GetAssetPath(monoScript));
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(objectReference);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AddAssetPath(assetPath);
            }
        }

        private void AddSceneRoot(GameObject gameObject)
        {
            if (gameObject == null || gameObject == this.gameObject)
            {
                return;
            }

            if (!selectedSceneRoots.Contains(gameObject))
            {
                selectedSceneRoots.Add(gameObject);
                SetStatus("Added scene root " + gameObject.name, false);
            }

            Refresh();
        }

        private void OnEditorSelectionChanged()
        {
            CaptureEditorSelection();
        }

        private void CaptureEditorSelection()
        {
            GameObject[] selectedRoots = Selection.gameObjects
                .Where(gameObject => gameObject != null && gameObject != this.gameObject)
                .Distinct()
                .ToArray();

            if (selectedRoots.Length == 0)
            {
                return;
            }

            selectedSceneRoots = selectedRoots.ToList();
            Refresh();
        }
#endif

        private void RunObfuscator()
        {
#if UNITY_EDITOR
            if (!IsConfuserInstalled())
            {
                SetStatus("ConfuserEx CLI was not found at Tools/ConfuserEx/Confuser.CLI.exe.", true);
                return;
            }

            string validationError;
            if (!TryPrepareSourcesForRun(out validationError))
            {
                SetStatus(validationError, true);
                return;
            }

            try
            {
                SetStatus("Building and obfuscating selected sources...", false);
                EditorUtility.DisplayProgressBar("Scene Obfuscator Tool", "Building and obfuscating scripts...", 0.5f);

                object config = CreateEditorConfig();
                string output = InvokeBuild(config);
                SetStatus("Created " + output + ". Original sources were backed up and replaced.", false);
            }
            catch (Exception ex)
            {
                SetStatus(UnwrapException(ex).Message, true);
                Debug.LogException(UnwrapException(ex));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Refresh();
            }
#else
            SetStatus("Obfuscation runs only in the Unity Editor.", true);
#endif
        }

#if UNITY_EDITOR
        private bool TryPrepareSourcesForRun(out string error)
        {
            error = string.Empty;

            if (pathField != null && !string.IsNullOrWhiteSpace(pathField.value))
            {
                int countBefore = sourceAssetPaths.Count;
                AddAssetPath(pathField.value);
                if (sourceAssetPaths.Count == countBefore &&
                    !sourceAssetPaths.Any(path => PathsEqual(path, pathField.value)))
                {
                    error = "The path field does not identify an existing C# script or folder.";
                    return false;
                }
            }

            if (sourceAssetPaths.Count == 0 && selectedSceneRoots.Count == 0)
            {
                foreach (GameObject selectedGameObject in Selection.gameObjects)
                {
                    if (selectedGameObject != null && selectedGameObject != this.gameObject)
                    {
                        AddSceneRoot(selectedGameObject);
                    }
                }
            }

            RemoveMissingSceneRoots();

            List<string> resolvedAssetPaths = new List<string>(sourceAssetPaths);
            foreach (GameObject sceneRoot in selectedSceneRoots)
            {
                foreach (string scriptPath in GetSceneRootScriptAssetPaths(sceneRoot))
                {
                    if (!resolvedAssetPaths.Contains(scriptPath, StringComparer.OrdinalIgnoreCase))
                    {
                        resolvedAssetPaths.Add(scriptPath);
                    }
                }
            }

            sourceAssetPaths = resolvedAssetPaths
                .Select(path => TryNormalizeSourcePath(path, out string normalized) ? normalized : string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(IsValidSourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceAssetPaths.Count == 0)
            {
                error = selectedSceneRoots.Count > 0
                    ? "Selected GameObject roots do not contain valid runtime C# MonoBehaviour scripts."
                    : "Add at least one valid C# script, folder, or GameObject root.";
                Refresh();
                return false;
            }

            int expanded = CountExpandedSources();
            if (expanded <= 0)
            {
                error = "Selected path does not contain runtime C# scripts. Choose a .cs file or a folder under Assets, excluding Editor folders.";
                Refresh();
                return false;
            }

            Refresh();
            return true;
        }

        private IEnumerable<string> GetSceneRootScriptAssetPaths(GameObject gameObject)
        {
            if (gameObject == null)
            {
                yield break;
            }

            foreach (MonoBehaviour behaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                if (script == null)
                {
                    continue;
                }

                string scriptPath = AssetDatabase.GetAssetPath(script);
                if (!string.IsNullOrWhiteSpace(scriptPath))
                {
                    yield return scriptPath;
                }
            }
        }

        private void RemoveMissingSceneRoots()
        {
            selectedSceneRoots = selectedSceneRoots
                .Where(gameObject => gameObject != null && gameObject != this.gameObject)
                .Distinct()
                .ToList();
        }

        private static bool IsValidSourcePath(string sourcePath)
        {
            if (!TryNormalizeSourcePath(sourcePath, out string normalized))
            {
                return false;
            }

            string absolutePath = SourcePathToAbsolutePath(normalized);
            bool isScript = File.Exists(absolutePath) && normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            bool isFolder = Directory.Exists(absolutePath);
            return isScript || isFolder;
        }

        private static bool TryNormalizeSourcePath(string inputPath, out string sourcePath)
        {
            sourcePath = string.Empty;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return false;
            }

            string normalized = inputPath.Replace('\\', '/').Trim().Trim('"').TrimEnd('/');

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullPath = Path.IsPathRooted(normalized)
                    ? Path.GetFullPath(normalized)
                    : Path.GetFullPath(Path.Combine(projectRoot, normalized));
                string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsPathWithin(fullPath, assetsRoot))
                {
                    string relativePath = fullPath.Substring(assetsRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    sourcePath = string.IsNullOrEmpty(relativePath) ? "Assets" : "Assets/" + relativePath;
                }
                else
                {
                    sourcePath = fullPath.Replace('\\', '/');
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SourcePathToAbsolutePath(string sourcePath)
        {
            return Path.IsPathRooted(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", sourcePath.Replace('\\', '/')));
        }
#else
        private static bool IsValidSourcePath(string sourcePath)
        {
            if (!TryNormalizeSourcePath(sourcePath, out string normalized))
            {
                return false;
            }

            string absolutePath = SourcePathToAbsolutePath(normalized);
            return Directory.Exists(absolutePath) ||
                   (File.Exists(absolutePath) && normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryNormalizeSourcePath(string inputPath, out string sourcePath)
        {
            sourcePath = string.Empty;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return false;
            }

            string normalized = inputPath.Replace('\\', '/').Trim().Trim('"').TrimEnd('/');

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string fullPath = Path.IsPathRooted(normalized)
                    ? Path.GetFullPath(normalized)
                    : Path.GetFullPath(Path.Combine(projectRoot, normalized));
                string assetsRoot = Path.GetFullPath(Application.dataPath);
                if (IsPathWithin(fullPath, assetsRoot))
                {
                    string relativePath = fullPath.Substring(assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    sourcePath = string.IsNullOrEmpty(relativePath) ? "Assets" : "Assets/" + relativePath;
                }
                else
                {
                    sourcePath = fullPath.Replace('\\', '/');
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SourcePathToAbsolutePath(string sourcePath)
        {
            return Path.IsPathRooted(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", sourcePath.Replace('\\', '/')));
        }
#endif

        private static bool IsPathWithin(string path, string root)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return TryNormalizeSourcePath(left, out string normalizedLeft) &&
                   TryNormalizeSourcePath(right, out string normalizedRight) &&
                   string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyProtectionLevel(ProtectionLevel level)
        {
            protectionLevel = level;
            switch (level)
            {
                case ProtectionLevel.Basic:
                    enableRename = true;
                    enableStringEncryption = false;
                    enableControlFlow = false;
                    break;
                case ProtectionLevel.Balanced:
                    enableRename = true;
                    enableStringEncryption = true;
                    enableControlFlow = false;
                    break;
                case ProtectionLevel.Strong:
                    enableRename = true;
                    enableStringEncryption = true;
                    enableControlFlow = true;
                    break;
            }

            QueueBuildUi();
        }

        private void RefreshProtectionLevelVisuals()
        {
            SetProtectionButtonState(basicProtectionButton, protectionLevel == ProtectionLevel.Basic);
            SetProtectionButtonState(balancedProtectionButton, protectionLevel == ProtectionLevel.Balanced);
            SetProtectionButtonState(strongProtectionButton, protectionLevel == ProtectionLevel.Strong);

            if (protectionLevelSummary != null)
            {
                protectionLevelSummary.text = "Selected: " + protectionLevel + " (" + GetEnabledProtectionText() + ")";
            }
        }

        private string GetEnabledProtectionText()
        {
            var protections = new List<string>();
            if (enableRename) protections.Add("rename");
            if (enableStringEncryption) protections.Add("constants");
            if (enableControlFlow) protections.Add("control flow");
            return protections.Count == 0 ? "no protections" : string.Join(", ", protections);
        }

        private static void SetProtectionButtonState(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.style.backgroundColor = selected ? ColorFromHex("#2563eb") : ColorFromHex("#f8fafc");
            button.style.color = selected ? Color.white : ColorFromHex("#26364d");
            Color border = selected ? ColorFromHex("#1d4ed8") : ColorFromHex("#d8e0eb");
            button.style.borderTopColor = border;
            button.style.borderRightColor = border;
            button.style.borderBottomColor = border;
            button.style.borderLeftColor = border;
        }

        private void SetStatus(string message, bool isError)
        {
            statusLabel.text = message;
            if (statusScroll != null)
            {
                statusScroll.style.display = DisplayStyle.Flex;
            }
            statusLabel.style.backgroundColor = isError ? ColorFromHex("#fee2e2") : ColorFromHex("#e7f7ed");
            statusLabel.style.color = isError ? ColorFromHex("#991b1b") : ColorFromHex("#146c3e");
        }

        private int CountExpandedSources()
        {
#if UNITY_EDITOR
            try
            {
                Type processorType = Type.GetType("ScriptObfuscator.ObfuscatorProcessor, ScriptObfuscator.Editor", false);
                MethodInfo method = processorType?.GetMethod("CountExpandedSourceFilesForDisplay", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    object result = method.Invoke(null, new object[] { sourceAssetPaths });
                    if (result is int i)
                    {
                        return i;
                    }
                }
            }
            catch (Exception)
            {
                // Swallow reflection/assembly load exceptions so editor UI continues to work
            }
#endif
            return sourceAssetPaths.Count;
        }

        private bool IsConfuserInstalled()
        {
#if UNITY_EDITOR
            try
            {
                Type type = Type.GetType("ScriptObfuscator.ConfuserExManager, ScriptObfuscator.Editor", false);
                PropertyInfo property = type?.GetProperty("IsInstalled", BindingFlags.Public | BindingFlags.Static);
                return property != null && (bool)property.GetValue(null);
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }

        private string GetExpectedCliPath()
        {
#if UNITY_EDITOR
            try
            {
                Type type = Type.GetType("ScriptObfuscator.ConfuserExManager, ScriptObfuscator.Editor", false);
                PropertyInfo property = type?.GetProperty("ExpectedCliPath", BindingFlags.Public | BindingFlags.Static);
                return property?.GetValue(null)?.ToString() ?? "Tools/ConfuserEx/Confuser.CLI.exe";
            }
            catch (Exception)
            {
                return "Tools/ConfuserEx/Confuser.CLI.exe";
            }
#else
            return "Tools/ConfuserEx/Confuser.CLI.exe";
#endif
        }

#if UNITY_EDITOR
        private static void RepairEditorSceneInput()
        {
            Type eventSystemType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            Type standaloneInputType = Type.GetType("UnityEngine.EventSystems.StandaloneInputModule, UnityEngine.UI");
            Type inputSystemUiType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            GameObject eventSystemObject = FindSceneComponentObject(eventSystemType);
            if (eventSystemObject == null && eventSystemType != null)
            {
                eventSystemObject = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");
                eventSystemObject.AddComponent(eventSystemType);
            }

            RemoveDuplicateEventSystems(eventSystemObject, eventSystemType);
            RemoveComponentsOfType(standaloneInputType);

            if (eventSystemObject != null && inputSystemUiType != null && eventSystemObject.GetComponent(inputSystemUiType) == null)
            {
                Undo.AddComponent(eventSystemObject, inputSystemUiType);
            }
        }

        private static GameObject FindSceneComponentObject(Type componentType)
        {
            if (componentType == null)
            {
                return null;
            }

            Component component = Resources.FindObjectsOfTypeAll(componentType)
                .OfType<Component>()
                .FirstOrDefault(IsSceneObject);
            return component != null ? component.gameObject : null;
        }

        private static void RemoveDuplicateEventSystems(GameObject keep, Type eventSystemType)
        {
            if (eventSystemType == null)
            {
                return;
            }

            foreach (Component component in Resources.FindObjectsOfTypeAll(eventSystemType).OfType<Component>().Where(IsSceneObject))
            {
                if (keep != null && component.gameObject == keep)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(component.gameObject);
            }
        }

        private static void RemoveComponentsOfType(Type componentType)
        {
            if (componentType == null)
            {
                return;
            }

            foreach (Component component in Resources.FindObjectsOfTypeAll(componentType).OfType<Component>().Where(IsSceneObject))
            {
                Undo.DestroyObjectImmediate(component);
            }
        }

        private static bool IsSceneObject(Component component)
        {
            return component != null &&
                   component.gameObject != null &&
                   component.gameObject.scene.IsValid() &&
                   component.gameObject.scene.isLoaded;
        }

        private object CreateEditorConfig()
        {
            try
            {
                Type configType = Type.GetType("ScriptObfuscator.ObfuscatorConfig, ScriptObfuscator.Editor", false);
                if (configType == null)
                {
                    throw new InvalidOperationException("ObfuscatorConfig type not found.");
                }

                object config = Activator.CreateInstance(configType, true);

                SetField(config, "sourceAssetPaths", new List<string>(sourceAssetPaths));
                SetField(config, "dllName", dllName);
                SetField(config, "outputFolder", outputFolder);
                SetField(config, "targetFramework", targetFramework);
                SetField(config, "enableRename", enableRename);
                SetField(config, "enableControlFlow", enableControlFlow);
                SetField(config, "enableStringEncryption", enableStringEncryption);
                SetField(config, "includeEditorReferences", false);
                SetField(config, "allowUnsafeCode", allowUnsafeCode);
                SetField(config, "overwriteOutput", overwriteOutput);
                SetField(config, "removeSourcesAfterSuccess", false);
                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to create obfuscator config.", ex);
            }
        }

        private string InvokeBuild(object config)
        {
            try
            {
                Type processorType = Type.GetType("ScriptObfuscator.ObfuscatorProcessor, ScriptObfuscator.Editor", false);
                if (processorType == null)
                {
                    throw new InvalidOperationException("ObfuscatorProcessor type not found.");
                }

                MethodInfo method = processorType.GetMethod("BuildAndReplace", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    throw new InvalidOperationException("BuildAndReplace method not found on ObfuscatorProcessor.");
                }

                return (string)method.Invoke(null, new object[] { config, new Func<string>(SelectBackupFolderAfterBuild) });
            }
            catch (TargetInvocationException tie)
            {
                throw UnwrapException(tie);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to invoke obfuscator build.", ex);
            }
        }

        private static string SelectBackupFolderAfterBuild()
        {
#if UNITY_EDITOR
            EditorUtility.ClearProgressBar();
            return EditorUtility.OpenFolderPanel(
                "Obfuscation Complete - Choose Original Source Backup Folder",
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                string.Empty);
#else
            return string.Empty;
#endif
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        private static void SetPropertyIfAvailable(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            try
            {
                property.SetValue(target, value);
            }
            catch (Exception)
            {
                // Unity changes some UI Toolkit document properties between versions.
            }
        }

        private static void SetEnumPropertyIfAvailable(object target, string propertyName, params string[] valueNames)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
            {
                return;
            }

            foreach (string valueName in valueNames)
            {
                try
                {
                    if (Enum.IsDefined(property.PropertyType, valueName))
                    {
                        property.SetValue(target, Enum.Parse(property.PropertyType, valueName));
                        return;
                    }
                }
                catch (Exception)
                {
                    // Keep trying compatible enum names across Unity versions.
                }
            }
        }

        private static void InvokeStatic(string typeName, string methodName)
        {
            try
            {
                Type type = Type.GetType(typeName, false);
                if (type == null)
                {
                    return;
                }

                MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                method?.Invoke(null, null);
            }
            catch (Exception)
            {
                // Ignore failures to invoke optional editor hooks
            }
        }

        private static Exception UnwrapException(Exception exception)
        {
            return exception is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null
                ? targetInvocationException.InnerException
                : exception;
        }
#endif

        private static VisualElement Card(string title)
        {
            VisualElement card = Column();
            card.style.flexShrink = 0;
            card.style.backgroundColor = Color.white;
            card.style.paddingLeft = 18;
            card.style.paddingRight = 18;
            card.style.paddingTop = 16;
            card.style.paddingBottom = 18;
            card.style.marginBottom = 16;
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.borderTopWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderTopColor = ColorFromHex("#dde4ef");
            card.style.borderRightColor = ColorFromHex("#dde4ef");
            card.style.borderBottomColor = ColorFromHex("#dde4ef");
            card.style.borderLeftColor = ColorFromHex("#dde4ef");

            Label heading = StrongLabel(title);
            heading.style.fontSize = 15;
            heading.style.marginBottom = 12;
            card.Add(heading);
            return card;
        }

        private void ApplyTextDefaults(VisualElement element)
        {
            Font font = GetRuntimeFont();
            if (font != null)
            {
#pragma warning disable CS0618
                element.style.unityFont = font;
#pragma warning restore CS0618
            }

            foreach (VisualElement child in element.Children())
            {
                ApplyTextDefaults(child);
            }
        }

        private Font GetRuntimeFont()
        {
            if (uiFont != null)
            {
                return uiFont;
            }

            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

#if UNITY_EDITOR
            if (uiFont == null)
            {
                uiFont = EditorGUIUtility.Load("Fonts/Inter-Regular.ttf") as Font;
            }
#endif

            return uiFont;
        }

        private static VisualElement BoundTextField(string label, string value, Action<string> changed)
        {
            TextField field = new TextField { value = value };
            StyleTextField(field);
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return LabeledRow(label, field);
        }

        private static VisualElement LabeledRow(string label, VisualElement control)
        {
            VisualElement row = Row();
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 10;
            row.style.minHeight = 34;
            row.style.flexShrink = 0;

            Label labelElement = FieldLabel(label);
            labelElement.style.width = 150;
            labelElement.style.flexShrink = 0;
            labelElement.style.marginBottom = 0;
            row.Add(labelElement);

            control.style.flexGrow = 1;
            row.Add(control);
            return row;
        }

        private static Label FieldLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = ColorFromHex("#536179");
            label.style.marginBottom = 4;
            return label;
        }

        private static void StyleTextField(TextField field)
        {
            field.style.height = 32;
            field.style.backgroundColor = ColorFromHex("#f8fafc");
            field.style.borderTopWidth = 1;
            field.style.borderRightWidth = 1;
            field.style.borderBottomWidth = 1;
            field.style.borderLeftWidth = 1;
            field.style.borderTopColor = ColorFromHex("#d8e0eb");
            field.style.borderRightColor = ColorFromHex("#d8e0eb");
            field.style.borderBottomColor = ColorFromHex("#d8e0eb");
            field.style.borderLeftColor = ColorFromHex("#d8e0eb");
            field.style.borderTopLeftRadius = 2;
            field.style.borderTopRightRadius = 2;
            field.style.borderBottomLeftRadius = 2;
            field.style.borderBottomRightRadius = 2;
            field.style.paddingLeft = 10;
            field.style.paddingRight = 10;
            field.style.paddingTop = 0;
            field.style.paddingBottom = 0;
            field.style.color = ColorFromHex("#202b3d");
            field.style.fontSize = 12;
        }

        private static VisualElement BoundToggle(string label, bool value, Action<bool> changed)
        {
            bool currentValue = value;

            VisualElement row = Row();
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 10;
            row.style.minHeight = 28;

            Label labelElement = FieldLabel(label);
            labelElement.style.width = 0;
            labelElement.style.flexGrow = 1;
            labelElement.style.marginBottom = 0;
            row.Add(labelElement);

            VisualElement box = new VisualElement();
            box.style.width = 18;
            box.style.height = 18;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.backgroundColor = ColorFromHex("#f8fafc");
            box.style.borderTopWidth = 1;
            box.style.borderRightWidth = 1;
            box.style.borderBottomWidth = 1;
            box.style.borderLeftWidth = 1;
            box.style.borderTopColor = ColorFromHex("#7a7f87");
            box.style.borderRightColor = ColorFromHex("#7a7f87");
            box.style.borderBottomColor = ColorFromHex("#7a7f87");
            box.style.borderLeftColor = ColorFromHex("#7a7f87");
            box.style.marginLeft = 8;

            Label check = new Label();
            check.style.fontSize = 16;
            check.style.unityFontStyleAndWeight = FontStyle.Bold;
            check.style.color = ColorFromHex("#3d4654");
            check.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.Add(check);
            row.Add(box);

            Action refresh = () =>
            {
                check.text = currentValue ? "✓" : string.Empty;
                box.style.backgroundColor = currentValue ? ColorFromHex("#f8fafc") : Color.white;
            };

            row.RegisterCallback<ClickEvent>(_ =>
            {
                currentValue = !currentValue;
                changed(currentValue);
                refresh();
            });

            refresh();
            return row;
        }

        private static VisualElement Row()
        {
            return new VisualElement { style = { flexDirection = FlexDirection.Row } };
        }

        private static VisualElement Column()
        {
            return new VisualElement { style = { flexDirection = FlexDirection.Column } };
        }

        private static Label StrongLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 14;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = ColorFromHex("#1f2a3d");
            return label;
        }

        private static Label InfoLabel()
        {
            Label label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = ColorFromHex("#315174");
            label.style.backgroundColor = ColorFromHex("#eaf3ff");
            label.style.paddingLeft = 10;
            label.style.paddingRight = 10;
            label.style.paddingTop = 8;
            label.style.paddingBottom = 8;
            label.style.borderTopLeftRadius = 6;
            label.style.borderTopRightRadius = 6;
            label.style.borderBottomLeftRadius = 6;
            label.style.borderBottomRightRadius = 6;
            return label;
        }

        private static Button PrimaryButton(string text)
        {
            Button button = new Button { text = text };
            button.style.backgroundColor = ColorFromHex("#2563eb");
            button.style.color = Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 15;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            return button;
        }

        private static Button SecondaryButton(string text)
        {
            Button button = new Button { text = text };
            button.style.backgroundColor = ColorFromHex("#eef2f7");
            button.style.color = ColorFromHex("#26364d");
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 13;
            button.style.height = 32;
            button.style.flexGrow = 1;
            button.style.marginRight = 8;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopColor = ColorFromHex("#d8e0eb");
            button.style.borderRightColor = ColorFromHex("#d8e0eb");
            button.style.borderBottomColor = ColorFromHex("#d8e0eb");
            button.style.borderLeftColor = ColorFromHex("#d8e0eb");
            return button;
        }

        private static Button LevelButton(string text)
        {
            Button button = SecondaryButton(text);
            button.style.height = 32;
            button.style.minWidth = 0;
            button.style.flexBasis = Length.Percent(33);
            return button;
        }

        private static Color ColorFromHex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }
    }
}
