using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using HarmonyLib;

namespace BetterAnimations
{
#if UNITY_EDITOR
    public enum WaveformPosition
    {
        Top,
        Bottom
    }

    [InitializeOnLoad]
    public static class AnimationWindowPatcher
    {
        private static Harmony harmony;

        public static AudioClip audioClip;
        public static Texture2D waveformTexture;
        public static Color waveformColor = new Color(1f, 0.3f, 0.3f, 0.6f);
        public static Color bgColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        public static bool isEnabled = false;
        public static bool isExpanded = true;
        public static float waveformHeight = 80f;
        public static float volume = 1.0f;

        // Positioning options
        public static WaveformPosition waveformPosition = WaveformPosition.Bottom;

        private static bool isPatched = false;
        private static bool isPlaying = false;
        private static float lastTime = 0;
        private static float lastLastTime = 0;
        private static bool hasStopped = false;

        // Cache reflection lookups for performance
        private static Type _animationWindowStateType;
        private static PropertyInfo _playingProperty;
        private static PropertyInfo _currentTimeProperty;
        private static object _cachedStateInstance;
        private static double _lastStateCacheTime;
        private const double STATE_CACHE_LIFETIME = 0.5; // Refresh cache every 0.5 seconds

        // Threshold to prevent micro-movements from triggering audio
        private const float TIME_CHANGE_THRESHOLD = 0.001f;

        static AnimationWindowPatcher()
        {
            EditorApplication.delayCall += DelayedPatch;
            EditorApplication.update += UpdateAudioPlayback;
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

                // Update volume when playing
                if (isPlaying)
                {
                    AudioUtility.SetClipVolume(volume);
                }

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
            if (!isEnabled || audioClip == null || waveformTexture == null)
                return;

            try
            {
                DrawWaveformOverlay(__instance, position);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BetterAnimations] Waveform overlay error: {e.Message}");
            }
        }

        static void DrawWaveformOverlay(object animEditorInstance, Rect position)
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
            float waveformWidth = audioClip.length * pixelsPerSecond;

            float actualHeight = isExpanded ? waveformHeight : 20f;
            float headerHeight = 20f;

            // Calculate waveform position based on setting
            float waveformY;
            float waveformStartX = dopeSheetRect.x + translation.x;

            if (waveformPosition == WaveformPosition.Bottom)
            {
                // Position at bottom of dopesheet area for better integration
                waveformY = dopeSheetRect.y + dopeSheetRect.height - actualHeight;
            }
            else
            {
                // Original top position (below time ruler)
                float timeRulerHeight = 17f;
                waveformY = position.y + timeRulerHeight;

                if (waveformY + actualHeight > dopeSheetRect.y)
                {
                    waveformY = dopeSheetRect.y - actualHeight - 2;
                }
            }

            // Draw header with improved styling
            Rect headerRect = new Rect(dopeSheetRect.x, waveformY, dopeSheetRect.width, headerHeight);
            EditorGUI.DrawRect(headerRect, new Color(0.22f, 0.22f, 0.22f, 1f));

            // Add subtle top border for better separation
            EditorGUI.DrawRect(new Rect(dopeSheetRect.x, waveformY, dopeSheetRect.width, 1), new Color(0.4f, 0.4f, 0.4f, 0.5f));

            Rect foldoutRect = new Rect(dopeSheetRect.x + 5, waveformY + 2, 200, 16);
            bool newExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, $"♪ {audioClip.name}", true);
            if (newExpanded != isExpanded)
            {
                isExpanded = newExpanded;
            }
            GUIStyle headerLabelStyle = new GUIStyle(EditorStyles.miniLabel);
            headerLabelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            headerLabelStyle.alignment = TextAnchor.MiddleRight;

            Rect infoRect = new Rect(dopeSheetRect.x + dopeSheetRect.width - 150, waveformY + 2, 140, 16);
            GUI.Label(infoRect, $"{audioClip.length:F2}s", headerLabelStyle);

            if (isExpanded)
            {
                Rect sliderLabelRect = new Rect(dopeSheetRect.x + dopeSheetRect.width - 290, waveformY + 2, 40, 16);
                GUI.Label(sliderLabelRect, "Size:", EditorStyles.miniLabel);

                Rect sliderRect = new Rect(dopeSheetRect.x + dopeSheetRect.width - 250, waveformY + 2, 90, 16);
                float newHeight = GUI.HorizontalSlider(sliderRect, waveformHeight, 60f, 200f);
                if (newHeight != waveformHeight)
                {
                    waveformHeight = newHeight;
                }
            }

            if (isExpanded)
            {
                float contentY = waveformY + headerHeight;
                float contentHeight = actualHeight - headerHeight;

                // Draw background
                Rect bgRect = new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, contentHeight);
                EditorGUI.DrawRect(bgRect, bgColor);

                // Draw borders for visual separation
                EditorGUI.DrawRect(new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, 1), new Color(0.15f, 0.15f, 0.15f, 0.8f));
                EditorGUI.DrawRect(new Rect(dopeSheetRect.x, contentY + contentHeight - 1, dopeSheetRect.width, 1), new Color(0.4f, 0.4f, 0.4f, 0.5f));

                GUI.BeginClip(new Rect(dopeSheetRect.x, contentY, dopeSheetRect.width, contentHeight));

                Rect localWaveformRect = new Rect(
                    waveformStartX - dopeSheetRect.x,
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
                    // Draw playhead line with improved visibility
                    Rect indicator = new Rect(currentTimeX - 1, contentY, 2, contentHeight);
                    EditorGUI.DrawRect(indicator, new Color(1f, 0.3f, 0.3f, 0.9f));

                    // Draw playhead marker at top
                    Rect topMarker = new Rect(currentTimeX - 3, contentY - 1, 6, 3);
                    EditorGUI.DrawRect(topMarker, new Color(1f, 0.3f, 0.3f, 1f));
                }
            }
        }

        // Unpatch beim Deaktivieren (optional)
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