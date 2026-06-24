using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptObfuscator
{
    internal static class SceneObfuscatorToolMenu
    {
        [MenuItem("Tools/Script Obfuscator/Create Scene UI")]
        [MenuItem("GameObject/Script Obfuscator/Scene Tool", false, 10)]
        public static void CreateSceneTool()
        {
            RepairSceneInput();

            Type componentType = Type.GetType("ScriptObfuscator.SceneUI.SceneObfuscatorTool, Assembly-CSharp");
            if (componentType == null)
            {
                EditorUtility.DisplayDialog(
                    "Scene Tool Not Compiled",
                    "Unity has not compiled SceneObfuscatorTool yet. Wait for script compilation to finish, then try again.",
                    "OK");
                return;
            }

            GameObject toolObject = new GameObject("Script Obfuscator Scene Tool");
            Undo.RegisterCreatedObjectUndo(toolObject, "Create Script Obfuscator Scene Tool");

            UIDocument document = toolObject.AddComponent<UIDocument>();
            document.panelSettings = GetOrCreatePanelSettings();

            toolObject.AddComponent(componentType);
            Selection.activeGameObject = toolObject;
            EditorGUIUtility.PingObject(toolObject);
        }

        private static void RepairSceneInput()
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

        private static PanelSettings GetOrCreatePanelSettings()
        {
            const string path = "Assets/ScriptObfuscator/SceneObfuscatorPanelSettings.asset";
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panelSettings != null)
            {
                return panelSettings;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "SceneObfuscatorPanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 720);
            AssetDatabase.CreateAsset(panelSettings, path);
            AssetDatabase.SaveAssets();
            return panelSettings;
        }
    }
}
