using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering;                 // URP Volume
using UnityEngine.Rendering.Universal;      // URP Vignette
using TMPro;                                // TMPテキスト表示用

public class GameOver : MonoBehaviour
{
    public Transform Player;                    // プレイヤー
    public Transform Ghost;                     // 幽霊（初期参照用 / フォールバック）

    [Header("隠れ判定")]
    public HideCroset HideRef;                  // クローゼット隠れ制御
    public SearchChase GhostChase;              // 幽霊の状態を見る用

    [Header("判定設定")]
    public float TriggerDistance = 0.735f;      // これ以下なら捕まる
    public float CheckInterval = 0.05f;         // 距離チェック間隔（秒）

    [Header("シーン遷移")]
    public string GameoverScene = "";           // 空なら現シーンをリロード
    public bool StopAgentsOnGameOver = true;    // 遷移前にNavMeshAgentを止める

    // ===== 画面の「やばいよ」ビネット =====
    [Header("ポストプロセス（警告ビネット）")]
    public Volume PostVolume;                   // URP Volume（Vignetteとか入ってるやつ）
    public Color EdgeColor = Color.red;         // 周辺の色
    public float WarnDistance = 2.0f;           // この距離以内でビネットが濃くなる
    [Range(0f, 1f)] public float MaxVignette = 0.45f;
    [Range(0.1f, 20f)] public float FadeSpeed = 6f;
    public bool AlsoShakeChromatic = false;     // 色収差も揺らしたいならON

    private bool _gameOverFired = false;        // 多重発火防止

    // Volume内のエフェクト
    private Vignette _vig;
    private ChromaticAberration _ca;
    private bool _hasVig = false;
    private bool _hasCA = false;

    // 徐々に追従させるための現在値
    private float _currIntensity = 0f;

    // ===== サウンド =====
    [Header("SE再生用")]
    public AudioManager AudioMgr;               // 捕まった時のSEを鳴らすための参照

    // ===== カメラ演出 =====
    [Header("ゲームオーバー演出カメラ")]
    public Camera MainCamera;                   // 普段のプレイ用
    public Camera KillCamera;                   // 演出用（幽霊を映す）

    [Header("キルカメラ設定")]
    public float KillCamNearClip = 0.01f;       // 近距離でも透けないようNearClip低め

    [Header("カメラ自動配置（幽霊基準）")]
    public float CamDistFromGhost = 0.4f;       // 幽霊.forward 方向にどれだけ前
    public float CamSideOffset = 0.0f;          // 幽霊.right 方向にどれだけ横
    public float CamHeightOffset = 0.0f;        // 幽霊.position.y からの足し高さ

    [Header("キルカメラ向きオフセット")]
    public float CamLookPitchOffsetDeg = 0f;    // カメラの上下向き(回転Xだけ足す角度)

    public float CameraCutDelay = 0.05f;        // 捕まった直後、カメラ切替までのタメ
    public float FallbackDelay = 2.0f;          // アニメ遷移が入らない時の保険タイム

    [Header("ゲームオーバー演出の余韻")]
    public float HoldAfterAnimSeconds = 3.0f;   // アニメ終わってから見せる時間

    [Header("カメラ揺れ")]
    public bool EnableCameraShake = true;
    public float ShakeDuration = 0.3f;          // 掴まれた瞬間の強い揺れの長さ
    public float ShakeAmplitude = 0.02f;        // 掴まれた瞬間の揺れの大きさ
    public float HoldShakeAmplitude = 0.02f;    // 余韻中の揺れの大きさ

    [Header("フェードアウト")]
    public CanvasGroup FadeCanvasGroup;         // 暗転用CanvasGroup(Alpha0開始)
    public float FadeDuration = 1.0f;

    [Header("幽霊アニメーション")]
    public Animator GhostAnimator;              // 捕まえモーションを再生するAnimator
    public string GameOverBoolName = "GameOver";// これをtrueにすると捕まえアニメに入る想定
    public int GhostAnimLayer = 0;              // そのアニメがあるレイヤー(index)
    public string GameOverAnimTag = "GameOver"; // そのステートに付いてるTag名

    [Header("プレイヤー表示制御")]
    public bool HidePlayerOnGameOver = true;    // 捕まった瞬間にプレイヤーを非表示にするか

    [Header("ゲームオーバーテキスト表示(TMP)")]
    public TextMeshProUGUI GameOverText;        // 「捕まえた―」を出すUI
    public string GameOverMessage = "捕まえた―";
    public float TextCharInterval = 0.05f;      // 1文字ごとに出す間隔

    // === 内部: カメラ揺れ管理 ===
    private float _shakeTimeLeft = 0f;
    private float _shakeTotalDuration = 0f;
    private float _currentShakeAmplitude = 0f;

    // === 内部: プレイヤー非表示キャッシュ ===
    private Renderer[] _cachedPlayerRenderers;
    private bool _playerHidden = false;

    // === 内部: テキスト演出 ===
    private static bool _textStarted = false;   // 同じシーン中で二重再生しないように
    private Coroutine _typingCo = null;

    void Awake()
    {
        // ----- 参照の保険と初期化 -----

        // Player自動取得
        if (!Player)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                Player = p.transform;
            }
            else
            {
                PlayerController pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    Player = pc.transform;
                }
            }
        }

        // Ghost自動取得（とりあえず1体）
        if (!Ghost)
        {
            GameObject g = GameObject.FindGameObjectWithTag("Ghost");
            if (g != null)
            {
                Ghost = g.transform;
            }
            else
            {
                SearchChase anyChase = UnityEngine.Object.FindFirstObjectByType<SearchChase>();
                if (anyChase != null)
                {
                    Ghost = anyChase.transform;
                }
            }
        }

        // HideCroset
        if (!HideRef)
        {
            HideRef = UnityEngine.Object.FindFirstObjectByType<HideCroset>();
        }

        // GhostChase
        if (!GhostChase && Ghost)
        {
            GhostChase = Ghost.GetComponent<SearchChase>();
            if (!GhostChase)
            {
                GhostChase = UnityEngine.Object.FindFirstObjectByType<SearchChase>();
            }
        }

        // Volume系
        if (!PostVolume)
        {
            PostVolume = UnityEngine.Object.FindFirstObjectByType<Volume>();
        }
        if (PostVolume && PostVolume.profile)
        {
            _hasVig = PostVolume.profile.TryGet(out _vig);
            _hasCA = PostVolume.profile.TryGet(out _ca);

            if (_hasVig)
            {
                _vig.active = true;
                _vig.color.Override(EdgeColor);
                _vig.smoothness.Override(0.9f);
                _vig.intensity.Override(0f); // 初期は0
            }

            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.active = true;
                _ca.intensity.Override(0f);
            }
        }

        // キルカメラOFFスタート
        if (KillCamera && KillCamera.enabled)
        {
            KillCamera.enabled = false;
        }

        // Animator の GameOverフラグ 初期化
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
            GhostAnimator.SetBool(GameOverBoolName, false);
        }

        // フェードの初期化
        if (FadeCanvasGroup)
        {
            FadeCanvasGroup.alpha = 0f;
        }

        // プレイヤーRendererまとめてキャッシュ
        if (Player)
        {
            _cachedPlayerRenderers = Player.GetComponentsInChildren<Renderer>(true);
        }

        // テキスト初期状態は非表示で中身空
        if (GameOverText)
        {
            GameOverText.text = "";
            GameOverText.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        StartCoroutine(DistanceWatchLoop());
    }

    void Update()
    {
        // 毎フレーム：幽霊との距離からビネット濃度をじわっと更新
        UpdateDangerVignette();
    }

    // 一定間隔で「つかまった？」を監視
    IEnumerator DistanceWatchLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(CheckInterval);

        while (enabled && !_gameOverFired)
        {
            Transform nearGhost = GetNearestGhostToPlayer();

            if (Player && nearGhost != null)
            {
                float dist = Vector3.Distance(Player.position, nearGhost.position);

                // 「隠れてるし、幽霊がState1（通常探索）ならセーフ」な状況ならスキップ
                if (ShouldSkipCatch())
                {
                    yield return wait;
                    continue;
                }

                if (dist <= TriggerDistance)
                {
                    FireGameOver();
                    yield break;
                }
            }

            yield return wait;
        }
    }

    // 距離に応じた警告ビネット更新
    private void UpdateDangerVignette()
    {
        if (!_hasVig || !Player) return;

        Transform nearGhost = GetNearestGhostToPlayer();
        if (!nearGhost) return;

        // 隠れてて安全ならフェードアウト方向
        if (ShouldSkipCatch())
        {
            _currIntensity = Mathf.MoveTowards(_currIntensity, 0f, FadeSpeed * Time.deltaTime);
            _vig.intensity.Override(_currIntensity);

            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.intensity.Override(_currIntensity * 0.55f);
            }
            return;
        }

        // プレイヤーと幽霊の距離が近いほど濃い
        float dist = Vector3.Distance(Player.position, nearGhost.position);

        float t = 0f;
        if (dist <= WarnDistance)
        {
            float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
            t = Mathf.Clamp01((WarnDistance - dist) / span); // 0→1
        }

        float target = t * MaxVignette;

        _currIntensity = Mathf.MoveTowards(_currIntensity, target, FadeSpeed * Time.deltaTime);

        _vig.color.Override(EdgeColor);
        _vig.intensity.Override(_currIntensity);

        if (_hasCA && AlsoShakeChromatic)
        {
            _ca.intensity.Override(_currIntensity * 0.55f);
        }
    }

    // ★ここで実際に「捕まった！」の処理
    private void FireGameOver()
    {
        if (_gameOverFired) return;
        _gameOverFired = true;

        // いちばん近い幽霊を取り直して、そのAnimatorを優先して使う
        Animator nearestAnim = GetNearestGhostAnimator();
        if (nearestAnim != null)
        {
            GhostAnimator = nearestAnim;
        }

        // SE
        if (AudioMgr != null)
        {
            AudioMgr.CatchSource();
        }
        else
        {
            Debug.LogWarning("[GameOver] AudioMgr がありません（捕まったSEなし）");
        }

        // プレイヤーの見た目を消す（手とか体とか）
        HidePlayerVisualsIfNeeded();

        // 幽霊のアニメーターに「GameOverフラグON」
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
            // まずタイムスケール0でも動くように UnscaledTime 更新に変えておく
            GhostAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

            GhostAnimator.SetBool(GameOverBoolName, true);

            bool val = GhostAnimator.GetBool(GameOverBoolName);
            Debug.Log("[GameOver] " + GameOverBoolName + " を true にセット。現在値=" + val);

            LogAnimatorState("[GameOver] After SetBool");
        }
        else
        {
            Debug.LogWarning("[GameOver] GhostAnimator か GameOverBoolName が未設定です。");
        }

        // イベント本編
        StartCoroutine(GameOverSequence());
    }

    // ゲームオーバー演出のフロー本体
    private IEnumerator GameOverSequence()
    {
        // NavMeshAgentを止める（暴れ防止）
        if (StopAgentsOnGameOver)
        {
            NavMeshAgent a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            NavMeshAgent a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;
            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        // ちょいタメ
        if (CameraCutDelay > 0f)
        {
            yield return new WaitForSeconds(CameraCutDelay);
        }

        // メインカメラOFF・キルカメラON
        if (MainCamera && MainCamera.enabled)
        {
            MainCamera.enabled = false;
        }
        if (KillCamera)
        {
            KillCamera.nearClipPlane = KillCamNearClip;
            KillCamera.enabled = true;
        }

        // アニメ再生待ち + 余韻ホールド + テキスト出し
        yield return StartCoroutine(WaitForGameOverAnim());

        // 暗転フェード
        yield return StartCoroutine(FadeOutScreen());

        // シーン遷移
        if (!string.IsNullOrEmpty(GameoverScene))
        {
            SceneManager.LoadScene(GameoverScene);
        }
        else
        {
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }

    // アニメの待ち
    private IEnumerator WaitForGameOverAnim()
    {
        float timer = 0f;
        bool enteredTaggedState = false;

        // 1) GameOverタグ付きステートに入るまで待つ
        while (true)
        {
            UpdateKillCameraFollow();
            LogAnimatorState("[GameOverAnimCheck] waiting ENTER");

            if (GhostAnimator != null)
            {
                AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

                if (st.IsTag(GameOverAnimTag))
                {
                    enteredTaggedState = true;

                    // 掴まれた瞬間の短い強い揺れ
                    if (EnableCameraShake)
                    {
                        StartShake(ShakeDuration, ShakeAmplitude);
                    }

                    break;
                }
            }

            timer += Time.deltaTime;
            if (timer >= FallbackDelay)
            {
                Debug.LogWarning("[GameOverAnimCheck] タグステートに入らずFallback");
                break;
            }

            yield return null;
        }

        // 2) タグ付きステートが1周終わるか / 抜けるまで待つ
        if (enteredTaggedState)
        {
            while (true)
            {
                UpdateKillCameraFollow();
                LogAnimatorState("[GameOverAnimCheck] waiting FINISH");

                if (GhostAnimator != null)
                {
                    AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

                    // もうタグじゃなくなった = 遷移した → 終了扱い
                    if (!st.IsTag(GameOverAnimTag))
                    {
                        Debug.Log("[GameOverAnimCheck] タグ外れたのでFINISH扱い");
                        break;
                    }

                    // normalizedTime>=1.0 → 1周再生完了（ループしない前提）
                    if (st.normalizedTime >= 1.0f)
                    {
                        Debug.Log("[GameOverAnimCheck] normalizedTime>=1.0 -> FINISH");
                        break;
                    }
                }

                yield return null;
            }
        }

        // 3) 余韻ホールド
        if (EnableCameraShake)
        {
            StartShake(HoldAfterAnimSeconds, HoldShakeAmplitude);
        }

        // ホールド開始時にテキスト開始（1文字ずつ）
        StartTypewriterText();

        float hold = 0f;
        while (hold < HoldAfterAnimSeconds)
        {
            UpdateKillCameraFollow();
            hold += Time.deltaTime;
            yield return null;
        }
    }

    // テキストのタイプ出しを開始（シーン中で一度きり）
    private void StartTypewriterText()
    {
        if (_textStarted)
        {
            Debug.Log("[GameOverText] すでにタイプ開始済みなのでスキップ");
            return;
        }

        _textStarted = true;
        Debug.Log("[GameOverText] タイプライター開始");

        if (GameOverText == null)
        {
            Debug.LogWarning("[GameOverText] GameOverText が未設定");
            return;
        }

        GameOverText.gameObject.SetActive(true);
        GameOverText.text = "";

        if (_typingCo != null)
        {
            StopCoroutine(_typingCo);
        }

        _typingCo = StartCoroutine(TypewriterCo());
    }

    // 実際に1文字ずつ増やしていく
    private IEnumerator TypewriterCo()
    {
        if (GameOverText == null) yield break;

        string msg = GameOverMessage;
        GameOverText.text = "";

        for (int i = 0; i < msg.Length; i++)
        {
            GameOverText.text += msg[i];

            if (TextCharInterval > 0f)
                yield return new WaitForSeconds(TextCharInterval);
            else
                yield return null;
        }
    }

    // カメラ揺れ開始
    private void StartShake(float duration, float amplitude)
    {
        if (!EnableCameraShake)
        {
            _shakeTimeLeft = 0f;
            _shakeTotalDuration = 0f;
            _currentShakeAmplitude = 0f;
            return;
        }

        _shakeTimeLeft = duration;
        _shakeTotalDuration = duration;
        _currentShakeAmplitude = amplitude;
    }

    // 黒フェード
    private IEnumerator FadeOutScreen()
    {
        if (!FadeCanvasGroup)
        {
            yield break;
        }

        float t = 0f;
        while (t < FadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / FadeDuration);
            FadeCanvasGroup.alpha = a;
            yield return null;
        }

        FadeCanvasGroup.alpha = 1f;
    }

    // Animatorの現在ステート/タグをログ
    private void LogAnimatorState(string header)
    {
        if (!GhostAnimator)
        {
            Debug.LogWarning(header + " Animatorがありません");
            return;
        }

        AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

        bool inTagged = st.IsTag(GameOverAnimTag);
        float norm = st.normalizedTime;
        int shortHash = st.shortNameHash;

        bool currentBool = false;
        if (!string.IsNullOrEmpty(GameOverBoolName))
        {
            currentBool = GhostAnimator.GetBool(GameOverBoolName);
        }

        Debug.Log(
            header +
            " layer=" + GhostAnimLayer +   // ★修正: GameOverAnimLayer -> GhostAnimLayer
            " bool(" + GameOverBoolName + ")=" + currentBool +
            " stateHash=" + shortHash +
            " tagMatch=" + inTagged +
            " tagWanted=" + GameOverAnimTag +
            " normTime=" + norm.ToString("0.00")
        );
    }

    // キルカメラを「今いちばん近い幽霊」に追従させる＆揺らす
    private void UpdateKillCameraFollow()
    {
        if (!KillCamera) return;

        Transform tgtGhost = GetNearestGhostToPlayer();
        if (!tgtGhost)
        {
            Debug.LogWarning("[GameOverCam] tgtGhost=null (近い幽霊が見つからない)");
            return;
        }

        // ベース位置
        Vector3 camPos = tgtGhost.position;
        camPos += tgtGhost.forward * CamDistFromGhost;
        camPos += tgtGhost.right * CamSideOffset;
        camPos.y = tgtGhost.position.y + CamHeightOffset;

        // 揺れ：_shakeTimeLeft > 0 の間は徐々に小さくしながらランダムオフセット
        if (EnableCameraShake && _shakeTimeLeft > 0f && _shakeTotalDuration > 0f)
        {
            float shakeT = _shakeTimeLeft / _shakeTotalDuration; // 1→0
            float amp = _currentShakeAmplitude * shakeT;         // 時間とともに小さく

            Vector3 randomOffset = new Vector3(
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp * 0.5f
            );

            camPos += randomOffset;

            _shakeTimeLeft -= Time.deltaTime;
        }

        KillCamera.transform.position = camPos;

        // 幽霊を見る
        KillCamera.transform.LookAt(tgtGhost.position);

        // ピッチだけオフセット
        if (CamLookPitchOffsetDeg != 0f)
        {
            KillCamera.transform.rotation =
                KillCamera.transform.rotation * Quaternion.Euler(CamLookPitchOffsetDeg, 0f, 0f);
        }

        // デバッグ用にだしてる
        float realDist = Vector3.Distance(KillCamera.transform.position, tgtGhost.position);
        Vector3 vp = KillCamera.WorldToViewportPoint(tgtGhost.position);
        bool isVisible =
            (vp.z > 0f) &&
            (vp.x >= 0f && vp.x <= 1f) &&
            (vp.y >= 0f && vp.y <= 1f);

        Debug.Log(
            "[GameOverCam] ghost=" + tgtGhost.name +
            " dist=" + realDist.ToString("0.000") +
            " vp=(" + vp.x.ToString("0.000") + "," + vp.y.ToString("0.000") + ",z=" + vp.z.ToString("0.000") + ")" +
            " visible=" + isVisible
        );

        Debug.DrawLine(
            KillCamera.transform.position,
            tgtGhost.position,
            Color.yellow,
            0f,
            false
        );
    }

    // プレイヤーに一番近い幽霊(複数湧いたもの含む)を探す
    private Transform GetNearestGhostToPlayer()
    {
        if (Player == null) return null;

        Transform nearest = null;
        float bestDist = float.PositiveInfinity;

        // タグ "Ghost" を持つ候補
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Ghost");
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject go = tagged[i];
            if (!go) continue;

            float d = Vector3.Distance(Player.position, go.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = go.transform;
            }
        }

        // SearchChase持ち（タグがない生成ゴーストも拾う）
        SearchChase[] chasers = UnityEngine.Object.FindObjectsByType<SearchChase>(FindObjectsSortMode.None);
        for (int i = 0; i < chasers.Length; i++)
        {
            SearchChase sc = chasers[i];
            if (!sc) continue;

            Transform t = sc.transform;
            float d = Vector3.Distance(Player.position, t.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = t;
            }
        }

        // それでもnullならFallbackで最初のGhost参照
        if (nearest == null)
        {
            nearest = Ghost;
        }

        return nearest;
    }

    // いちばん近い幽霊のAnimatorを取る（FireGameOverで使う）
    private Animator GetNearestGhostAnimator()
    {
        Transform t = GetNearestGhostToPlayer();
        if (!t) return null;

        // ド真ん中のオブジェクト側にAnimatorが無い場合もあるので
        // 子から探す
        Animator a = t.GetComponentInChildren<Animator>();
        return a;
    }

    // クローゼットに隠れてる＋幽霊がState1なら、キャッチ無効
    private bool ShouldSkipCatch()
    {
        if (!HideRef || !HideRef.hide) return false;

        int ghostState = (GhostChase ? GhostChase.GetState() : 1);
        return ghostState == 1;
    }

    private void OnDrawGizmosSelected()
    {
        Transform g = GetNearestGhostToPlayer();
        if (Player && g)
        {
            // 判定距離の目安
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(g.position, Mathf.Max(TriggerDistance, 0.01f));

            // Player-ghost のライン
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, g.position);

            // シーンビューでキルカメラ予定位置も見えるように
            Vector3 debugCamPos = g.position
                                + g.forward * CamDistFromGhost
                                + g.right * CamSideOffset;
            debugCamPos.y = g.position.y + CamHeightOffset;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(debugCamPos, 0.07f);
        }
    }

    // プレイヤーを消す（Renderer.enabled=false）
    private void HidePlayerVisualsIfNeeded()
    {
        if (!HidePlayerOnGameOver) return;
        if (_playerHidden) return;
        if (Player == null) return;

        if (_cachedPlayerRenderers == null || _cachedPlayerRenderers.Length == 0)
        {
            _cachedPlayerRenderers = Player.GetComponentsInChildren<Renderer>(true);
        }

        if (_cachedPlayerRenderers != null)
        {
            for (int i = 0; i < _cachedPlayerRenderers.Length; i++)
            {
                Renderer r = _cachedPlayerRenderers[i];
                if (!r) continue;
                r.enabled = false;
            }
        }

        _playerHidden = true;
        Debug.Log("[GameOver] プレイヤー見た目を非表示にしました。");
    }

    // === 追加: 現在の「近接警告ビネット」ブレンド量(0..1)を外部へ渡す ===
    public float GetDangerBlend01()
    {
        if (!_hasVig || MaxVignette <= 0f) return 0f;
        return Mathf.Clamp01(_currIntensity / MaxVignette);
    }
}
