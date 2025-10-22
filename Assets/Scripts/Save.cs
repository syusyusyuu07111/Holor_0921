using UnityEngine;
using UnityEngine.SceneManagement;



public class Save : MonoBehaviour
{
    // 既存：インスペクタから true にするとセーブに反映（デバッグ用にも使える）
    [Tooltip("前段のチュートリアルをクリア済みか（保存対象）。true にすると保存され、次回以降は前段をスキップします。")]
    public bool ClereTutorial = false;

    // ---- オプション ----
    [Header("起動時の挙動（前段クリア済みのとき）")]
    [Tooltip("シーンロード時に前段スキップを自動適用する")]
    public bool ApplySkipOnSceneLoad = true;

    [Tooltip("スキップ適用後に Step1 テキストを即座に表示する（任意）")]
    public bool ShowStep1WhenSkipped = true;

    [Tooltip("Tutorial を自動で探す（参照未設定時の保険）")]
    public bool AutoFindTutorial = true;

    [Tooltip("シーン上の Tutorial（任意でアサイン推奨）")]
    public Tutorial TutorialRef;

    private const string Key_TutorialCleared = "TutorialCleared";
    private bool _appliedThisScene = false;   // 同一シーンで多重適用しないためのガード

    // ====== ライフサイクル ======
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 起動直後のシーンでも、既にクリア済みなら適用（タイトル→即ゲームシーン等のケース向け）
        TryApplySkipIfCleared();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        _appliedThisScene = false;      // シーン変わったので解除
        if (ApplySkipOnSceneLoad)
            TryApplySkipIfCleared();
    }

    private void Start()
    {
        // 既存コメントに合わせて Update だけでなく Start でも保存状態を反映
        if (ClereTutorial && !IsSavedCleared())
        {
            SaveClearedFlag();
        }
    }

    private void Update()
    {
        // 既存コメントの意図：実行中に「クリア扱いに切り替え」→保存
        if (ClereTutorial && !IsSavedCleared())
        {
            SaveClearedFlag();
        }

        // 実行中に「未クリアへ戻す」運用もしたい場合は下を使う（任意）
        // if (!ClereTutorial && IsSavedCleared()) { ClearSavedFlag(); }
    }

    // ====== 外部API ======

    /// <summary>前段のチュートリアルをクリア扱いにして保存（外部から呼ぶ想定）</summary>
    public void MarkTutorialCleared()
    {
        if (!ClereTutorial) ClereTutorial = true;
        SaveClearedFlag();
    }

    /// <summary>保存上のフラグを消す（デバッグ用途）</summary>
    public void ClearSavedFlag()
    {
        PlayerPrefs.DeleteKey(Key_TutorialCleared);
        PlayerPrefs.Save();
        Debug.Log("[Save] TutorialCleared を削除しました。");
    }

    // ====== 内部：保存/読込 ======
    private bool IsSavedCleared()
    {
        return PlayerPrefs.GetInt(Key_TutorialCleared, 0) == 1;
    }

    private void SaveClearedFlag()
    {
        PlayerPrefs.SetInt(Key_TutorialCleared, 1);
        PlayerPrefs.Save();
        Debug.Log("[Save] TutorialCleared = true を保存しました。");
    }

    // ====== 内部：前段スキップ適用 ======
    private void TryApplySkipIfCleared()
    {
        if (_appliedThisScene) return;
        if (!IsSavedCleared()) return;

        // Tutorial 参照取得
        if (!TutorialRef && AutoFindTutorial)
        {
#if UNITY_2023_1_OR_NEWER
            TutorialRef = UnityEngine.Object.FindAnyObjectByType<Tutorial>(FindObjectsInactive.Include);
#else
            TutorialRef = FindObjectOfType<Tutorial>(true);
#endif
        }

        if (!TutorialRef)
        {
            // このシーンに Tutorial が無いなら何もしない（タイトル等）
            return;
        }

        // すでにスキップ適用済みなら何もしない
        if (!TutorialRef.EnableBasicTutorial)
        {
            _appliedThisScene = true;
            return;
        }

        // 前段をスキップ（あなたの Tutorial 側は「!EnableBasicTutorial || _basicDone」でゲートしているので、これで本編解禁）
        TutorialRef.EnableBasicTutorial = false;
        _appliedThisScene = true;
        Debug.Log("[Save] 前段スキップを適用（Tutorial.EnableBasicTutorial = false）");

        // 好みに応じて Step1 を即表示
        if (ShowStep1WhenSkipped)
        {
            TutorialRef.Step1();
        }

        // 抽選開始まで自動ですっ飛ばしたい場合は必要に応じて以下を解放
        // TutorialRef.DoStep3();
    }
}
