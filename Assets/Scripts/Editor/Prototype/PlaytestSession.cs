using UnityEditor;
using Ediki.Unity;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Owns the "play THIS encounter" override for exactly one play session.
    ///
    /// Armed just before entering play mode and cleared the moment the editor
    /// comes back, so the override can never outlive the session that asked for
    /// it. Without that, pressing Unity's own Play button a week later would
    /// silently boot somebody's half-finished test map instead of the shipped
    /// default, and nothing on screen would say why.
    ///
    /// InitializeOnLoad because the handler has to be re-registered after every
    /// domain reload — including the one that entering play mode causes.
    /// </summary>
    [InitializeOnLoad]
    public static class PlaytestSession
    {
        static PlaytestSession()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) Clear();
        }

        /// <summary>Resource name currently armed, or empty.</summary>
        public static string Armed => EditorPrefs.GetString(PrototypeBootstrap.EditorEncounterKey, string.Empty);

        public static bool IsArmed => !string.IsNullOrEmpty(Armed);

        public static void Arm(string resourceName)
        {
            EditorPrefs.SetString(PrototypeBootstrap.EditorEncounterKey, resourceName);
        }

        public static void Clear()
        {
            if (EditorPrefs.HasKey(PrototypeBootstrap.EditorEncounterKey))
                EditorPrefs.DeleteKey(PrototypeBootstrap.EditorEncounterKey);
        }
    }
}
