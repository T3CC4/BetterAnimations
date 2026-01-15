using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;

namespace BetterAnimations
{
#if UNITY_EDITOR
    [InitializeOnLoad]
    public static class AnimationWindowPatcher
    {
        private static Harmony harmony;

        // Settings
        public static AudioClip audioClip;
        public static Texture2D waveformTexture;
        public static Color waveformColor = new Color(1f, 0.3f, 0.3f, 0.6f);
        public static Color bgColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        public static bool isEnabled = true;
        public static bool isExpanded = true;
        public static float waveformHeight = 100f;
        public static float volume = 1.0f;
        public static bool fadeForm = true;

        private static bool isPatched = false;
        private static bool isPlaying = false;
        private static float lastTime = 0;
        private static float lastLastTime = 0;
        private static bool hasStopped = false;

        // Audio clip selection
        private static AudioClip[] allAudioClips;
        private static string[] audioClipNames;
        private static int selectedAudioIndex = 0;
        private static bool showSettings = false;

        // Cache reflection lookups for performance
        private static Type _animationWindowStateType;
        private static PropertyInfo _playingProperty;
        private static PropertyInfo _currentTimeProperty;
        private static object _cachedStateInstance;
        private static double _lastStateCacheTime;
        private const double STATE_CACHE_LIFETIME = 0.5;

        // Threshold to prevent micro-movements from triggering audio
        private const float TIME_CHANGE_THRESHOLD = 0.001f;

        // Waveform generation constants
        private const int WAVEFORM_WIDTH = 16000;
        private const int WAVEFORM_HEIGHT = 80;

        static AnimationWindowPatcher()
        {
            EditorApplication.delayCall += DelayedPatch;
            EditorApplication.update += UpdateAudioPlayback;
            RefreshAudioClips();
        }

        static void DelayedPatch()
        {
            if (isPatched) return;

            try
            {
                var test = EditorStyles.label;
                if (test == null)
                {
                    EditorApplication.delayCall += DelayedPatch;
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BetterAnimations] EditorStyles not ready, retrying: {e.Message}");
                EditorApplication.delayCall += DelayedPatch;
                return;
            }

            harmony = new Harmony("com.betteranimations.animwindowpatch");

            try
            {
                Assembly editorAssembly = typeof(Editor).Assembly;
                Type animEditorType = editorAssembly.GetType("UnityEditor.AnimEditor");

                if (animEditorType == null)
                {
                    Debug.LogWarning("[BetterAnimations] AnimEditor type not found!");
                    return;
                }

                MethodInfo originalMethod = animEditorType.GetMethod(
                    "OnAnimEditorGUI",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(EditorWindow), typeof(Rect) },
                    null
                );

                if (originalMethod == null)
                {
                    Debug.LogWarning("[BetterAnimations] OnAnimEditorGUI method not found!");
                    return;
                }

                MethodInfo postfixMethod = typeof(AnimationWindowPatcher).GetMethod(
                    nameof(OnAnimEditorGUI_Postfix),
                    BindingFlags.Static | BindingFlags.Public
                );

                harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));

                isPatched = true;
                Debug.Log("[BetterAnimations] ✓ Animation Window successfully patched!");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BetterAnimations] Failed to patch: {e.Message}");
            }
        }

        static void RefreshAudioClips()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip");
            string[] paths = guids.Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToArray();

            allAudioClips = paths
                .Select(path => AssetDatabase.LoadAssetAtPath<AudioClip>(path))
                .Where(clip => clip != null)
                .ToArray();

            audioClipNames = allAudioClips.Select(clip => clip.name).ToArray();

            if (audioClip != null && allAudioClips.Length > 0)
            {
                selectedAudioIndex = Array.IndexOf(allAudioClips, audioClip);
                if (selectedAudioIndex < 0) selectedAudioIndex = 0;
            }
        }

        static void UpdateAudioPlayback()
        {
            if (!isEnabled || audioClip == null || EditorApplication.isPlaying)
                return;

            try
            {
                // Initialize reflection cache if needed
                if (_animationWindowStateType == null)
                {
                    Assembly editorAssembly = typeof(Editor).Assembly;
                    _animationWindowStateType = editorAssembly.GetType("UnityEditorInternal.AnimationWindowState");

                    if (_animationWindowStateType == null)
                    {
                        Debug.LogError("[BetterAnimations] AnimationWindowState type not found");
                        return;
                    }

                    _playingProperty = _animationWindowStateType.GetProperty("playing");
                    _currentTimeProperty = _animationWindowStateType.GetProperty("currentTime");
                }

                // Cache state instance (refresh periodically to handle window changes)
                double currentTime = EditorApplication.timeSinceStartup;
                if (_cachedStateInstance == null || (currentTime - _lastStateCacheTime) > STATE_CACHE_LIFETIME)
                {
                    UnityEngine.Object[] stateInstances = Resources.FindObjectsOfTypeAll(_animationWindowStateType);
                    if (stateInstances.Length == 0)
                    {
                        _cachedStateInstance = null;
                        return;
                    }
                    _cachedStateInstance = stateInstances[0];
                    _lastStateCacheTime = currentTime;
                }

                if (_cachedStateInstance == null || _playingProperty == null || _currentTimeProperty == null)
                    return;

                bool isAnimPlaying = (bool)_playingProperty.GetValue(_cachedStateInstance);
                float animCurrentTime = (float)_currentTimeProperty.GetValue(_cachedStateInstance);

                // Calculate time change to avoid micro-stutters
                float timeDelta = Mathf.Abs(animCurrentTime - lastTime);
                bool timeChanged = timeDelta > TIME_CHANGE_THRESHOLD;
                bool timeJumped = timeDelta > 0.1f; // Significant jump (scrubbing or loop)

                // Detect animation loop (time jumped backwards to near start)
                bool isLoop = isAnimPlaying && animCurrentTime < lastTime && animCurrentTime < 0.5f && lastTime > 0.5f;

                // Handle animation loop - restart audio from beginning
                if (isLoop && animCurrentTime <= audioClip.length)
                {
                    StopAudioPlayback();
                    StartAudioPlayback(animCurrentTime);
                }
                // Handle animation playback start
                else if (isAnimPlaying && timeChanged && !isPlaying && animCurrentTime <= audioClip.length)
                {
                    StartAudioPlayback(animCurrentTime);
                }
                // Handle scrubbing (timeline moved while not playing)
                else if (timeChanged && !isAnimPlaying && animCurrentTime <= audioClip.length)
                {
                    HandleScrubbing(animCurrentTime, timeJumped);
                }
                // Handle stop/pause
                else if (!isAnimPlaying && !timeChanged && isPlaying)
                {
                    StopAudioPlayback();
                }
                // Handle timeline position jump during playback
                else if (isAnimPlaying && isPlaying && timeJumped && !isLoop)
                {
                    SyncAudioPosition(animCurrentTime);
                }

                lastLastTime = lastTime;
                lastTime = animCurrentTime;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BetterAnimations] Audio playback error: {e.Message}");
            }
        }

        private static void StartAudioPlayback(float startTime)
        {
            isPlaying = true;
            hasStopped = false;
            int sampleStart = Mathf.Clamp(
                (int)(audioClip.samples * (startTime / audioClip.length)),
                0,
                audioClip.samples - 1
            );
            AudioUtility.PlayClip(audioClip);
            AudioUtility.SetClipVolume(volume);
            AudioUtility.SetClipSamplePosition(audioClip, sampleStart);
        }

        private static void HandleScrubbing(float currentTime, bool isJump)
        {
            if (!isPlaying || isJump)
            {
                hasStopped = false;
                if (!isPlaying)
                {
                    AudioUtility.PlayClip(audioClip);
                    AudioUtility.SetClipVolume(volume);
                    isPlaying = true;
                }
            }
            SyncAudioPosition(currentTime);
        }

        private static void SyncAudioPosition(float currentTime)
        {
            int sampleStart = Mathf.Clamp(
                (int)(audioClip.samples * (currentTime / audioClip.length)),
                0,
                audioClip.samples - 1
            );
            AudioUtility.SetClipSamplePosition(audioClip, sampleStart);
        }

        private static void StopAudioPlayback()
        {
            if (!hasStopped)
            {
                AudioUtility.StopAllClips();
                hasStopped = true;
            }
            isPlaying = false;
        }

        public static void OnAnimEditorGUI_Postfix(object __instance, EditorWindow parent, Rect position)
        {
            if (!isEnabled)
                return;

            try
            {
                DrawWaveformOverlay(__instance, parent, position);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BetterAnimations] Waveform overlay error: {e.Message}");
            }
        }

        static void DrawWaveformOverlay(object animEditorInstance, EditorWindow parentWindow, Rect position)
        {
            Assembly editorAssembly = typeof(Editor).Assembly;
            Type animEditorType = animEditorInstance.GetType();

            FieldInfo dopeSheetField = animEditorType.GetField("m_DopeSheet", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dopeSheetField == null) return;

            object dopeSheet = dopeSheetField.GetValue(animEditorInstance);
            if (dopeSheet == null) return;

            Type dopeSheetType = dopeSheet.GetType();

            PropertyInfo shownAreaProp = dopeSheetType.GetProperty("shownArea");
            PropertyInfo translationProp = dopeSheetType.GetProperty("translation");
            PropertyInfo rectProp = dopeSheetType.GetProperty("rect");

            if (shownAreaProp == null || translationProp == null || rectProp == null) return;

            Rect shownArea = (Rect)shownAreaProp.GetValue(dopeSheet);
            Vector2 translation = (Vector2)translationProp.GetValue(dopeSheet);
            Rect dopeSheetRect = (Rect)rectProp.GetValue(dopeSheet);

            FieldInfo stateField = animEditorType.GetField("m_State", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null) return;

            object state = stateField.GetValue(animEditorInstance);
            if (state == null) return;

            Type stateType = state.GetType();
            PropertyInfo currentTimeProp = stateType.GetProperty("currentTime");
            if (currentTimeProp == null) return;

            float currentTime = (float)currentTimeProp.GetValue(state);

            float timelineWidth = dopeSheetRect.width;
            if (shownArea.width <= 0) return;

            float pixelsPerSecond = timelineWidth / shownArea.width;

            // Calculate waveform position - always at bottom
            float actualHeight = isExpanded ? waveformHeight : 25f;
            float headerHeight = 25f;
            float waveformY = dopeSheetRect.y + dopeSheetRect.height - actualHeight;

            // Calculate total overlay area (header + settings panel if visible + content)
            float settingsPanelHeight = (showSettings && audioClip != null) ? 65f : 0f;
            float totalOverlayHeight = actualHeight + settingsPanelHeight;
            Rect totalOverlayRect = new Rect(dopeSheetRect.x, waveformY, dopeSheetRect.width, totalOverlayHeight);

            // CRITICAL: Block ALL events over our overlay to prevent passthrough to underlying controls
            Event evt = Event.current;
            bool mouseOverOverlay = totalOverlayRect.Contains(evt.mousePosition);

            if (mouseOverOverlay)
            {
                // Consume all mouse events to prevent interaction with timeline below
                if (evt.type == EventType.MouseDown ||
                    evt.type == EventType.MouseUp ||
                    evt.type == EventType.MouseDrag ||
                    evt.type == EventType.ScrollWheel)
                {
                    // Mark that we're handling this event
                    // We'll consume it after our controls process it
                }
            }

            // Draw header with improved styling
            Rect headerRect = new Rect(dopeSheetRect.x, waveformY, dopeSheetRect.width, headerHeight);
            EditorGUI.DrawRect(headerRect, new Color(0.22f, 0.22f, 0.22f, 1f));
            EditorGUI.DrawRect(new Rect(dopeSheetRect.x, waveformY, dopeSheetRect.width, 1), new Color(0.4f, 0.4f, 0.4f, 0.5f));

            float xOffset = dopeSheetRect.x + 5;

            // Foldout
            Rect foldoutRect = new Rect(xOffset, waveformY + 4, 15, 16);
            bool newExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, "", true);
            if (newExpanded != isExpanded)
            {
                isExpanded = newExpanded;
                parentWindow.Repaint();
            }
            xOffset += 20;

            // Audio clip selector
            if (allAudioClips != null && allAudioClips.Length > 0)
            {
                Rect clipRect = new Rect(xOffset, waveformY + 3, 150, 18);

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(clipRect, selectedAudioIndex, audioClipNames);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedAudioIndex = newIndex;
                    audioClip = allAudioClips[selectedAudioIndex];
                    CreateWaveform();
                    parentWindow.Repaint();
                }
                xOffset += 155;
            }
            else
            {
                GUI.Label(new Rect(xOffset, waveformY + 4, 150, 16), "No audio clips", EditorStyles.miniLabel);
                xOffset += 155;
            }

            // Refresh button
            Rect refreshRect = new Rect(xOffset, waveformY + 3, 60, 18);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && refreshRect.Contains(Event.current.mousePosition))
            {
                RefreshAudioClips();
                Event.current.Use();
                parentWindow.Repaint();
            }
            GUI.Button(refreshRect, "Refresh", EditorStyles.miniButton);
            xOffset += 65;

            // Settings button
            Rect settingsRect = new Rect(xOffset, waveformY + 3, 60, 18);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && settingsRect.Contains(Event.current.mousePosition))
            {
                showSettings = !showSettings;
                Event.current.Use();
                parentWindow.Repaint();
            }
            GUI.Button(settingsRect, showSettings ? "Hide" : "Settings", EditorStyles.miniButton);
            xOffset += 65;

            if (audioClip != null)
            {
                // Volume slider
                GUI.Label(new Rect(xOffset, waveformY + 4, 30, 16), "Vol:", EditorStyles.miniLabel);
                xOffset += 32;

                Rect volumeRect = new Rect(xOffset, waveformY + 6, 80, 14);
                EditorGUI.BeginChangeCheck();
                float newVolume = GUI.HorizontalSlider(volumeRect, volume, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    volume = newVolume;
                    if (isPlaying)
                    {
                        AudioUtility.SetClipVolume(volume);
                    }
                    parentWindow.Repaint();
                }
                xOffset += 85;

                // Volume percentage
                GUI.Label(new Rect(xOffset, waveformY + 4, 35, 16), $"{Mathf.RoundToInt(volume * 100)}%", EditorStyles.miniLabel);
                xOffset += 40;

                // Size slider when expanded
                if (isExpanded)
                {
                    GUI.Label(new Rect(xOffset, waveformY + 4, 30, 16), "Size:", EditorStyles.miniLabel);
                    xOffset += 32;

                    Rect sizeRect = new Rect(xOffset, waveformY + 6, 80, 14);
                    EditorGUI.BeginChangeCheck();
                    float newHeight = GUI.HorizontalSlider(sizeRect, waveformHeight, 60f, 200f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        waveformHeight = newHeight;
                        parentWindow.Repaint();
                    }
                    xOffset += 85;
                }

                // Info label (right-aligned)
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel);
                infoStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                infoStyle.alignment = TextAnchor.MiddleRight;
                Rect infoRect = new Rect(dopeSheetRect.x + dopeSheetRect.width - 100, waveformY + 4, 95, 16);
                GUI.Label(infoRect, $"♪ {audioClip.length:F2}s", infoStyle);
            }

            // Settings panel (expanded below header)
            if (showSettings && audioClip != null)
            {
                Rect settingsPanel = new Rect(dopeSheetRect.x, waveformY + headerHeight, dopeSheetRect.width, settingsPanelHeight);
                EditorGUI.DrawRect(settingsPanel, new Color(0.18f, 0.18f, 0.18f, 1f));

                float sy = waveformY + headerHeight + 5;
                float sx = dopeSheetRect.x + 10;

                // Waveform color
                GUI.Label(new Rect(sx, sy, 100, 16), "Waveform Color:", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                Color newWaveColor = EditorGUI.ColorField(new Rect(sx + 105, sy, 80, 16), waveformColor);
                if (EditorGUI.EndChangeCheck())
                {
                    waveformColor = newWaveColor;
                    parentWindow.Repaint();
                }

                // Background color
                GUI.Label(new Rect(sx + 200, sy, 100, 16), "Background:", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                Color newBgColor = EditorGUI.ColorField(new Rect(sx + 290, sy, 80, 16), bgColor);
                if (EditorGUI.EndChangeCheck())
                {
                    bgColor = newBgColor;
                    parentWindow.Repaint();
                }

                sy += 22;

                // Fade waveform toggle
                EditorGUI.BeginChangeCheck();
                bool newFade = EditorGUI.Toggle(new Rect(sx, sy, 150, 16), "Fade Waveform", fadeForm);
                if (EditorGUI.EndChangeCheck())
                {
                    fadeForm = newFade;
                    CreateWaveform();
                    parentWindow.Repaint();
                }

                // Generate waveform button
                Rect regenRect = new Rect(sx + 200, sy, 120, 18);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && regenRect.Contains(Event.current.mousePosition))
                {
                    CreateWaveform();
                    Event.current.Use();
                    parentWindow.Repaint();
                }
                GUI.Button(regenRect, "Regenerate Waveform", EditorStyles.miniButton);

                sy += 22;

                // Info text
                GUIStyle helpStyle = new GUIStyle(EditorStyles.miniLabel);
                helpStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                GUI.Label(new Rect(sx, sy, dopeSheetRect.width - 20, 16),
                    "Audio loops automatically when animation loops. Waveform syncs perfectly with timeline at x=0.",
                    helpStyle);

                // Adjust waveform position to account for settings panel
                waveformY += settingsPanelHeight;
                actualHeight -= settingsPanelHeight;
            }

            // Draw waveform content
            if (isExpanded && audioClip != null && waveformTexture != null)
            {
                float contentY = waveformY + headerHeight;
                float contentHeight = actualHeight - headerHeight;

                if (contentHeight > 10)
                {
                    // Draw background
                    Rect bgRect = new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, contentHeight);
                    EditorGUI.DrawRect(bgRect, bgColor);
                    EditorGUI.DrawRect(new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, 1), new Color(0.15f, 0.15f, 0.15f, 0.8f));
                    EditorGUI.DrawRect(new Rect(dopeSheetRect.x, contentY + contentHeight - 1, dopeSheetRect.width, 1), new Color(0.4f, 0.4f, 0.4f, 0.5f));

                    // Calculate waveform positioning - ensure it starts exactly at timeline 0
                    float waveformWidth = audioClip.length * pixelsPerSecond;
                    // waveformStartX should be where time=0 is on the timeline
                    float time0X = dopeSheetRect.x + ((0 - shownArea.x) * pixelsPerSecond);

                    // Clip region for waveform
                    GUI.BeginClip(new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, contentHeight));

                    Rect localWaveformRect = new Rect(
                        time0X - dopeSheetRect.x,  // Start exactly at timeline position 0
                        0,
                        waveformWidth,
                        contentHeight
                    );

                    Color oldColor = GUI.color;
                    GUI.color = waveformColor;
                    GUI.DrawTexture(localWaveformRect, waveformTexture, ScaleMode.StretchToFill);
                    GUI.color = oldColor;

                    GUI.EndClip();

                    // Draw grid lines (vertical lines every 5 seconds)
                    for (float t = 0; t <= audioClip.length; t += 5f)
                    {
                        float gridX = dopeSheetRect.x + ((t - shownArea.x) * pixelsPerSecond);
                        if (gridX >= dopeSheetRect.x && gridX <= dopeSheetRect.x + dopeSheetRect.width)
                        {
                            Rect gridLine = new Rect(gridX, contentY, 1, contentHeight);
                            EditorGUI.DrawRect(gridLine, new Color(1f, 1f, 1f, 0.08f));

                            // Time labels on grid
                            if (t > 0)
                            {
                                GUIStyle timeStyle = new GUIStyle(EditorStyles.miniLabel);
                                timeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                                timeStyle.fontSize = 9;
                                GUI.Label(new Rect(gridX + 2, contentY + 2, 40, 12), $"{t:F0}s", timeStyle);
                            }
                        }
                    }

                    // Time Indicator (playhead)
                    float currentTimeX = dopeSheetRect.x + ((currentTime - shownArea.x) * pixelsPerSecond);
                    if (currentTimeX >= dopeSheetRect.x && currentTimeX <= dopeSheetRect.x + dopeSheetRect.width)
                    {
                        Rect indicator = new Rect(currentTimeX - 1, contentY, 2, contentHeight);
                        EditorGUI.DrawRect(indicator, new Color(1f, 0.3f, 0.3f, 0.9f));

                        Rect topMarker = new Rect(currentTimeX - 3, contentY - 1, 6, 3);
                        EditorGUI.DrawRect(topMarker, new Color(1f, 0.3f, 0.3f, 1f));
                    }
                }
            }

            // Force repaint while playing
            if (isPlaying)
            {
                parentWindow.Repaint();
            }

            // CRITICAL: Only consume events in non-interactive areas to prevent passthrough
            // Don't consume events in header/settings areas - controls need them!
            // Only block events in the waveform content area
            if (mouseOverOverlay)
            {
                // Calculate the content area (below header and settings panel)
                float contentStartY = waveformY + headerHeight + settingsPanelHeight;
                bool mouseOverContentArea = evt.mousePosition.y >= contentStartY;

                // Only consume events if in the waveform content area (not over controls)
                if (mouseOverContentArea)
                {
                    if (evt.type == EventType.MouseDown ||
                        evt.type == EventType.MouseUp ||
                        evt.type == EventType.MouseDrag ||
                        evt.type == EventType.MouseMove ||
                        evt.type == EventType.ScrollWheel ||
                        evt.type == EventType.ContextClick)
                    {
                        evt.Use();
                    }
                }
                // For header/settings area, only consume non-interactive background events
                else
                {
                    // Let controls handle their events, only block background clicks
                    // The manual buttons already call evt.Use() when clicked
                    // Other controls (dropdowns, sliders) handle events automatically
                }
            }
        }

        static void CreateWaveform()
        {
            if (audioClip == null) return;

            // Clean up old texture
            if (waveformTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(waveformTexture);
                waveformTexture = null;
            }

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

            float[] samples = new float[audioClip.samples * audioClip.channels];
            audioClip.GetData(samples, 0);

            int step = Mathf.Max(1, samples.Length / WAVEFORM_WIDTH);
            int halfHeight = WAVEFORM_HEIGHT / 2;

            for (int x = 0; x < WAVEFORM_WIDTH; x++)
            {
                if (x * step >= samples.Length) break;

                // Sample multiple points for better peak detection
                float maxSample = 0;
                int startIdx = x * step;
                int endIdx = Mathf.Min(startIdx + step, samples.Length);
                for (int i = startIdx; i < endIdx; i++)
                {
                    maxSample = Mathf.Max(maxSample, Mathf.Abs(samples[i]));
                }

                int barHeight = Mathf.CeilToInt(Mathf.Clamp(maxSample * WAVEFORM_HEIGHT / 2, 0, WAVEFORM_HEIGHT / 2));

                for (int y = 0; y < barHeight; y++)
                {
                    Color pixelColor = Color.white;

                    if (fadeForm && gradientTexture != null)
                    {
                        int gradientY = Mathf.Clamp((int)((float)y / barHeight * (gradientHeight - 1)), 0, gradientHeight - 1);
                        pixelColor = gradientTexture.GetPixel(0, gradientY);
                    }

                    waveformTexture.SetPixel(x, halfHeight + y, pixelColor);
                    waveformTexture.SetPixel(x, halfHeight - y, pixelColor);
                }
            }

            waveformTexture.filterMode = FilterMode.Point;
            waveformTexture.Apply();

            if (gradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(gradientTexture);
            }

            Debug.Log($"[BetterAnimations] Waveform generated for {audioClip.name}");
        }

        public static void Unpatch()
        {
            if (harmony != null && isPatched)
            {
                harmony.UnpatchAll("com.betteranimations.animwindowpatch");
                isPatched = false;
                AudioUtility.StopAllClips();
                Debug.Log("[BetterAnimations] Patch removed");
            }
        }
    }
#endif
}
