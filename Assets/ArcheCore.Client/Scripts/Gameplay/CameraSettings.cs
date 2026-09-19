using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// Persisted camera preferences, alongside PlayerInputActions' key
    /// bindings - the two halves of a controls options screen.
    ///
    /// Static because there is one camera and one player. It reads lazily
    /// and writes through, so an options slider can assign straight to
    /// Sensitivity and it takes effect on the next mouse movement with no
    /// apply step. Nothing here is wired into the camera's serialized
    /// defaults: an unset value falls back to whatever the prefab says,
    /// which is what makes the inspector fields still meaningful as the
    /// out-of-box tuning.
    ///
    /// Sensitivity is intentionally NOT normalised for DPI. See the note on
    /// MMOCamera.mouseSensitivity - players tune this to their own hardware,
    /// and that is the point of exposing it.
    /// </summary>
    public static class CameraSettings
    {
        private const string SensitivityKey = "ArcheCore.Camera.Sensitivity";
        private const string VerticalScaleKey = "ArcheCore.Camera.VerticalScale";
        private const string InvertYKey = "ArcheCore.Camera.InvertY";

        private static float? _sensitivity;
        private static float? _verticalScale;
        private static bool? _invertY;

        /// <summary>Degrees per unit of mouse delta. Zero means "unset -
        /// use the camera's own default".</summary>
        public static float Sensitivity
        {
            get
            {
                _sensitivity ??= PlayerPrefs.GetFloat(SensitivityKey, 0f);
                return _sensitivity.Value;
            }
            set
            {
                _sensitivity = Mathf.Clamp(value, 0.01f, 2f);
                PlayerPrefs.SetFloat(SensitivityKey, _sensitivity.Value);
                PlayerPrefs.Save();
            }
        }

        public static float VerticalScale
        {
            get
            {
                _verticalScale ??= PlayerPrefs.GetFloat(VerticalScaleKey, 0f);
                return _verticalScale.Value;
            }
            set
            {
                _verticalScale = Mathf.Clamp(value, 0.1f, 3f);
                PlayerPrefs.SetFloat(VerticalScaleKey, _verticalScale.Value);
                PlayerPrefs.Save();
            }
        }

        public static bool HasInvertY =>
            PlayerPrefs.HasKey(InvertYKey) || _invertY.HasValue;

        public static bool InvertY
        {
            get
            {
                _invertY ??= PlayerPrefs.GetInt(InvertYKey, 0) != 0;
                return _invertY.Value;
            }
            set
            {
                _invertY = value;
                PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void ResetToDefaults()
        {
            _sensitivity = null;
            _verticalScale = null;
            _invertY = null;

            PlayerPrefs.DeleteKey(SensitivityKey);
            PlayerPrefs.DeleteKey(VerticalScaleKey);
            PlayerPrefs.DeleteKey(InvertYKey);
            PlayerPrefs.Save();
        }
    }
}