using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;
using CriWare;

public class CriAudioDebugWatcher : MonoBehaviour
{
    private static CriAudioDebugWatcher instance;

    /*
        プロジェクトで実際に使っているカテゴリ名
        ・ここに書いたカテゴリの Volume / Mute / Pause 状態を出す
        ・カテゴリ名が違うと例外になるので注意
    */
    private static readonly string[] CategoryNames = { "BGM", "SE", "ENV" };

    //================
    // 起動時に自動生成する（Scene読み込み後に1回だけ作る）
    // ・DontDestroyOnLoad でシーン切替をまたいでも残す
    // ・Audio周りの「初期化が増殖していないか」を追うためのデバッグ用途
    //================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateWatcher()
    {
        if (instance != null) return;

        var go = new GameObject("[CRI Audio Debug Watcher]");
        Object.DontDestroyOnLoad(go);

        instance = go.AddComponent<CriAudioDebugWatcher>();

        Debug.Log("[CRI AUDIO DEBUG] Watcher created and DontDestroyOnLoad.");
    }

    //================
    // シーンイベントを購読して、切替のタイミングでCRI状態を吐く
    //================
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        // 起動直後の状態を確認する（最初のシーン用）
        LogCriState("OnEnable (first scene)");
    }

    //================
    // シーンイベントの購読解除（多重登録の事故防止）
    //================
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    //================
    // シーン読み込み完了時のログ
    //================
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LogCriState($"SceneLoaded: {scene.name} (mode: {mode})");
    }

    //================
    // アクティブシーンが切り替わった時のログ
    //================
    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        LogCriState($"ActiveSceneChanged: {oldScene.name} -> {newScene.name}");
    }

    /*
        CRIまわりの状態をまとめてログ出力する

        何を見るためのログか：
        ・CriWareInitializer / CriAtom / Listener が増殖していないか
        ・CriAtomSource がどのシーン由来か（DontDestroyで残り続けてないか）
        ・カテゴリ(BGM/SE/ENV)の volume/mute/pause が想定通りか
    */
    private void LogCriState(string header)
    {
        var sb = new StringBuilder();

        sb.AppendLine("========== [CRI AUDIO DEBUG] " + header + " ==========");

        //================
        // 現在のアクティブシーン
        //================
        var active = SceneManager.GetActiveScene();
        sb.AppendLine($"Active Scene : {active.name}");

        //================
        // 初期化オブジェクト（複数あると二重初期化の疑い）
        //================
        var inits = FindAll<CriWareInitializer>();
        sb.AppendLine($"CriWareInitializer count : {inits.Length}");
        foreach (var init in inits)
        {
            sb.AppendLine($"  - {GetPath(init.gameObject)}  (scene: {init.gameObject.scene.name})");
        }

        //================
        // CriAtom（音声システムの中核。増殖してないか確認）
        //================
        var atoms = FindAll<CriAtom>();
        sb.AppendLine($"CriAtom count : {atoms.Length}");
        foreach (var atom in atoms)
        {
            sb.AppendLine($"  - {GetPath(atom.gameObject)}  (scene: {atom.gameObject.scene.name})");
        }

        //================
        // リスナー（3D音の基準。位置が想定通りか確認）
        //================
        var listeners = FindAll<CriAtomListener>();
        sb.AppendLine($"CriAtomListener count : {listeners.Length}");
        foreach (var listener in listeners)
        {
            var t = listener.transform;
            sb.AppendLine(
                $"  - {GetPath(listener.gameObject)}  pos={t.position}  (scene: {listener.gameObject.scene.name})"
            );
        }

        //================
        // サウンドソース（今鳴ってる/鳴る設定の source を一覧化）
        //================
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

            // Stop / Playing など（どのソースが生きてるか確認用）
            var status = src.status;

            sb.AppendLine(
                $"  - {path}  (scene: {sceneName})\n" +
                $"      cueSheet={cueSheet}, cueName={cueName}, volume={volume}, playOnStart={playOnStart}, 3D={use3d}, status={status}"
            );
        }

        //================
        // カテゴリ状態（BGM/SE/ENVなど）
        // ・音量が0になってないか
        // ・Mute/Pauseが残りっぱなしになってないか
        //================
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

                    sb.AppendLine($"  - {cat} : volume={vol}, muted={muted}, paused={paused}");
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

    /*
        非推奨の FindObjectsOfType の代わり
        ・Inactiveも含めて拾う（DontDestroy系の残骸も検知したい）
        ・Sortしない（デバッグ用途なので速度優先）
    */
    private static T[] FindAll<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
    }

    /*
        ヒエラルキー上のフルパス文字列を作る
        ・どの親の下にいるオブジェクトか、ログだけで追えるようにする
    */
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