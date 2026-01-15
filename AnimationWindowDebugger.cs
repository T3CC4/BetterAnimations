using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace BetterAnimations
{
#if UNITY_EDITOR
    public class AnimationWindowDebugger : EditorWindow
    {
        private Vector2 scrollPosition;
        private string searchFilter = "";
        private bool showMethods = true;
        private bool showFields = true;
        private bool showProperties = true;
        private bool showEvents = true;
        private bool showPrivate = true;
        private bool showPublic = true;
        private bool showStatic = true;
        private bool showInstance = true;

        private enum InspectType
        {
            AnimEditor,
            AnimationWindowState,
            DopeSheet,
            AnimationWindow,
            All
        }

        private InspectType currentType = InspectType.All;

        [MenuItem("Window/Better Animations/Debug Inspector")]
        public static void ShowWindow()
        {
            GetWindow<AnimationWindowDebugger>("Animation Window Debugger");
        }

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Animation Window Inspector", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Filters
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

            searchFilter = EditorGUILayout.TextField("Search", searchFilter);

            EditorGUILayout.BeginHorizontal();
            currentType = (InspectType)EditorGUILayout.EnumPopup("Inspect Type", currentType);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showMethods = EditorGUILayout.ToggleLeft("Methods", showMethods, GUILayout.Width(80));
            showFields = EditorGUILayout.ToggleLeft("Fields", showFields, GUILayout.Width(80));
            showProperties = EditorGUILayout.ToggleLeft("Properties", showProperties, GUILayout.Width(80));
            showEvents = EditorGUILayout.ToggleLeft("Events", showEvents, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showPublic = EditorGUILayout.ToggleLeft("Public", showPublic, GUILayout.Width(80));
            showPrivate = EditorGUILayout.ToggleLeft("Private", showPrivate, GUILayout.Width(80));
            showStatic = EditorGUILayout.ToggleLeft("Static", showStatic, GUILayout.Width(80));
            showInstance = EditorGUILayout.ToggleLeft("Instance", showInstance, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh", GUILayout.Height(25)))
            {
                Repaint();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Content
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            try
            {
                Assembly editorAssembly = typeof(Editor).Assembly;

                if (currentType == InspectType.AnimEditor || currentType == InspectType.All)
                {
                    Type animEditorType = editorAssembly.GetType("UnityEditor.AnimEditor");
                    if (animEditorType != null)
                    {
                        InspectType(animEditorType, "AnimEditor");
                    }
                }

                if (currentType == InspectType.AnimationWindowState || currentType == InspectType.All)
                {
                    Type animWindowStateType = editorAssembly.GetType("UnityEditorInternal.AnimationWindowState");
                    if (animWindowStateType != null)
                    {
                        InspectType(animWindowStateType, "AnimationWindowState");
                    }
                }

                if (currentType == InspectType.DopeSheet || currentType == InspectType.All)
                {
                    Type dopeSheetType = editorAssembly.GetType("UnityEditor.DopeSheetEditor");
                    if (dopeSheetType != null)
                    {
                        InspectType(dopeSheetType, "DopeSheetEditor");
                    }
                }

                if (currentType == InspectType.AnimationWindow || currentType == InspectType.All)
                {
                    Type animWindowType = editorAssembly.GetType("UnityEditor.AnimationWindow");
                    if (animWindowType != null)
                    {
                        InspectType(animWindowType, "AnimationWindow");
                    }
                }
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Error: {e.Message}", MessageType.Error);
            }

            EditorGUILayout.EndScrollView();
        }

        void InspectType(Type type, string typeName)
        {
            EditorGUILayout.BeginVertical("box");

            // Header
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);
            EditorGUILayout.LabelField($"═══ {typeName} ═══", headerStyle);

            EditorGUILayout.LabelField($"Full Name: {type.FullName}", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            // Get binding flags
            BindingFlags flags = BindingFlags.Default;
            if (showPublic) flags |= BindingFlags.Public;
            if (showPrivate) flags |= BindingFlags.NonPublic;
            if (showStatic) flags |= BindingFlags.Static;
            if (showInstance) flags |= BindingFlags.Instance;

            // Methods
            if (showMethods)
            {
                EditorGUILayout.LabelField("▼ Methods", EditorStyles.boldLabel);
                var methods = type.GetMethods(flags)
                    .Where(m => !m.IsSpecialName) // Exclude property getters/setters
                    .OrderBy(m => m.Name);

                foreach (var method in methods)
                {
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !method.Name.ToLower().Contains(searchFilter.ToLower()))
                        continue;

                    DrawMethod(method);
                }
                EditorGUILayout.Space();
            }

            // Fields
            if (showFields)
            {
                EditorGUILayout.LabelField("▼ Fields", EditorStyles.boldLabel);
                var fields = type.GetFields(flags).OrderBy(f => f.Name);

                foreach (var field in fields)
                {
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !field.Name.ToLower().Contains(searchFilter.ToLower()))
                        continue;

                    DrawField(field);
                }
                EditorGUILayout.Space();
            }

            // Properties
            if (showProperties)
            {
                EditorGUILayout.LabelField("▼ Properties", EditorStyles.boldLabel);
                var properties = type.GetProperties(flags).OrderBy(p => p.Name);

                foreach (var property in properties)
                {
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !property.Name.ToLower().Contains(searchFilter.ToLower()))
                        continue;

                    DrawProperty(property);
                }
                EditorGUILayout.Space();
            }

            // Events
            if (showEvents)
            {
                EditorGUILayout.LabelField("▼ Events", EditorStyles.boldLabel);
                var events = type.GetEvents(flags).OrderBy(e => e.Name);

                int eventCount = 0;
                foreach (var evt in events)
                {
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !evt.Name.ToLower().Contains(searchFilter.ToLower()))
                        continue;

                    DrawEvent(evt);
                    eventCount++;
                }

                if (eventCount == 0)
                {
                    EditorGUILayout.LabelField("  (No events found)", EditorStyles.miniLabel);
                }
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        void DrawMethod(MethodInfo method)
        {
            string accessModifier = GetAccessModifier(method);
            string staticModifier = method.IsStatic ? "static " : "";

            // Parameters
            var parameters = method.GetParameters();
            string paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));

            // Color code by access level
            Color color = method.IsPublic ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.8f, 0.5f);

            GUIStyle methodStyle = new GUIStyle(EditorStyles.label);
            methodStyle.normal.textColor = color;
            methodStyle.fontSize = 11;
            methodStyle.wordWrap = true;

            string signature = $"  {accessModifier} {staticModifier}{method.ReturnType.Name} {method.Name}({paramStr})";
            EditorGUILayout.LabelField(signature, methodStyle);
        }

        void DrawField(FieldInfo field)
        {
            string accessModifier = GetAccessModifier(field);
            string staticModifier = field.IsStatic ? "static " : "";
            string readonlyModifier = field.IsInitOnly ? "readonly " : "";

            Color color = field.IsPublic ? new Color(0.7f, 0.9f, 1f) : new Color(1f, 0.9f, 0.7f);

            GUIStyle fieldStyle = new GUIStyle(EditorStyles.label);
            fieldStyle.normal.textColor = color;
            fieldStyle.fontSize = 11;

            string signature = $"  {accessModifier} {staticModifier}{readonlyModifier}{field.FieldType.Name} {field.Name}";
            EditorGUILayout.LabelField(signature, fieldStyle);
        }

        void DrawProperty(PropertyInfo property)
        {
            string accessModifier = "public";
            if (property.GetMethod != null)
                accessModifier = GetAccessModifier(property.GetMethod);
            else if (property.SetMethod != null)
                accessModifier = GetAccessModifier(property.SetMethod);

            string getSet = "";
            if (property.CanRead && property.CanWrite) getSet = "{ get; set; }";
            else if (property.CanRead) getSet = "{ get; }";
            else if (property.CanWrite) getSet = "{ set; }";

            bool isStatic = false;
            if (property.GetMethod != null) isStatic = property.GetMethod.IsStatic;
            else if (property.SetMethod != null) isStatic = property.SetMethod.IsStatic;
            string staticModifier = isStatic ? "static " : "";

            Color color = new Color(0.9f, 0.7f, 1f);

            GUIStyle propStyle = new GUIStyle(EditorStyles.label);
            propStyle.normal.textColor = color;
            propStyle.fontSize = 11;

            string signature = $"  {accessModifier} {staticModifier}{property.PropertyType.Name} {property.Name} {getSet}";
            EditorGUILayout.LabelField(signature, propStyle);
        }

        void DrawEvent(EventInfo evt)
        {
            Color color = new Color(1f, 0.5f, 0.5f);

            GUIStyle eventStyle = new GUIStyle(EditorStyles.label);
            eventStyle.normal.textColor = color;
            eventStyle.fontSize = 11;

            string signature = $"  event {evt.EventHandlerType?.Name ?? "?"} {evt.Name}";
            EditorGUILayout.LabelField(signature, eventStyle);
        }

        string GetAccessModifier(MethodBase method)
        {
            if (method.IsPublic) return "public";
            if (method.IsPrivate) return "private";
            if (method.IsFamily) return "protected";
            if (method.IsAssembly) return "internal";
            if (method.IsFamilyOrAssembly) return "protected internal";
            return "unknown";
        }

        string GetAccessModifier(FieldInfo field)
        {
            if (field.IsPublic) return "public";
            if (field.IsPrivate) return "private";
            if (field.IsFamily) return "protected";
            if (field.IsAssembly) return "internal";
            if (field.IsFamilyOrAssembly) return "protected internal";
            return "unknown";
        }
    }
#endif
}
