using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Opens the PSX city scene when the editor is sitting on an empty "Untitled" scene,
/// so pressing Play always drops you into the rainy city.
/// </summary>
[InitializeOnLoad]
static class PSXAutoOpenScene
{
    const string ScenePath = "Assets/Scenes/PSXCity.unity";

    static PSXAutoOpenScene()
    {
        EditorApplication.delayCall += OpenIfUntitled;
    }

    static void OpenIfUntitled()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene active = SceneManager.GetActiveScene();
        bool untitled = string.IsNullOrEmpty(active.path) && !active.isDirty;
        if (!untitled || SceneManager.sceneCount > 1) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
        EditorSceneManager.OpenScene(ScenePath);
    }
}
