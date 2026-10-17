using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HoMM5.EditorTools
{
    /// <summary>
    /// Play normally enters MainMenu, even if a map-inspection scene is open.
    /// This does not overwrite, delete or save the user's inspected scene.
    /// </summary>
    [InitializeOnLoad]
    public static class H5PlayModeEntry
    {
        const string MenuPath = "Assets/Scenes/MainMenu.unity";
        const string TogglePath = "Tools/HoMM5/Play From Current Scene (debug)";
        static string Preference => "HoMM5.PlayCurrentScene." + Application.dataPath;

        static H5PlayModeEntry() { EditorApplication.delayCall += Configure; }

        static void Configure()
        {
            EditorSceneManager.playModeStartScene = EditorPrefs.GetBool(Preference, false)
                ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath);
        }

        [MenuItem("Tools/HoMM5/Open Main Menu Scene")]
        public static void OpenMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(MenuPath);
        }

        [MenuItem(TogglePath)]
        public static void Toggle()
        {
            EditorPrefs.SetBool(Preference, !EditorPrefs.GetBool(Preference, false));
            Configure();
        }

        [MenuItem(TogglePath, true)]
        public static bool ValidateToggle()
        {
            Menu.SetChecked(TogglePath, EditorPrefs.GetBool(Preference, false));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
