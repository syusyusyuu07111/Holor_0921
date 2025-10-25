using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering;                 // ★ 追加：URP Volume
using UnityEngine.Rendering.Universal;      // ★ 追加：URP Vignette

public class GameOver : MonoBehaviour
{
    public Transform Player;                    // プレイヤー
    public Transform Ghost;                     // 幽霊（追跡側）

    [Header("隠れ判定")]
    public HideCroset HideRef;                  // クローゼット隠れ制御
    public SearchChase GhostChase;              // 幽霊の状態取得用

    [Header("判定設定")]
    public float TriggerDistance = 0.735f;      // 距離がこの値以下でゲームオーバー
    public float CheckInterval = 0.05f;         // 距離チェック間隔（秒）

    [Header("シーン遷移")]
    public string GameoverScene = "";           // 空なら現シーンをリロード
    public bool StopAgentsOnGameOver = true;    // 遷移直前にNavMeshAgentを止める

    // ===== ポストプロセス（URP Volume） =====
    [Header("ポストプロセス（警告ビネット）")]
    public Volume PostVolume;                   // グローバルVolume（Vignette入り）
    public Color EdgeColor = Color.red;         // エッジ色（赤）
    public float WarnDistance = 2.0f;           // この距離から演出を開始
    [Range(0f, 1f)] public float MaxVignette = 0.45f; // 最大濃さ
    [Range(0.1f, 20f)] public float FadeSpeed = 6f;   // 追従速度（補間）
    public bool AlsoShakeChromatic = false;     // お好みで色収差も少し

    private bool _gameOverFired = false;        // 多重発火防止

    // 内部：Vignette/Chromatic 参照
    private Vignette _vig;
    private ChromaticAberration _ca;
    private bool _hasVig = false;
    private bool _hasCA = false;

    // 内部：現在の強度（スムージング用）
    private float _currIntensity = 0f;

    // ===== サウンド再生用 =====
    [Header("SE再生用")]
    public AudioManager AudioMgr;               // 捕まった時のSEを鳴らすための参照（インスペクタで割り当て）

    void Awake()
    {
        // 参照の自動補完
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) Player = p.transform;
        }
        if (!Ghost)
        {
            var g = GameObject.FindGameObjectWithTag("Ghost"); // タグ割り当てる
            if (g) Ghost = g.transform;
        }

        if (!HideRef)
        {
#if UNITY_2023_1_OR_NEWER
            HideRef = UnityEngine.Object.FindFirstObjectByType<HideCroset>();
            if (!HideRef) HideRef = UnityEngine.Object.FindAnyObjectByType<HideCroset>();
#else
            HideRef = UnityEngine.Object.FindObjectOfType<HideCroset>();
#endif
        }

        if (!GhostChase && Ghost)
        {
            GhostChase = Ghost.GetComponent<SearchChase>();
        }

        // Volume 自動補完
        if (!PostVolume)
        {
#if UNITY_2023_1_OR_NEWER
            PostVolume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (!PostVolume) PostVolume = UnityEngine.Object.FindAnyObjectByType<Volume>();
#else
            PostVolume = UnityEngine.Object.FindObjectOfType<Volume>();
#endif
        }

        // Vignette/Chromatic をプロファイルから取得
        if (PostVolume && PostVolume.profile)
        {
            _hasVig = PostVolume.profile.TryGet(out _vig);
            _hasCA = PostVolume.profile.TryGet(out _ca);
            if (_hasVig)
            {
                _vig.active = true;
                _vig.color.Override(EdgeColor);
                _vig.smoothness.Override(0.9f); // 周辺に寄せる
                _vig.intensity.Override(0f);    // 初期は無効相当
            }
            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.active = true;
                _ca.intensity.Override(0f);
            }
        }
    }

    void OnEnable()
    {
        StartCoroutine(DistanceWatchLoop());
    }

    void Update()
    {
        // 毎フレーム：距離に応じてビネット強度をスムーズに更新
        UpdateDangerVignette();
    }

    IEnumerator DistanceWatchLoop()
    {
        var wait = new WaitForSeconds(CheckInterval);
        while (enabled && !_gameOverFired)
        {
            if (Player && Ghost)
            {
                // 距離計測 & ログ出力
                float dist = Vector3.Distance(Player.position, Ghost.position);
                // Debug.Log($"[GameOver] distance = {dist:0.000}");

                if (ShouldSkipCatch())
                {
                    yield return wait;
                    continue;
                }

                // 判定：距離がトリガー値以下
                if (dist <= TriggerDistance)
                {
                    FireGameOver();
                    yield break;
                }
            }
            yield return wait;
        }
    }

    private void UpdateDangerVignette()
    {
        if (!_hasVig || !Player || !Ghost) return;

        if (ShouldSkipCatch())
        {
            _currIntensity = Mathf.MoveTowards(_currIntensity, 0f, FadeSpeed * Time.deltaTime);
            _vig.intensity.Override(_currIntensity);
            if (_hasCA && AlsoShakeChromatic) _ca.intensity.Override(_currIntensity * 0.55f);
            return;
        }

        float dist = Vector3.Distance(Player.position, Ghost.position);

        // WarnDistance…TriggerDistance の間で 0→1 に線形マップ
        // Warn より遠い: 0、Trigger 以下: 1
        float t = 0f;
        if (dist <= WarnDistance)
        {
            // InverseLerp(遠い→近い)だと扱いづらいので、手で正規化
            float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
            t = Mathf.Clamp01((WarnDistance - dist) / span);
        }

        // 目標強度
        float target = t * MaxVignette;

        // スムーズ追従（フレームレートに相対）
        _currIntensity = Mathf.MoveTowards(_currIntensity, target, FadeSpeed * Time.deltaTime);

        // 反映
        _vig.color.Override(EdgeColor);
        _vig.intensity.Override(_currIntensity);

        // お好みで色収差を少し
        if (_hasCA && AlsoShakeChromatic)
        {
            // Trigger 付近で最大0.25くらい（過剰だと酔う）
            _ca.intensity.Override(_currIntensity * 0.55f);
        }
    }

    private void FireGameOver()
    {
        if (_gameOverFired) return;
        _gameOverFired = true;

        // ★ 捕まったSEを鳴らす（AudioManager経由）
        if (AudioMgr != null)
        {
            AudioMgr.CatchSource();
        }
        else
        {
            Debug.LogWarning("[GameOver] AudioMgr が割り当てられていないので、捕まったSEは鳴りません。");
        }

        // 遷移前にNavMeshAgentを止めて暴れ防止
        if (StopAgentsOnGameOver)
        {
            var a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            var a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;
            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        // シーン遷移
        if (!string.IsNullOrEmpty(GameoverScene))
        {
            SceneManager.LoadScene(GameoverScene);
        }
        else
        {
            // 遷移先未指定なら現シーンをリロード
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }

    private bool ShouldSkipCatch()
    {
        if (!HideRef || !HideRef.hide) return false;

        int ghostState = (GhostChase ? GhostChase.GetState() : 1);
        return ghostState == 1;
    }

    private void OnDrawGizmosSelected()
    {
        if (Player && Ghost)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Ghost.position, Mathf.Max(TriggerDistance, 0.01f));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, Ghost.position);
        }
    }
}
