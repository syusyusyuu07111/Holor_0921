using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;
using CriWare;

public class CriAudioDebugWatcher : MonoBehaviour
{
    private static CriAudioDebugWatcher instance;

    // プロジェクトで実際に使っているカテゴリ名に合わせてください
    private static readonly string[] CategoryNames = { "BGM", "SE", "ENV" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateWatcher()
    {
        if (instance != null) return;

        var go = new GameObject("[CRI Audio Debug Watcher]");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<CriAudioDebugWatcher>();

        Debug.Log("[CRI AUDIO DEBUG] Watcher created and DontDestroyOnLoad.");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        LogCriState("OnEnable (first scene)");
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LogCriState($"SceneLoaded: {scene.name} (mode: {mode})");
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        LogCriState($"ActiveSceneChanged: {oldScene.name} -> {newScene.name}");
    }

    /// <summary>
    /// CRIまわりの状態をまとめてログ出力
    /// </summary>
    private void LogCriState(string header)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== [CRI AUDIO DEBUG] " + header + " ==========");

        var active = SceneManager.GetActiveScene();
        sb.AppendLine($"Active Scene : {active.name}");

        // 初期化オブジェクト（複数あったら怪しい）
        var inits = FindAll<CriWareInitializer>();
        sb.AppendLine($"CriWareInitializer count : {inits.Length}");
        foreach (var init in inits)
        {
            sb.AppendLine($"  - {GetPath(init.gameObject)}  (scene: {init.gameObject.scene.name})");
        }

        // CriAtom
        var atoms = FindAll<CriAtom>();
        sb.AppendLine($"CriAtom count : {atoms.Length}");
        foreach (var atom in atoms)
        {
            sb.AppendLine($"  - {GetPath(atom.gameObject)}  (scene: {atom.gameObject.scene.name})");
        }

        // リスナー
        var listeners = FindAll<CriAtomListener>();
        sb.AppendLine($"CriAtomListener count : {listeners.Length}");
        foreach (var listener in listeners)
        {
            var t = listener.transform;
            sb.AppendLine(
                $"  - {GetPath(listener.gameObject)}  pos={t.position}  (scene: {listener.gameObject.scene.name})"
            );
        }

        // サウンドソース
        var sources = FindAll<CriAtomSource>();
        sb.AppendLine($"CriAtomSource count : {sources.Length}");
        foreach (var src in sources)
        {
            string path = GetPath(src.gameObject);
            string sceneName = src.gameObject.scene.name;
            string cueSheet = src.cueSheet;
            string cueName = src.cueName;
            float volume = src.volume;
            bool playOnStart = src.playOnStart;
            bool use3d = src.use3dPositioning;
            var status = src.status;   // Stop / Playing など

            sb.AppendLine(
                $"  - {path}  (scene: {sceneName})\n" +
                $"      cueSheet={cueSheet}, cueName={cueName}, volume={volume}, playOnStart={playOnStart}, 3D={use3d}, status={status}"
            );
        }

        // カテゴリ状態
        if (CategoryNames != null && CategoryNames.Length > 0)
        {
            sb.AppendLine("Category status:");
            foreach (var cat in CategoryNames)
            {
                try
                {
                    float vol = CriAtomExCategory.GetVolume(cat);
                    bool muted = CriAtomExCategory.IsMuted(cat);
                    bool paused = CriAtomExCategory.IsPaused(cat);
                    sb.AppendLine(
                        $"  - {cat} : volume={vol}, muted={muted}, paused={paused}"
                    );
                }
                catch
                {
                    sb.AppendLine($"  - {cat} : (カテゴリ情報取得でエラー。カテゴリ名が違う可能性あり)");
                }
            }
        }

        sb.AppendLine("=======================================================");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 非推奨の FindObjectsOfType の代わりラッパ
    /// </summary>
    private static T[] FindAll<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
    }

    /// <summary>
    /// ヒエラルキー上のフルパス文字列
    /// </summary>
    private static string GetPath(GameObject go)
    {
        var path = go.name;
        var t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
