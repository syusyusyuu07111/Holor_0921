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
    public Transform Ghost;                     // 幽霊（追跡側 / デフォルト参照用）

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

    // ===== ゲームオーバー演出用 =====
    [Header("ゲームオーバー演出")]
    public Camera MainCamera;                   // 普段のプレイ用カメラ（演出開始時にOFFにする）
    public Camera KillCamera;                   // ゲームオーバー用の見せカメラ（幽霊を映す）

    [Header("キルカメラ設定")]
    public float KillCamNearClip = 0.01f;       // キルカメラ用NearClip（近距離でも透けないように）

    [Header("カメラ自動配置（幽霊基準）")]
    public float CamDistFromGhost = 0.4f;       // 幽霊の正面方向(=forward)にどれくらい前にカメラを置くか
    public float CamSideOffset = 0.0f;          // 幽霊の右方向(=right)にどれくらい横ずらすか
    public float CamHeightOffset = 0.0f;        // カメラの高さ = 幽霊の高さ + これ

    [Header("キルカメラ向きオフセット")]
    public float CamLookPitchOffsetDeg = 0f;    // カメラの上下向きオフセット（度数）。+で下向きなど

    public float CameraCutDelay = 0.05f;        // 捕まった直後にカメラを切り替えるまでのワンテンポ
    public float FallbackDelay = 2.0f;          // タグ付きステートに入らない/終わらない時の保険（秒）

    [Header("ゲームオーバー演出の余韻")]
    public float HoldAfterAnimSeconds = 3.0f;   // アニメ終わってから遷移前に見せる時間（秒）

    [Header("カメラ揺れ（掴まれた瞬間＆余韻）")]
    public bool EnableCameraShake = true;       // ★ 揺れを使うかどうか（チェックでON/OFF）
    public float ShakeDuration = 0.3f;          // 掴まれた直後の揺れ時間（秒）
    public float ShakeAmplitude = 0.02f;        // 掴まれた直後の揺れの大きさ（m）
    public float HoldShakeAmplitude = 0.02f;    // 余韻時間の揺れの大きさ（m）
    // Hold中は HoldAfterAnimSeconds 丸ごと揺らす

    [Header("フェードアウト")]
    public CanvasGroup FadeCanvasGroup;         // 画面全体を覆う黒いCanvasGroup（Alpha 0で開始）
    public float FadeDuration = 1.0f;           // 暗転にかける時間（秒）

    [Header("幽霊アニメーション")]
    public Animator GhostAnimator;              // 幽霊のAnimator（プレイヤーを掴むアニメ）
    public string GameOverBoolName = "GameOver";// Animator側のboolパラメータ名
    public int GhostAnimLayer = 0;              // そのアニメが流れるレイヤー番号（Base Layer=0など）
    public string GameOverAnimTag = "GameOver"; // ゲームオーバー用ステートのTag名

    [Header("プレイヤー表示制御")]
    public bool HidePlayerOnGameOver = true;    // 捕まった瞬間にプレイヤーの見た目を消すかどうか

    [Header("ゲームオーバーテキスト表示(TMP)")]
    public TextMeshProUGUI GameOverText;        // 画面に出すテキスト（Canvas上のTMP）
    public string GameOverMessage = "捕まえた―"; // 出したいメッセージ
    public float TextCharInterval = 0.05f;      // 1文字ずつ表示する間隔（秒）

    // 内部：揺れ管理
    private float _shakeTimeLeft = 0f;          // 残り揺れ時間
    private float _shakeTotalDuration = 0f;     // この揺れセットの合計時間
    private float _currentShakeAmplitude = 0f;  // 今回の揺れの最大振幅

    // 内部：プレイヤー非表示用キャッシュ
    private Renderer[] _cachedPlayerRenderers;  // プレイヤーのRendererまとめ（SkinnedMesh含む）
    private bool _playerHidden = false;         // 一度オフにしたかどうか

    // 内部：テキスト演出
    private static bool _textStarted = false;   // ★ static: シーン全体で1回だけタイプ開始する
    private Coroutine _typingCo = null;         // ★ 実行中のタイプコルーチンを保持

    void Awake()
    {
        // ----- 参照の自動補完 -----

        // Player
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

        // Ghost（とりあえず1体）
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

        // Volume
        if (!PostVolume)
        {
            PostVolume = UnityEngine.Object.FindFirstObjectByType<Volume>();
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

        // KillCamera は開始時OFF
        if (KillCamera && KillCamera.enabled)
        {
            KillCamera.enabled = false;
        }

        // Animator の GameOver フラグを初期化
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
            GhostAnimator.SetBool(GameOverBoolName, false);
        }

        // 画面フェードは最初は透明にしておく
        if (FadeCanvasGroup)
        {
            FadeCanvasGroup.alpha = 0f;
        }

        // プレイヤーのRendererをキャッシュ
        if (Player)
        {
            _cachedPlayerRenderers = Player.GetComponentsInChildren<Renderer>(true);
        }

        // テキストは最初は非表示（空文字＋オブジェクトOFF）
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
        // 毎フレーム：距離に応じてビネット強度をスムーズに更新
        UpdateDangerVignette();
    }

    IEnumerator DistanceWatchLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(CheckInterval);

        // 一番近い幽霊を見て距離を測って、近すぎたらゲームオーバー
        while (enabled && !_gameOverFired)
        {
            Transform nearGhost = GetNearestGhostToPlayer(); // プレイヤーに一番近い幽霊（クローン含む）

            if (Player && nearGhost != null)
            {
                float dist = Vector3.Distance(Player.position, nearGhost.position);

                // 隠れていて、幽霊がまだ探索状態ならスキップ
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
        if (!_hasVig || !Player) return;

        Transform nearGhost = GetNearestGhostToPlayer();
        if (!nearGhost) return;

        // 隠れていて安全なら徐々に戻す
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

        // プレイヤーと幽霊の距離から、ビネットの濃さを決める
        float dist = Vector3.Distance(Player.position, nearGhost.position);

        // WarnDistance…TriggerDistance の間で 0→1 に線形マップ
        float t = 0f;
        if (dist <= WarnDistance)
        {
            float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
            t = Mathf.Clamp01((WarnDistance - dist) / span);
        }

        float target = t * MaxVignette;

        // スムーズ追従
        _currIntensity = Mathf.MoveTowards(_currIntensity, target, FadeSpeed * Time.deltaTime);

        // 反映
        _vig.color.Override(EdgeColor);
        _vig.intensity.Override(_currIntensity);

        // お好みで色収差を少し
        if (_hasCA && AlsoShakeChromatic)
        {
            _ca.intensity.Override(_currIntensity * 0.55f);
        }
    }

    private void FireGameOver()
    {
        if (_gameOverFired) return;
        _gameOverFired = true;

        // 捕まったSEを鳴らす（AudioManager経由）
        if (AudioMgr != null)
        {
            AudioMgr.CatchSource();
        }
        else
        {
            Debug.LogWarning("[GameOver] AudioMgr が割り当てられていないので、捕まったSEは鳴りません。");
        }

        // プレイヤーの見た目を消す（プレイヤー本人視点に寄せる）
        HidePlayerVisualsIfNeeded();

        // 幽霊AnimatorのGameOverフラグをONにする（GameOverステートへ遷移）
        if (GhostAnimator && !string.IsNullOrEmpty(GameOverBoolName))
        {
            GhostAnimator.SetBool(GameOverBoolName, true);

            bool val = GhostAnimator.GetBool(GameOverBoolName);
            Debug.Log("[GameOver] " + GameOverBoolName + " を true にセットしました。現在値=" + val);

            LogAnimatorState("[GameOver] After SetBool");
        }
        else
        {
            Debug.LogWarning("[GameOver] GhostAnimator か GameOverBoolName が未設定です。");
        }

        // ゲームオーバー演出フロー開始
        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        // NavMeshAgent を止めて暴走防止
        if (StopAgentsOnGameOver)
        {
            NavMeshAgent a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            NavMeshAgent a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;
            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        // 捕まったSEを少し聞かせるためワンテンポ
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
            // 近距離で顔ドアップするのでNearClipを下げる（近すぎても透けないように）
            KillCamera.nearClipPlane = KillCamNearClip;

            KillCamera.enabled = true;
        }

        // キルカメラ追従＋アニメ終了待ち（＋余韻ホールド＋テキスト出し）
        yield return StartCoroutine(WaitForGameOverAnim());

        // 画面を暗転させる
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

    // アニメの待ち処理
    // 1) GameOverタグ付きステートに入るまで待つ
    // 2) 入ったらそのステートの1周完了まで待つ（normalizedTime>=1.0f）
    // 3) アニメが終わったあと HoldAfterAnimSeconds 秒ホールド
    //    - ホールド中はカメラを揺らし続ける（弱い揺れ, EnableCameraShake が true のときだけ）
    //    - ホールドの開始時にテキストを1文字ずつ出していく
    //    タグに入らない/終わらない時は FallbackDelay であきらめる
    private IEnumerator WaitForGameOverAnim()
    {
        float timer = 0f;
        bool enteredTaggedState = false;

        // (1) GameOverタグ付きステートに入るまで待つ
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

                    // 掴まれた瞬間の揺れ開始（短い強い揺れ）
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
                Debug.LogWarning("[GameOverAnimCheck] タグ付きステートに入らずFallback");
                break;
            }

            yield return null;
        }

        // (2) タグ付きステートの再生が終わるまで待つ
        if (enteredTaggedState)
        {
            while (true)
            {
                UpdateKillCameraFollow();
                LogAnimatorState("[GameOverAnimCheck] waiting FINISH");

                if (GhostAnimator != null)
                {
                    AnimatorStateInfo st = GhostAnimator.GetCurrentAnimatorStateInfo(GhostAnimLayer);

                    // タグが外れた = 遷移した → 終了扱い
                    if (!st.IsTag(GameOverAnimTag))
                    {
                        Debug.Log("[GameOverAnimCheck] タグ状態から離脱 -> FINISH扱い");
                        break;
                    }

                    // normalizedTime>=1.0f で1周分再生完了（ループしない前提）
                    if (st.normalizedTime >= 1.0f)
                    {
                        Debug.Log("[GameOverAnimCheck] normalizedTime>=1.0 -> FINISH");
                        break;
                    }
                }

                yield return null;
            }
        }

        // (3) アニメ終了後の見せつけホールド
        //     ここから先は「待ってる間ずっと揺れる」ようにする
        //     → ホールド時間分の揺れをスタート（少し弱い揺れ）
        if (EnableCameraShake)
        {
            StartShake(HoldAfterAnimSeconds, HoldShakeAmplitude);
        }

        //     → ホールド開始時にテキストを出し始める（1文字ずつ）
        StartTypewriterText();

        float hold = 0f;
        while (hold < HoldAfterAnimSeconds)
        {
            UpdateKillCameraFollow();
            hold += Time.deltaTime;
            yield return null;
        }
    }

    // テキスト1文字ずつ出す処理を開始（シーン中で1回だけ）
    private void StartTypewriterText()
    {
        // すでに他のGameOverから始まっていたら何もしない
        if (_textStarted)
        {
            Debug.Log("[GameOverText] すでにタイプ中なのでスキップしました");
            return;
        }

        _textStarted = true;
        Debug.Log("[GameOverText] タイプライター開始");

        if (GameOverText == null)
        {
            Debug.LogWarning("[GameOverText] GameOverText が設定されていません");
            return;
        }

        // テキストオブジェクトを出す（Canvas上で非表示だったら表示）
        GameOverText.gameObject.SetActive(true);
        GameOverText.text = "";

        // 念のため、もし前のコルーチンが残っていたら止める
        if (_typingCo != null)
        {
            StopCoroutine(_typingCo);
        }

        _typingCo = StartCoroutine(TypewriterCo());
    }

    // 実際に1文字ずつ出していくコルーチン
    private IEnumerator TypewriterCo()
    {
        if (GameOverText == null) yield break;

        string msg = GameOverMessage;
        GameOverText.text = "";

        // 1文字ごとに足していく
        for (int i = 0; i < msg.Length; i++)
        {
            GameOverText.text += msg[i];

            // 文字間ディレイ
            if (TextCharInterval > 0f)
            {
                yield return new WaitForSeconds(TextCharInterval);
            }
            else
            {
                yield return null;
            }
        }
    }

    // 揺れ開始用関数
    private void StartShake(float duration, float amplitude)
    {
        // カメラ揺れOFFの場合は何もしない
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

    // 画面をゆっくり暗くする
    private IEnumerator FadeOutScreen()
    {
        if (!FadeCanvasGroup)
        {
            // フェード用Canvasが未設定ならスキップ
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

    // Animatorの現在ステート情報をログにまとめて出す（デバッグ用）
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
            " layer=" + GhostAnimLayer +
            " bool(" + GameOverBoolName + ")=" + currentBool +
            " stateHash=" + shortHash +
            " tagMatch=" + inTagged +
            " tagWanted=" + GameOverAnimTag +
            " normTime=" + norm.ToString("0.00")
        );
    }

    // 捕まってる間、KillCameraを「一番近い幽霊」に合わせ続ける
    // ・カメラ位置をゴースト基準で決める
    // ・LookAtで幽霊方向を向く
    // ・X軸(上下)だけ手動で少し傾ける
    // ・揺れ中ならランダムに微振動を足す（_shakeTimeLeft > 0 の間ずっと、かつ EnableCameraShake==true のとき）
    private void UpdateKillCameraFollow()
    {
        if (!KillCamera) return;

        Transform tgtGhost = GetNearestGhostToPlayer();
        if (!tgtGhost)
        {
            Debug.LogWarning("[GameOverCam] tgtGhost=null (近い幽霊が見つからない)");
            return;
        }

        // カメラのベース位置（幽霊の正面ちょい前／横オフセット／高さ）
        Vector3 camPos = tgtGhost.position;
        camPos += tgtGhost.forward * CamDistFromGhost;
        camPos += tgtGhost.right * CamSideOffset;
        camPos.y = tgtGhost.position.y + CamHeightOffset;

        // 揺れ演出（揺れONのときだけ）
        if (EnableCameraShake && _shakeTimeLeft > 0f && _shakeTotalDuration > 0f)
        {
            float shakeT = _shakeTimeLeft / _shakeTotalDuration; // 1→0
            float amp = _currentShakeAmplitude * shakeT;         // 時間と共に弱める

            Vector3 randomOffset = new Vector3(
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp,
                (Random.value * 2f - 1f) * amp * 0.5f // Z方向は少し弱め
            );

            camPos += randomOffset;

            _shakeTimeLeft -= Time.deltaTime;
        }

        // カメラ位置を更新
        KillCamera.transform.position = camPos;

        // 常に幽霊本人を見る
        KillCamera.transform.LookAt(tgtGhost.position);

        // X回転（上下）だけオフセットする
        if (CamLookPitchOffsetDeg != 0f)
        {
            KillCamera.transform.rotation =
                KillCamera.transform.rotation * Quaternion.Euler(CamLookPitchOffsetDeg, 0f, 0f);
        }

        // ログ（デバッグ用）
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

    // プレイヤーの近い幽霊（=今掴んでるクローン含む）を探す
    private Transform GetNearestGhostToPlayer()
    {
        if (Player == null) return null;

        Transform nearest = null;
        float bestDist = float.PositiveInfinity;

        // タグ "Ghost" の候補
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

        // SearchChase持ち（タグついてないクローンの保険）
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

        // それでもnullなら最初に持ってるGhostを返す
        if (nearest == null)
        {
            nearest = Ghost;
        }

        return nearest;
    }

    private bool ShouldSkipCatch()
    {
        // クローゼットに隠れていて幽霊が「探索状態(=1)」ならスキップ
        if (!HideRef || !HideRef.hide) return false;

        int ghostState = (GhostChase ? GhostChase.GetState() : 1);
        return ghostState == 1;
    }

    private void OnDrawGizmosSelected()
    {
        Transform g = GetNearestGhostToPlayer();
        if (Player && g)
        {
            // トリガー距離の目安
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(g.position, Mathf.Max(TriggerDistance, 0.01f));

            // いまの距離ライン
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, g.position);

            // シーンビュー用に予定カメラ位置も描画
            Vector3 debugCamPos = g.position
                                + g.forward * CamDistFromGhost
                                + g.right * CamSideOffset;
            debugCamPos.y = g.position.y + CamHeightOffset;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(debugCamPos, 0.07f);
        }
    }

    // プレイヤーの見た目を消す処理
    // ・Renderer.enabled = false にするだけ
    // ・NavMeshAgentや当たり判定は残すのでゲームオーバー処理は続く
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
                if (r == null) continue;

                // プレイヤーの体・手・装備などをまとめて非表示
                r.enabled = false;
            }
        }

        _playerHidden = true;
        Debug.Log("[GameOver] プレイヤーのRendererを非表示にしました。");
    }
}
