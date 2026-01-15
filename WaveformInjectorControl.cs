using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

namespace BetterAnimations
{
#if UNITY_EDITOR
    public class WaveformInjectorControl : EditorWindow
    {
        private AudioClip audioClip;
        private Texture2D waveformTexture;
        private bool generatedWaveform = false;

        private Color waveformColor = new Color(1f, 0.3f, 0.3f, 0.6f);
        private Color bgColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        private bool fadeForm = true;

        private int audioIndex = 0;
        private AudioClip[] allAudioClips;

        private const int WAVEFORM_WIDTH = 16000;
        private const int WAVEFORM_HEIGHT = 80;

        [MenuItem("Window/Animation/Waveform Injector Control")]
        static void ShowWindow()
        {
            WaveformInjectorControl window = GetWindow<WaveformInjectorControl>("Waveform Control");
            window.minSize = new Vector2(300, 250);
            window.Show();
        }

        void OnEnable()
        {
            RefreshAudioClips();
        }

        void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label("Waveform Injection Control", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // Audio Selection
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            audioIndex = EditorGUILayout.Popup("Audio Clip:", audioIndex, GetAudioClipNames());
            if (EditorGUI.EndChangeCheck() && audioIndex < allAudioClips.Length)
            {
                LoadAudioClip(allAudioClips[audioIndex]);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshAudioClips();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Settings
            EditorGUI.BeginChangeCheck();
            waveformColor = EditorGUILayout.ColorField("Waveform Color", waveformColor);
            bgColor = EditorGUILayout.ColorField("Background Color", bgColor);
            fadeForm = EditorGUILayout.Toggle("Fade Waveform", fadeForm);

            if (EditorGUI.EndChangeCheck())
            {
                if (fadeForm && audioClip != null)
                {
                    CreateWaveform();
                }
                UpdatePatcherSettings();
            }

            GUILayout.Space(10);

            // Status
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Audio Clip:", audioClip != null ? audioClip.name : "None");
            EditorGUILayout.LabelField("Waveform:", generatedWaveform ? "✓ Generated" : "✗ Not generated");
            EditorGUILayout.LabelField("Injection:", AnimationWindowPatcher.isEnabled ? "✓ Active" : "✗ Inactive");
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Control Buttons
            GUI.enabled = audioClip != null && generatedWaveform;

            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = AnimationWindowPatcher.isEnabled ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 1f, 0.5f);

            if (GUILayout.Button(AnimationWindowPatcher.isEnabled ? "DISABLE Waveform Injection" : "ENABLE Waveform Injection", GUILayout.Height(40)))
            {
                AnimationWindowPatcher.isEnabled = !AnimationWindowPatcher.isEnabled;

                if (AnimationWindowPatcher.isEnabled)
                {
                    UpdatePatcherSettings();
                }
            }

            GUI.backgroundColor = oldColor;
            GUI.enabled = true;

            GUILayout.Space(10);

            // Info
            EditorGUILayout.HelpBox(
                "This uses Harmony to inject the waveform directly into Unity's Animation Window. " +
                "Enable injection to see the waveform overlaid on the timeline.",
                MessageType.Info
            );

            EditorGUILayout.EndVertical();

            // Drag & Drop
            HandleDragAndDrop();
        }

        void UpdatePatcherSettings()
        {
            AnimationWindowPatcher.audioClip = audioClip;
            AnimationWindowPatcher.waveformTexture = waveformTexture;
            AnimationWindowPatcher.waveformColor = waveformColor;
            AnimationWindowPatcher.bgColor = bgColor;
        }

        void HandleDragAndDrop()
        {
            Event evt = Event.current;
            Rect dropArea = new Rect(0, 0, position.width, position.height);

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                        {
                            if (obj is AudioClip clip)
                            {
                                LoadAudioClip(clip);

                                for (int i = 0; i < allAudioClips.Length; i++)
                                {
                                    if (allAudioClips[i] == clip)
                                    {
                                        audioIndex = i;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
        }

        void LoadAudioClip(AudioClip clip)
        {
            audioClip = clip;
            generatedWaveform = false;

            if (audioClip != null)
            {
                CreateWaveform();
                generatedWaveform = true;
                UpdatePatcherSettings();
            }
        }

        void CreateWaveform()
        {
            if (audioClip == null) return;

            int gradientHeight = 256;
            Texture2D gradientTexture = null;

            if (fadeForm)
            {
                gradientTexture = new Texture2D(1, gradientHeight, TextureFormat.RGBA32, false);
                for (int y = 0; y < gradientHeight; y++)
                {
                    float t = (float)y / (gradientHeight - 1) * 0.5f;
                    Color pixelColor = Color.Lerp(Color.white, Color.clear, t);
                    gradientTexture.SetPixel(0, y, pixelColor);
                }
                gradientTexture.Apply();
            }

            waveformTexture = new Texture2D(WAVEFORM_WIDTH, WAVEFORM_HEIGHT, TextureFormat.RGBA32, false);

            Color[] blankPixels = new Color[WAVEFORM_WIDTH * WAVEFORM_HEIGHT];
            for (int x = 0; x < blankPixels.Length; x++)
            {
                blankPixels[x] = Color.clear;
            }
            waveformTexture.SetPixels(blankPixels);

            int step = Mathf.CeilToInt((audioClip.samples * audioClip.channels) / WAVEFORM_WIDTH);
            float[] samples = new float[audioClip.samples * audioClip.channels];
            audioClip.GetData(samples, 0);

            for (int x = 0; x < WAVEFORM_WIDTH; x++)
            {
                int barHeight = Mathf.CeilToInt(Mathf.Clamp(Mathf.Abs(samples[x * step]) * WAVEFORM_HEIGHT / 2, 0, WAVEFORM_HEIGHT / 2));
                int halfHeight = WAVEFORM_HEIGHT / 2;

                for (int y = 0; y < barHeight; y++)
                {
                    Color pixelColor = Color.white;
                    if (fadeForm)
                    {
                        float distanceFromCenter = (float)y / barHeight;
                        pixelColor = gradientTexture.GetPixel(0, Mathf.FloorToInt(distanceFromCenter * (gradientHeight - 1)));
                    }

                    waveformTexture.SetPixel(x, halfHeight + y, pixelColor);
                    waveformTexture.SetPixel(x, halfHeight - y, pixelColor);
                }
            }

            waveformTexture.filterMode = FilterMode.Point;
            waveformTexture.Apply();
        }

        void RefreshAudioClips()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip");
            string[] paths = guids.Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToArray();

            allAudioClips = new AudioClip[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                allAudioClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            }
        }

        string[] GetAudioClipNames()
        {
            string[] names = new string[allAudioClips.Length];
            for (int i = 0; i < allAudioClips.Length; i++)
            {
                names[i] = allAudioClips[i] != null ? allAudioClips[i].name : "None";
            }
            return names;
        }
    }
#endif
}