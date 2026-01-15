using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BetterAnimations
{
#if UNITY_EDITOR
    public static class AudioUtility
    {
        // Cache reflection lookups for performance
        private static Type _audioUtilClass;
        private static MethodInfo _playPreviewClipMethod;
        private static MethodInfo _stopAllPreviewClipsMethod;
        private static MethodInfo _setPreviewClipSamplePositionMethod;
        private static MethodInfo _getPreviewClipSamplePositionMethod;
        private static MethodInfo _setPreviewClipVolumeMethod;

        private static Type AudioUtilClass
        {
            get
            {
                if (_audioUtilClass == null)
                {
                    Assembly unityEditorAssembly = typeof(AudioImporter).Assembly;
                    _audioUtilClass = unityEditorAssembly.GetType("UnityEditor.AudioUtil");

                    if (_audioUtilClass == null)
                    {
                        Debug.LogError("[AudioUtility] Failed to find UnityEditor.AudioUtil type");
                    }
                }
                return _audioUtilClass;
            }
        }

        private static MethodInfo PlayPreviewClipMethod
        {
            get
            {
                if (_playPreviewClipMethod == null && AudioUtilClass != null)
                {
                    _playPreviewClipMethod = AudioUtilClass.GetMethod(
                        "PlayPreviewClip",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
                        null
                    );
                }
                return _playPreviewClipMethod;
            }
        }

        private static MethodInfo StopAllPreviewClipsMethod
        {
            get
            {
                if (_stopAllPreviewClipsMethod == null && AudioUtilClass != null)
                {
                    _stopAllPreviewClipsMethod = AudioUtilClass.GetMethod(
                        "StopAllPreviewClips",
                        BindingFlags.Static | BindingFlags.Public
                    );
                }
                return _stopAllPreviewClipsMethod;
            }
        }

        private static MethodInfo SetPreviewClipSamplePositionMethod
        {
            get
            {
                if (_setPreviewClipSamplePositionMethod == null && AudioUtilClass != null)
                {
                    _setPreviewClipSamplePositionMethod = AudioUtilClass.GetMethod(
                        "SetPreviewClipSamplePosition",
                        BindingFlags.Static | BindingFlags.Public
                    );
                }
                return _setPreviewClipSamplePositionMethod;
            }
        }

        private static MethodInfo GetPreviewClipSamplePositionMethod
        {
            get
            {
                if (_getPreviewClipSamplePositionMethod == null && AudioUtilClass != null)
                {
                    _getPreviewClipSamplePositionMethod = AudioUtilClass.GetMethod(
                        "GetPreviewClipSamplePosition",
                        BindingFlags.Static | BindingFlags.Public
                    );
                }
                return _getPreviewClipSamplePositionMethod;
            }
        }

        private static MethodInfo SetPreviewClipVolumeMethod
        {
            get
            {
                if (_setPreviewClipVolumeMethod == null && AudioUtilClass != null)
                {
                    _setPreviewClipVolumeMethod = AudioUtilClass.GetMethod(
                        "SetPreviewClipVolume",
                        BindingFlags.Static | BindingFlags.Public
                    );
                }
                return _setPreviewClipVolumeMethod;
            }
        }

        public static void PlayClip(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioUtility] Cannot play null clip");
                return;
            }

            try
            {
                PlayPreviewClipMethod?.Invoke(null, new object[] { clip, 0, false });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioUtility] Failed to play clip: {e.Message}");
            }
        }

        public static void StopAllClips()
        {
            try
            {
                StopAllPreviewClipsMethod?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioUtility] Failed to stop clips: {e.Message}");
            }
        }

        public static void SetClipSamplePosition(AudioClip clip, int iSamplePosition)
        {
            if (clip == null) return;

            try
            {
                SetPreviewClipSamplePositionMethod?.Invoke(null, new object[] { clip, iSamplePosition });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioUtility] Failed to set sample position: {e.Message}");
            }
        }

        public static int GetClipSamplePosition(AudioClip clip)
        {
            if (clip == null) return 0;

            try
            {
                object result = GetPreviewClipSamplePositionMethod?.Invoke(null, new object[] { clip });
                return result != null ? (int)result : 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioUtility] Failed to get sample position: {e.Message}");
                return 0;
            }
        }

        private static bool _volumeWarningShown = false;

        public static void SetClipVolume(float volume)
        {
            try
            {
                volume = Mathf.Clamp01(volume);

                if (SetPreviewClipVolumeMethod == null)
                {
                    if (!_volumeWarningShown)
                    {
                        Debug.LogWarning("[AudioUtility] SetPreviewClipVolume method not found - volume control unavailable in this Unity version");
                        _volumeWarningShown = true;
                    }
                    return;
                }

                SetPreviewClipVolumeMethod.Invoke(null, new object[] { volume });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioUtility] Failed to set volume: {e.Message}\n{e.StackTrace}");
            }
        }
    }
#endif
}