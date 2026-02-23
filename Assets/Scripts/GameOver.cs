using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering;                 // URP Volume
using UnityEngine.Rendering.Universal;       // URP Vignette
using TMPro;                                 // TMPテキスト表示用
using CriWare;                               // CRI Atom 用（環境によっては不要なら外してOK）

public class GameOver : MonoBehaviour
{
    // このスクリプトの役割
    // 1) プレイヤーと最寄りの幽霊の距離を一定間隔で監視して、捕獲距離ならゲームオーバー発火
    // 2) 危険距離ではビネットを濃くして緊張感を出す（timeScale=0でも動くようにunscaledを使う）
    // 3) ピンチSE(3D/2D)を距離に応じてON/OFFや音量制御
    // 4) 捕獲時に演出カメラへ切り替え、幽霊アニメ待ち → フェード → シーン遷移

    public Transform Player;                    // プレイヤー
    public Transform Ghost;                     // 幽霊（初期参照用 / フォールバック）

    [Header("隠れ判定")]
    public HideCroset HideRef;                  // クローゼット隠れ制御（hide=trueなら隠れ中扱い）
    public SearchChase GhostChase;              // 幽霊の状態を見る用（GetStateで追跡状態などを判定）

    [Header("判定設定")]
    public float TriggerDistance = 0.735f;      // これ以下なら捕まる（捕獲距離）
    public float CheckInterval = 0.05f;         // 距離チェック間隔（秒）

    [Header("シーン遷移")]
    public string GameoverScene = "";           // 空なら現シーンをリロード
    public bool StopAgentsOnGameOver = true;    // 遷移前にNavMeshAgentを止める（演出中に動かないように）

    [Header("ポストプロセス（警告ビネット）")]
    public Volume PostVolume;                   // URP Volume（Vignetteなどが入っている）
    public Color EdgeColor = Color.red;         // 周辺の色
    public float WarnDistance = 2.0f;           // この距離以内でビネットが濃くなる & ピンチSE範囲
    [Range(0f, 1f)] public float MaxVignette = 0.45f;
    [Range(0.1f, 20f)] public float FadeSpeed = 6f;  // ビネットの追従速度（MoveTowardsの速度）
    public bool AlsoShakeChromatic = false;     // 色収差も揺らしたいならON

    private bool _gameOverFired = false;        // 多重発火防止（捕獲処理を一回だけにする）

    private Vignette _vig;                      // Volumeから取ったVignette参照
    private ChromaticAberration _ca;            // Volumeから取ったChromaticAberration参照
    private bool _hasVig = false;               // Vignetteが取れたか
    private bool _hasCA = false;                // ChromaticAberrationが取れたか

    private float _currIntensity = 0f;          // 現在のビネット強度（0..MaxVignette）

    [Header("SE再生用")]
    public AudioManager AudioMgr;               // 捕まった時のSEを鳴らすための参照

    [Header("ピンチSE（3D・CRI Atom Loop BGM）")]
    public CriAtomSource PinchSource;           // ピンチ時に鳴らすループBGM用（3D想定）
    private bool _pinchPlaying = false;         // 再生中かどうか（多重Play/Stop防止）

    [Header("ピンチSE（2D・距離で音量変化）")]
    public CriAtomSource Pinch2DSource;         // 距離に応じて音量が0→1になる2D SE
    private bool _pinch2DPlaying = false;       // 再生中かどうか

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
    public string GameOverBoolName = "GameOver";// trueにすると捕まえアニメに入る想定
    public int GhostAnimLayer = 0;              // そのアニメがあるレイヤー(index)
    public string GameOverAnimTag = "GameOver"; // そのステートに付いてるTag名

    [Header("プレイヤー表示制御")]
    public bool HidePlayerOnGameOver = true;    // 捕まった瞬間にプレイヤーを非表示にするか

    [Header("ゲームオーバーテキスト表示(TMP)")]
    public TextMeshProUGUI GameOverText;        // 「捕まえた―」を出すUI
    public string GameOverMessage = "捕まえた―";
    public float TextCharInterval = 0.05f;      // 1文字ごとに出す間隔

    private float _shakeTimeLeft = 0f;          // 現在の揺れ残り時間
    private float _shakeTotalDuration = 0f;     // 揺れ全体時間（減衰計算用）
    private float _currentShakeAmplitude = 0f;  // 今回の揺れの最大振幅

    private Renderer[] _cachedPlayerRenderers;  // プレイヤーのRenderer群（現状はキャッシュのみ）
    private bool _playerHidden = false;         // すでに隠したか（多重実行防止）

    private static bool _textStarted = false;   // テキスト演出の多重起動防止（シーン跨ぎも考慮のstatic）
    private Coroutine _typingCo = null;         // タイプライター用コルーチン参照

    private Coroutine _watchCo = null;          // 距離監視コルーチン参照

    private void Awake()
    {
        Debug.Log("[GameOver] Awake 開始");

        //================
        // Player 自動取得
        //================
        // Inspector未設定ならタグや型検索でプレイヤーを探す
        // ここで取れないと距離判定ができないので、ログで気づけるようにする
        if (!Player)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                Player = p.transform;
                Debug.Log("[GameOver] Player をタグから取得");
            }
            else
            {
                PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    Player = pc.transform;
                    Debug.Log("[GameOver] Player を PlayerController から取得");
                }
                else
                {
                    Debug.LogWarning("[GameOver] Player が見つかりません");
                }
            }
        }

        //================
        // Ghost 自動取得
        //================
        // Inspector未設定ならタグやSearchChaseから幽霊を探す
        // 複数幽霊がいる場合、後で最寄りを選び直すのでここは初期値/保険
        if (!Ghost)
        {
            GameObject g = GameObject.FindGameObjectWithTag("Ghost");
            if (g != null)
            {
                Ghost = g.transform;
                Debug.Log("[GameOver] Ghost をタグから取得");
            }
            else
            {
                SearchChase anyChase = Object.FindFirstObjectByType<SearchChase>();
                if (anyChase != null)
                {
                    Ghost = anyChase.transform;
                    Debug.Log("[GameOver] Ghost を SearchChase から取得");
                }
                else
                {
                    Debug.LogWarning("[GameOver] Ghost が見つかりません");
                }
            }
        }

        //================
        // HideCroset 参照
        //================
        // クローゼット隠れ判定に必要（hide=trueなら捕獲無効にする）
        if (!HideRef)
        {
            HideRef = Object.FindFirstObjectByType<HideCroset>();
            if (HideRef == null) Debug.LogWarning("[GameOver] HideCroset がシーンに見つかりません");
        }

        //================
        // GhostChase 参照
        //================
        // 幽霊の状態参照（state==1など）に必要
        // GhostにSearchChaseが無ければシーンから探す
        if (!GhostChase && Ghost)
        {
            GhostChase = Ghost.GetComponent<SearchChase>();
            if (!GhostChase) GhostChase = Object.FindFirstObjectByType<SearchChase>();
        }

        //================
        // Volume 初期化
        //================
        // URP VolumeからVignette/ChromaticAberrationを取り出して初期値を設定する
        // Vignetteが無いプロファイルでも落ちないようTryGetで判定する
        if (!PostVolume) PostVolume = Object.FindFirstObjectByType<Volume>();

        if (PostVolume && PostVolume.profile)
        {
            _hasVig = PostVolume.profile.TryGet(out _vig);
            _hasCA = PostVolume.profile.TryGet(out _ca);

            Debug.Log($"[GameOver] Volume 初期化 hasVig={_hasVig} hasCA={_hasCA}");

            if (_hasVig)
            {
                _vig.active = true;
                _vig.color.Override(EdgeColor);
                _vig.smoothness.Override(0.9f);
                _vig.intensity.Override(0f);   // 開始時は警告なし
            }

            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.active = true;
                _ca.intensity.Override(0f);    // 開始時は色収差なし
            }
        }

        //================
        // キルカメラ OFF スタート
        //================
        // 演出が始まるまでKillCameraは無効（MainCameraで通常プレイ）
        if (KillCamera && KillCamera.enabled) KillCamera.enabled = false;

        //================
        // Animator GameOver フラグ初期化
        //================
        // シーン開始直後にGameOverがtrueだと演出が始まってしまうので、必ずfalseに戻す
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
            GhostAnimator.SetBool(GameOverBoolName, false);
        }

        //================
        // フェード初期化
        //================
        // フェード用CanvasGroupは透明開始
        if (FadeCanvasGroup) FadeCanvasGroup.alpha = 0f;

        //================
        // プレイヤーRenderer キャッシュ
        //================
        // いまは使用していないが、表示制御や点滅などに使う場合のためにキャッシュしておく
        if (Player) _cachedPlayerRenderers = Player.GetComponentsInChildren<Renderer>(true);

        //================
        // テキスト初期化
        //================
        // ゲームオーバーテキストは非表示で開始（演出開始時にタイプライターで表示）
        if (GameOverText)
        {
            GameOverText.text = "";
            GameOverText.gameObject.SetActive(false);
        }

        //================
        // サウンド設定ログ
        //================
        // 設定漏れをログで気づけるようにしておく
        if (PinchSource != null) Debug.Log("[GameOver] PinchSource(3D) 設定あり obj=" + PinchSource.gameObject.name);
        else Debug.LogWarning("[GameOver] PinchSource(3D) が未設定です");

        if (Pinch2DSource != null) Debug.Log("[GameOver] Pinch2DSource(2D) 設定あり obj=" + Pinch2DSource.gameObject.name);
        else Debug.LogWarning("[GameOver] Pinch2DSource(2D) が未設定です");

        Debug.Log("[GameOver] Awake 完了");
    }

    private void OnEnable()
    {
        //================
        // 監視コルーチンの多重起動防止
        //================
        // Enable/Disableが繰り返されても監視が二重にならないようにする
        if (_watchCo != null) StopCoroutine(_watchCo);
        _watchCo = StartCoroutine(DistanceWatchLoop());
    }

    private void OnDisable()
    {
        // 有効状態が終わったら監視を止める
        if (_watchCo != null) StopCoroutine(_watchCo);
        _watchCo = null;
    }

    private void Update()
    {
        // デバッグ：Pキーで2DピンチSEをテスト再生
        // 本番では不要なら消してOKだが、今は動作確認用に残してある
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (Pinch2DSource != null)
            {
                Debug.Log("[GameOver] [TEST] Pキーで Pinch2DSource.Play()");
                Pinch2DSource.volume = 1.0f;
                Pinch2DSource.Play();
                _pinch2DPlaying = true;
            }
            else
            {
                Debug.LogWarning("[GameOver] [TEST] Pinch2DSource が未設定のため再生できません");
            }
        }

        // ゲームオーバー後は演出更新を止める
        // ここで必ずピンチSEを停止し続けることで、残響や再生し直しを防ぐ
        if (_gameOverFired)
        {
            if (PinchSource != null && _pinchPlaying)
            {
                Debug.Log("[GameOver] _gameOverFired 中なので PinchSource(3D) STOP");
                PinchSource.Stop();
                _pinchPlaying = false;
            }

            if (Pinch2DSource != null)
            {
                if (_pinch2DPlaying)
                {
                    Debug.Log("[GameOver] _gameOverFired 中なので Pinch2DSource(2D) STOP");
                    Pinch2DSource.Stop();
                    _pinch2DPlaying = false;
                }
                Pinch2DSource.volume = 0f;
            }

            return;
        }

        // 通常時は危険演出（ビネットとピンチSE）を更新
        UpdateDangerVignette();
    }

    //================
    // 一定間隔で「つかまった？」を監視（timeScale=0でも動く）
    //================
    private IEnumerator DistanceWatchLoop()
    {
        // WaitForSecondsRealtimeを使うことで timeScale=0 でも距離監視が止まらない
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(CheckInterval);

        // enabledかつゲームオーバー未発火の間、ループで監視する
        while (enabled && !_gameOverFired)
        {
            // 現在の最寄り幽霊を取得（複数幽霊がいても最短距離を使う）
            Transform nearGhost = GetNearestGhostToPlayer();

            if (Player && nearGhost != null)
            {
                // 最寄りが変わり得るので、参照を更新しておく（後続の判定や演出で使う）
                Ghost = nearGhost;
                GhostChase = nearGhost.GetComponent<SearchChase>();

                // 捕獲判定に使う距離
                float dist = Vector3.Distance(Player.position, nearGhost.position);

                // 状態のログ用（判定の理由が追えるように）
                bool hidden = (HideRef && HideRef.hide);
                int state = (GhostChase ? GhostChase.GetState() : 1);

                // 隠れ中など「捕獲をスキップ」する状態か
                bool skip = ShouldSkipCatch();

                // 距離条件とスキップ条件から、今回発火するか
                bool willFire = (!skip && dist <= TriggerDistance);

                Debug.Log($"[CatchCheck] ghost={nearGhost.name} dist={dist:F3} trigger={TriggerDistance:F3} hidden={hidden} state={state} skip={skip} fire={willFire}");

                if (willFire)
                {
                    // ここで一度だけゲームオーバー演出へ移行する
                    Debug.Log("[CatchCheck] 発火条件成立 → FireGameOver()");
                    FireGameOver();
                    yield break;
                }
            }

            yield return wait;
        }
    }

    //================
    // 距離に応じた警告ビネット更新 + ピンチSE制御（timeScale=0でも動く）
    //================
    private void UpdateDangerVignette()
    {
        // Vignetteが無い/Playerが無いなら何もしない
        if (!_hasVig || !Player) return;

        // 最寄り幽霊が取れないなら何もしない
        Transform nearGhost = GetNearestGhostToPlayer();
        if (!nearGhost) return;

        float dist = Vector3.Distance(Player.position, nearGhost.position);

        // 隠れていて安全扱いなら、ビネットは消える方向（0へ）
        bool isSafe = ShouldSkipCatch();

        if (isSafe)
        {
            _currIntensity = Mathf.MoveTowards(_currIntensity, 0f, FadeSpeed * Time.unscaledDeltaTime);
            _vig.intensity.Override(_currIntensity);

            // 色収差も連動させる場合はビネット強度から作る
            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.intensity.Override(_currIntensity * 0.55f);
            }

            // 安全扱いでも距離は渡す（内部で安全なら停止する）
            UpdatePinchSEByDistance(dist, true);
            return;
        }

        // 危険度tの計算
        // distがWarnDistanceより近いほど tが1に近づく
        // TriggerDistanceに近いほど最大に寄せる
        float t = 0f;
        if (dist <= WarnDistance)
        {
            float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
            t = Mathf.Clamp01((WarnDistance - dist) / span);
        }

        float target = t * MaxVignette;

        // 急にパッと変えずにMoveTowardsで滑らかに追従させる
        _currIntensity = Mathf.MoveTowards(_currIntensity, target, FadeSpeed * Time.unscaledDeltaTime);

        _vig.color.Override(EdgeColor);
        _vig.intensity.Override(_currIntensity);

        if (_hasCA && AlsoShakeChromatic)
        {
            _ca.intensity.Override(_currIntensity * 0.55f);
        }

        UpdatePinchSEByDistance(dist, false);
    }

    //================
    // 「捕まった！」処理
    //================
    private void FireGameOver()
    {
        // 二重発火防止
        if (_gameOverFired) return;
        _gameOverFired = true;

        Debug.Log("[GameOver] FireGameOver 実行");

        //================
        // ピンチSE停止（3D/2D）
        //================
        // 捕獲が確定したらピンチ系は必ず止める（演出SEと被らないように）
        if (PinchSource != null && _pinchPlaying)
        {
            Debug.Log("[GameOver] PinchSource(3D) STOP");
            PinchSource.Stop();
            _pinchPlaying = false;
        }

        if (Pinch2DSource != null)
        {
            if (_pinch2DPlaying)
            {
                Debug.Log("[GameOver] Pinch2DSource(2D) STOP");
                Pinch2DSource.Stop();
                _pinch2DPlaying = false;
            }
            Pinch2DSource.volume = 0f;
        }

        //================
        // 近い幽霊へ参照更新
        //================
        // 捕獲演出で使う幽霊を「最寄り」に合わせる
        Transform nearGhost = GetNearestGhostToPlayer();
        if (nearGhost != null)
        {
            Ghost = nearGhost;
            GhostChase = nearGhost.GetComponent<SearchChase>();
        }

        // 捕獲アニメを再生するAnimatorも最寄りから取り直す
        Animator nearestAnim = GetNearestGhostAnimator();
        if (nearestAnim != null) GhostAnimator = nearestAnim;

        //================
        // 捕まったSE
        //================
        // 外部AudioManagerに依存するので例外が出ても演出を止めないようtry/catch
        try
        {
            if (AudioMgr != null) AudioMgr.CatchSource();
            else Debug.LogWarning("[GameOver] AudioMgr がありません（捕まったSEなし）");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameOver] SE再生中に例外：{ex.Message}\n{ex.StackTrace}\n→ SEをスキップして続行します。");
        }

        //================
        // プレイヤー表示制御
        //================
        // 捕獲演出でプレイヤーが邪魔ならここで消す（子オブジェクトを非表示）
        HidePlayerVisualsIfNeeded();

        //================
        // 幽霊アニメ開始
        //================
        // GameOverBoolName=trueで捕獲アニメへ遷移する想定
        // updateModeをUnscaledTimeにして、timeScaleが止まってもアニメが進むようにする
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
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

        //================
        // 演出シーケンス開始
        //================
        Debug.Log("[GameOver] StartCoroutine(GameOverSequence)");
        StartCoroutine(GameOverSequence());
    }

    //================
    // ゲームオーバー演出フロー（timeScale=0でも動く）
    //================
    private IEnumerator GameOverSequence()
    {
        // 必要ならNavMeshAgentを停止（演出中に移動してしまうのを防ぐ）
        if (StopAgentsOnGameOver)
        {
            NavMeshAgent a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            NavMeshAgent a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;

            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        // 捕獲直後に少し間を置いてからカメラ切替（タメ）
        if (CameraCutDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(CameraCutDelay);
        }

        // 通常カメラOFF
        if (MainCamera && MainCamera.enabled) MainCamera.enabled = false;

        // 演出カメラON（NearClipを詰めて近距離の欠けを減らす）
        if (KillCamera)
        {
            KillCamera.nearClipPlane = KillCamNearClip;
            KillCamera.enabled = true;
        }

        Debug.Log("[GameOver] Camera switched");

        // 幽霊のGameOverアニメが終わるのを待つ（またはFallback時間で抜ける）
        yield return StartCoroutine(WaitForGameOverAnim());

        // 画面フェードアウト
        yield return StartCoroutine(FadeOutScreen());

        Debug.Log("[GameOver] LoadScene now");

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

    //================
    // アニメ待ち（timeScale=0でも動く）
    //================
    private IEnumerator WaitForGameOverAnim()
    {
        float timer = 0f;
        bool enteredTaggedState = false;

        // まず「GameOverタグのステートに入る」まで待つ
        // 入らない場合はFallbackDelayで抜ける（遷移失敗の保険）
        while (true)
        {
            // 演出カメラは毎フレーム幽霊に追従させる
            UpdateKillCameraFollow();

            // デバッグ用に現在状態をログで追えるようにしている
            LogAnimatorState("[GameOverAnimCheck] waiting ENTER");

            if (GhostAnimator != null)
            {
                AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

                // タグ一致したら「アニメ開始扱い」
                if (st.IsTag(GameOverAnimTag))
                {
                    enteredTaggedState = true;

                    // 掴まれた瞬間の強い揺れを開始
                    if (EnableCameraShake)
                    {
                        StartShake(ShakeDuration, ShakeAmplitude);
                    }

                    break;
                }
            }

            timer += Time.unscaledDeltaTime;
            if (timer >= FallbackDelay)
            {
                Debug.LogWarning("[GameOverAnimCheck] タグステートに入らずFallback");
                break;
            }

            yield return null;
        }

        // タグに入れた場合は「終わる」まで待つ
        // 終わり判定は2通り
        // 1) タグが外れた
        // 2) normalizedTimeが1.0以上（1ループ分再生完了）
        if (enteredTaggedState)
        {
            while (true)
            {
                UpdateKillCameraFollow();
                LogAnimatorState("[GameOverAnimCheck] waiting FINISH");

                if (GhostAnimator != null)
                {
                    AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

                    if (!st.IsTag(GameOverAnimTag))
                    {
                        Debug.Log("[GameOverAnimCheck] タグ外れたのでFINISH扱い");
                        break;
                    }

                    if (st.normalizedTime >= 1.0f)
                    {
                        Debug.Log("[GameOverAnimCheck] normalizedTime>=1.0 -> FINISH");
                        break;
                    }
                }

                yield return null;
            }
        }

        // アニメ後の余韻用の揺れに切り替える（長め・弱め）
        if (EnableCameraShake)
        {
            StartShake(HoldAfterAnimSeconds, HoldShakeAmplitude);
        }

        // テキスト演出開始（タイプライター）
        StartTypewriterText();

        // 余韻時間の間、カメラ追従を続ける
        float hold = 0f;
        while (hold < HoldAfterAnimSeconds)
        {
            UpdateKillCameraFollow();
            hold += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    //================
    // テキスト演出開始
    //================
    private void StartTypewriterText()
    {
        // すでに開始している場合は二重にコルーチンを回さない
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

        // 表示ONにして、まず空文字から開始
        GameOverText.gameObject.SetActive(true);
        GameOverText.text = "";

        // 既存のコルーチンがあれば止めてから新規開始
        if (_typingCo != null) StopCoroutine(_typingCo);
        _typingCo = StartCoroutine(TypewriterCo());
    }

    private IEnumerator TypewriterCo()
    {
        if (GameOverText == null) yield break;

        // 1文字ずつ追加していく演出
        string msg = GameOverMessage;
        GameOverText.text = "";

        for (int i = 0; i < msg.Length; i++)
        {
            GameOverText.text += msg[i];

            // 文字間隔が0なら即時、0より大きければリアルタイム待ち
            if (TextCharInterval > 0f)
                yield return new WaitForSecondsRealtime(TextCharInterval);
            else
                yield return null;
        }
    }

    //================
    // 揺れ開始
    //================
    private void StartShake(float duration, float amplitude)
    {
        // 揺れ無効なら内部値をリセットして何もしない
        if (!EnableCameraShake)
        {
            _shakeTimeLeft = 0f;
            _shakeTotalDuration = 0f;
            _currentShakeAmplitude = 0f;
            return;
        }

        // UpdateKillCameraFollow内で shakeTimeLeft を減らしながらランダムオフセットを足す
        _shakeTimeLeft = duration;
        _shakeTotalDuration = duration;
        _currentShakeAmplitude = amplitude;
    }

    //================
    // フェードアウト（timeScale=0でも動く）
    //================
    private IEnumerator FadeOutScreen()
    {
        if (!FadeCanvasGroup) yield break;

        // unscaledDeltaTimeで進めるので timeScale=0 でもフェードする
        float t = 0f;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / FadeDuration);
            FadeCanvasGroup.alpha = a;
            yield return null;
        }

        FadeCanvasGroup.alpha = 1f;
    }

    private void LogAnimatorState(string header)
    {
        // Animator未設定ならログだけ出して戻る
        if (!GhostAnimator)
        {
            Debug.LogWarning(header + " Animatorがありません");
            return;
        }

        // 指定レイヤーの現在ステート情報を取得
        AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

        // タグ一致や再生進行状況を確認するための値
        bool inTagged = st.IsTag(GameOverAnimTag);
        float norm = st.normalizedTime;
        int shortHash = st.shortNameHash;

        // GameOverBoolが実際にtrueになっているか確認（遷移条件のデバッグ用）
        bool currentBool = false;
        if (!string.IsNullOrEmpty(GameOverBoolName))
        {
            currentBool = GhostAnimator.GetBool(GameOverBoolName);
        }

        Debug.Log(
            header +
            " layer=" + GhostAnimLayer +
            " bool(" + GameOverBoolName + ")=" + currentBool +
            " stateHash=" + shortHash +
            " tagMatch=" + inTagged +
            " tagWanted=" + GameOverAnimTag +
            " normTime=" + norm.ToString("0.00")
        );
    }

    private void UpdateKillCameraFollow()
    {
        // KillCamera未設定なら何もしない
        if (!KillCamera) return;

        // 演出対象の幽霊は「最寄り」を使う（途中で最寄りが変わるケースも想定）
        Transform tgtGhost = GetNearestGhostToPlayer();
        if (!tgtGhost)
        {
            Debug.LogWarning("[GameOverCam] tgtGhost=null (近い幽霊が見つからない)");
            return;
        }

        // 幽霊基準でカメラ位置を決める
        // forward方向に前へ、right方向に横へ、yは高さオフセット
        Vector3 camPos = tgtGhost.position;
        camPos += tgtGhost.forward * CamDistFromGhost;
        camPos += tgtGhost.right * CamSideOffset;
        camPos.y = tgtGhost.position.y + CamHeightOffset;

        // 揺れが有効なら、残り時間に応じて減衰したランダムオフセットを足す
        if (EnableCameraShake && _shakeTimeLeft > 0f && _shakeTotalDuration > 0f)
        {
            float shakeT = _shakeTimeLeft / _shakeTotalDuration;
            float amp = _currentShakeAmplitude * shakeT;

            Vector3 randomOffset = new Vector3(
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp * 0.5f
            );

            camPos += randomOffset;

            // 次フレームに向けて残り時間を減らす
            _shakeTimeLeft -= Time.unscaledDeltaTime;
        }

        KillCamera.transform.position = camPos;

        // カメラは常に幽霊を向く（演出で幽霊を画面に収めるため）
        KillCamera.transform.LookAt(tgtGhost.position);

        // 追加でピッチを足したい場合（顔アップや見下ろしなど）
        if (CamLookPitchOffsetDeg != 0f)
        {
            KillCamera.transform.rotation =
                KillCamera.transform.rotation * Quaternion.Euler(CamLookPitchOffsetDeg, 0f, 0f);
        }
    }

    private Transform GetNearestGhostToPlayer()
    {
        // Player未設定なら最寄り判定ができないのでnull
        if (Player == null) return null;

        // 最寄り幽霊を探す（タグGhostのオブジェクト群）
        Transform nearest = null;
        float bestDist = float.PositiveInfinity;

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

        // SearchChaseを持つ幽霊も候補に入れる（タグ設定漏れ対策）
        // FindObjectsSortMode.Noneで並び替えしない（速度優先）
        SearchChase[] chasers = Object.FindObjectsByType<SearchChase>(FindObjectsSortMode.None);
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

        // 何も見つからない場合の保険として、既存Ghost参照を返す
        if (nearest == null) nearest = Ghost;

        return nearest;
    }

    private Animator GetNearestGhostAnimator()
    {
        // 最寄り幽霊の子階層からAnimatorを探す（捕獲アニメ用）
        Transform t = GetNearestGhostToPlayer();
        if (!t) return null;

        Animator a = t.GetComponentInChildren<Animator>();
        return a;
    }

    private bool ShouldSkipCatch()
    {
        // 捕獲をスキップする条件
        // ここはゲーム仕様そのものなので、読みやすく意味が追えるように分解している
        bool hidden = (HideRef && HideRef.hide);
        int state = (GhostChase ? GhostChase.GetState() : 1);

        // state==1 かつ hidden のときだけ安全扱い（捕獲しない）
        return (state == 1 && hidden);
    }

    private void OnDrawGizmosSelected()
    {
        // エディタ上で捕獲距離やカメラ位置のイメージを見える化する
        Transform g = GetNearestGhostToPlayer();
        if (Player && g)
        {
            // 捕獲距離の可視化（幽霊中心の赤い球）
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(g.position, Mathf.Max(TriggerDistance, 0.01f));

            // プレイヤーと幽霊の線（距離感確認用）
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, g.position);

            // 演出カメラが置かれる想定位置（黄色球）
            Vector3 debugCamPos = g.position
                                + g.forward * CamDistFromGhost
                                + g.right * CamSideOffset;
            debugCamPos.y = g.position.y + CamHeightOffset;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(debugCamPos, 0.07f);
        }
    }

    private void HidePlayerVisualsIfNeeded()
    {
        // プレイヤーを隠さない設定なら何もしない
        if (!HidePlayerOnGameOver) return;

        // すでに隠しているなら二重でやらない
        if (_playerHidden) return;

        // Player参照が無いなら何もしない
        if (Player == null) return;

        // Player自身は残し、子オブジェクトを全て非表示にする
        // 見た目だけ消して、当たり判定などが必要ならPlayer本体を残す意図
        Transform[] allChildren = Player.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform t = allChildren[i];
            if (t == Player) continue;

            t.gameObject.SetActive(false);
        }

        _playerHidden = true;
        Debug.Log("[GameOver] Player の子オブジェクトを全て非表示にしました。");
    }

    private void UpdatePinchSEByDistance(float dist, bool isSafe)
    {
        // 安全状態のときはピンチSEを止めて、2Dは音量も0にする
        // ここで停止しておくと、隠れている間に緊張音が鳴り続けない
        if (isSafe)
        {
            if (PinchSource != null && _pinchPlaying)
            {
                PinchSource.Stop();
                _pinchPlaying = false;
            }

            if (Pinch2DSource != null)
            {
                if (_pinch2DPlaying)
                {
                    Pinch2DSource.Stop();
                    _pinch2DPlaying = false;
                }
                Pinch2DSource.volume = 0f;
            }
            return;
        }

        // WarnDistance以内ならピンチ範囲
        bool inPinchRange = (dist <= WarnDistance);

        // 3DピンチSEは距離範囲に入ったら再生、外れたら停止（ON/OFF）
        if (PinchSource != null)
        {
            if (!_pinchPlaying && inPinchRange)
            {
                PinchSource.Play();
                _pinchPlaying = true;
            }
            else if (_pinchPlaying && !inPinchRange)
            {
                PinchSource.Stop();
                _pinchPlaying = false;
            }
        }

        // 2DピンチSEは距離で音量を0..1に変化させる
        // distがTriggerDistanceに近いほど1に近づく
        if (Pinch2DSource != null)
        {
            float pinch01 = 0f;

            if (dist <= WarnDistance)
            {
                float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
                pinch01 = Mathf.Clamp01((WarnDistance - dist) / span);
            }

            // 音量が0より大きくなったら再生開始、0になったら停止
            if (!_pinch2DPlaying && pinch01 > 0f)
            {
                Pinch2DSource.Play();
                _pinch2DPlaying = true;
            }
            else if (_pinch2DPlaying && pinch01 <= 0f)
            {
                Pinch2DSource.Stop();
                _pinch2DPlaying = false;
            }

            // 再生中は音量反映、停止中は必ず0
            if (_pinch2DPlaying) Pinch2DSource.volume = pinch01;
            else Pinch2DSource.volume = 0f;
        }
    }

    public float GetDangerBlend01()
    {
        // 現在の危険度を0..1で返す
        // UI側でゲージ表示などに使える（ビネット強度をMaxで割ったもの）
        if (!_hasVig || MaxVignette <= 0f) return 0f;
        return Mathf.Clamp01(_currIntensity / MaxVignette);
    }
}