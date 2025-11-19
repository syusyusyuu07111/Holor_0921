using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering;                 // URP Volume
using UnityEngine.Rendering.Universal;      // URP Vignette
using TMPro;                                // TMPテキスト表示用
using CriWare;                              // CRI Atom 用（環境によっては不要なら外してOK）

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
    public float WarnDistance = 2.0f;           // この距離以内でビネットが濃くなる & ピンチSE範囲
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

    [Header("ピンチSE（3D・CRI Atom Loop BGM）")]
    public CriAtomSource PinchSource;           // ピンチ時に鳴らすループBGM用（3D想定）
    private bool _pinchPlaying = false;         // 再生中かどうか

    [Header("ピンチSE（2D・距離で音量変化）")]
    public CriAtomSource Pinch2DSource;         // 距離に応じて音量が0→1になる2D SE
    private bool _pinch2DPlaying = false;       // 再生中かどうか

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
        Debug.Log("[GameOver] Awake 開始");

        // Player自動取得
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
                PlayerController pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
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

        // Ghost自動取得（とりあえず1体）
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
                SearchChase anyChase = UnityEngine.Object.FindFirstObjectByType<SearchChase>();
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

        // HideCroset
        if (!HideRef)
        {
            HideRef = UnityEngine.Object.FindFirstObjectByType<HideCroset>();
            if (HideRef == null)
                Debug.LogWarning("[GameOver] HideCroset がシーンに見つかりません");
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

            Debug.Log($"[GameOver] Volume 初期化 hasVig={_hasVig} hasCA={_hasCA}");

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

        // === サウンドの初期状態ログ ===
        if (PinchSource != null)
        {
            Debug.Log("[GameOver] PinchSource(3D) が設定されています。obj=" + PinchSource.gameObject.name);
        }
        else
        {
            Debug.LogWarning("[GameOver] PinchSource(3D) が未設定です");
        }

        if (Pinch2DSource != null)
        {
            Debug.Log("[GameOver] Pinch2DSource(2D) が設定されています。obj=" + Pinch2DSource.gameObject.name);
        }
        else
        {
            Debug.LogWarning("[GameOver] Pinch2DSource(2D) が未設定です");
        }

        Debug.Log("[GameOver] Awake 完了");
    }

    void OnEnable()
    {
        StartCoroutine(DistanceWatchLoop());
    }

    void Update()
    {
        // デバッグ：Pキーで2DピンチSEをテスト再生
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (Pinch2DSource != null)
            {
                Debug.Log("[GameOver] [TEST] Pキーで Pinch2DSource.Play() を呼びます");
                Pinch2DSource.volume = 1.0f;
                Pinch2DSource.Play();
                _pinch2DPlaying = true;
            }
            else
            {
                Debug.LogWarning("[GameOver] [TEST] Pinch2DSource が未設定のため再生できません");
            }
        }

        // ★ ゲームオーバー後は演出更新を止める & ピンチSEも必ずOFF
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

        // 毎フレーム：幽霊との距離からビネット濃度をじわっと更新 & ピンチSE制御
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
                Ghost = nearGhost;
                GhostChase = nearGhost.GetComponent<SearchChase>();

                float dist = Vector3.Distance(Player.position, nearGhost.position);
                bool hidden = (HideRef && HideRef.hide);
                int state = (GhostChase ? GhostChase.GetState() : 1);
                bool skip = ShouldSkipCatch();
                bool willFire = (!skip && dist <= TriggerDistance);

                Debug.Log($"[CatchCheck] ghost={nearGhost.name} dist={dist:F3} trigger={TriggerDistance:F3} hidden={hidden} state={state} skip={skip} fire={willFire}");

                if (willFire)
                {
                    Debug.Log("[CatchCheck] 発火条件成立 → FireGameOver()");
                    FireGameOver();
                    yield break;
                }
            }

            yield return wait;
        }
    }

    // 距離に応じた警告ビネット更新 + ピンチSE制御
    private void UpdateDangerVignette()
    {
        if (!_hasVig || !Player) return;

        Transform nearGhost = GetNearestGhostToPlayer();
        if (!nearGhost) return;

        float dist = Vector3.Distance(Player.position, nearGhost.position);
        bool isSafe = ShouldSkipCatch(); // 隠れ＋state1なら安全

        Debug.Log($"[Danger] dist={dist:F3}, isSafe={isSafe}");

        // 隠れてて安全ならフェードアウト方向（= state1 かつ 隠れ中のみ）
        if (isSafe)
        {
            _currIntensity = Mathf.MoveTowards(_currIntensity, 0f, FadeSpeed * Time.deltaTime);
            _vig.intensity.Override(_currIntensity);

            if (_hasCA && AlsoShakeChromatic)
            {
                _ca.intensity.Override(_currIntensity * 0.55f);
            }

            // 安全状態なのでピンチSEも止める
            UpdatePinchSEByDistance(dist, true);
            return;
        }

        // プレイヤーと幽霊の距離が近いほど濃い
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

        // WarnDistance を境にピンチSE（3D/2D）をON/OFF＋2D音量更新
        UpdatePinchSEByDistance(dist, false);
    }

    // ここで実際に「捕まった！」の処理
    private void FireGameOver()
    {
        if (_gameOverFired) return;
        _gameOverFired = true;

        Debug.Log("[GameOver] FireGameOver 実行");

        // 捕まった瞬間にピンチBGMを必ず停止（3D/2D両方）
        if (PinchSource != null && _pinchPlaying)
        {
            Debug.Log("[GameOver] FireGameOver で PinchSource(3D) STOP");
            PinchSource.Stop();
            _pinchPlaying = false;
        }
        if (Pinch2DSource != null)
        {
            if (_pinch2DPlaying)
            {
                Debug.Log("[GameOver] FireGameOver で Pinch2DSource(2D) STOP");
                Pinch2DSource.Stop();
                _pinch2DPlaying = false;
            }
            Pinch2DSource.volume = 0f;
        }

        Transform nearGhost = GetNearestGhostToPlayer();
        if (nearGhost != null)
        {
            Ghost = nearGhost;
            GhostChase = nearGhost.GetComponent<SearchChase>();
        }

        Animator nearestAnim = GetNearestGhostAnimator();
        if (nearestAnim != null)
        {
            GhostAnimator = nearestAnim;
        }

        try
        {
            if (AudioMgr != null)
            {
                AudioMgr.CatchSource();
            }
            else
            {
                Debug.LogWarning("[GameOver] AudioMgr がありません（捕まったSEなし）");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameOver] SE再生中に例外：{ex.Message}\n{ex.StackTrace}\n→ SEをスキップして続行します。");
        }

        HidePlayerVisualsIfNeeded();

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

        StartCoroutine(GameOverSequence());
    }

    // ゲームオーバー演出のフロー本体
    private IEnumerator GameOverSequence()
    {
        if (StopAgentsOnGameOver)
        {
            NavMeshAgent a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            NavMeshAgent a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;
            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        if (CameraCutDelay > 0f)
        {
            yield return new WaitForSeconds(CameraCutDelay);
        }

        if (MainCamera && MainCamera.enabled)
        {
            MainCamera.enabled = false;
        }
        if (KillCamera)
        {
            KillCamera.nearClipPlane = KillCamNearClip;
            KillCamera.enabled = true;
        }

        yield return StartCoroutine(WaitForGameOverAnim());
        yield return StartCoroutine(FadeOutScreen());

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

    private IEnumerator WaitForGameOverAnim()
    {
        float timer = 0f;
        bool enteredTaggedState = false;

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

        if (EnableCameraShake)
        {
            StartShake(HoldAfterAnimSeconds, HoldShakeAmplitude);
        }

        StartTypewriterText();

        float hold = 0f;
        while (hold < HoldAfterAnimSeconds)
        {
            UpdateKillCameraFollow();
            hold += Time.deltaTime;
            yield return null;
        }
    }

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

    private void UpdateKillCameraFollow()
    {
        if (!KillCamera) return;

        Transform tgtGhost = GetNearestGhostToPlayer();
        if (!tgtGhost)
        {
            Debug.LogWarning("[GameOverCam] tgtGhost=null (近い幽霊が見つからない)");
            return;
        }

        Vector3 camPos = tgtGhost.position;
        camPos += tgtGhost.forward * CamDistFromGhost;
        camPos += tgtGhost.right * CamSideOffset;
        camPos.y = tgtGhost.position.y + CamHeightOffset;

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

            _shakeTimeLeft -= Time.deltaTime;
        }

        KillCamera.transform.position = camPos;
        KillCamera.transform.LookAt(tgtGhost.position);

        if (CamLookPitchOffsetDeg != 0f)
        {
            KillCamera.transform.rotation =
                KillCamera.transform.rotation * Quaternion.Euler(CamLookPitchOffsetDeg, 0f, 0f);
        }

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

    private Transform GetNearestGhostToPlayer()
    {
        if (Player == null) return null;

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

        if (nearest == null)
        {
            nearest = Ghost;
        }

        return nearest;
    }

    private Animator GetNearestGhostAnimator()
    {
        Transform t = GetNearestGhostToPlayer();
        if (!t) return null;

        Animator a = t.GetComponentInChildren<Animator>();
        return a;
    }

    private bool ShouldSkipCatch()
    {
        bool hidden = (HideRef && HideRef.hide);
        int state = (GhostChase ? GhostChase.GetState() : 1);
        return (state == 1 && hidden);
    }

    private void OnDrawGizmosSelected()
    {
        Transform g = GetNearestGhostToPlayer();
        if (Player && g)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(g.position, Mathf.Max(TriggerDistance, 0.01f));

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, g.position);

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
        if (!HidePlayerOnGameOver) return;
        if (_playerHidden) return;
        if (Player == null) return;

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

    // === WarnDistance を境に、距離と安全状態でピンチBGM(3D)と2D SEを制御 ===
    private void UpdatePinchSEByDistance(float dist, bool isSafe)
    {
        // === デバッグ共通ログ ===
        float pinch01Debug = 0f;
        if (dist <= WarnDistance)
        {
            float spanDebug = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
            pinch01Debug = Mathf.Clamp01((WarnDistance - dist) / spanDebug);
        }
        Debug.Log($"[Pinch] isSafe={isSafe}, dist={dist:F3}, Warn={WarnDistance:F3}, Trigger={TriggerDistance:F3}, pinch01(2D)={pinch01Debug:F2}");

        // 安全状態（クローゼット＋state1）のときは両方止めて音量0
        if (isSafe)
        {
            if (PinchSource != null && _pinchPlaying)
            {
                PinchSource.Stop();
                _pinchPlaying = false;
                Debug.Log("[PinchSE] STOP (safe hide / 3D)");
            }

            if (Pinch2DSource != null)
            {
                if (_pinch2DPlaying)
                {
                    Pinch2DSource.Stop();
                    _pinch2DPlaying = false;
                    Debug.Log("[Pinch2D] STOP (safe hide)");
                }
                Pinch2DSource.volume = 0f;
            }
            return;
        }

        bool inPinchRange = (dist <= WarnDistance);

        // --- 3D ピンチSEのON/OFF ---
        if (PinchSource != null)
        {
            if (!_pinchPlaying && inPinchRange)
            {
                PinchSource.Play();
                _pinchPlaying = true;
                Debug.Log("[PinchSE] START  dist=" + dist.ToString("0.00"));
            }
            else if (_pinchPlaying && !inPinchRange)
            {
                PinchSource.Stop();
                _pinchPlaying = false;
                Debug.Log("[PinchSE] STOP   dist=" + dist.ToString("0.00"));
            }
        }

        // --- 2D ピンチSEの音量制御 ---
        if (Pinch2DSource != null)
        {
            float pinch01 = 0f;

            if (dist <= WarnDistance)
            {
                float span = Mathf.Max(WarnDistance - TriggerDistance, 0.0001f);
                pinch01 = Mathf.Clamp01((WarnDistance - dist) / span); // dist=Warn →0, dist=Trigger→1
            }

            Debug.Log($"[Pinch2D] dist={dist:F3}, pinch01={pinch01:F2}, playing={_pinch2DPlaying}");

            if (!_pinch2DPlaying && pinch01 > 0f)
            {
                Debug.Log("[Pinch2D] START 再生要求");
                Pinch2DSource.Play();
                _pinch2DPlaying = true;
            }
            else if (_pinch2DPlaying && pinch01 <= 0f)
            {
                Debug.Log("[Pinch2D] STOP 再生停止");
                Pinch2DSource.Stop();
                _pinch2DPlaying = false;
            }

            if (_pinch2DPlaying)
            {
                Pinch2DSource.volume = pinch01;
            }
            else
            {
                Pinch2DSource.volume = 0f;
            }
        }
    }

    public float GetDangerBlend01()
    {
        if (!_hasVig || MaxVignette <= 0f) return 0f;
        return Mathf.Clamp01(_currIntensity / MaxVignette);
    }
}
