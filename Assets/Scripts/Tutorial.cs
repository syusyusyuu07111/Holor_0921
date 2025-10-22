using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ========== テキスト／タイプ演出 ==========
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };
    [TextArea] public string[] Step3Lines = { "……何か音がしたぞ！", "周りを探してみよう。" }; // 1回目の生成時にのみ出す
    private Coroutine _typing;

    // ロック解除テキスト
    [TextArea] public string DoorUnlockedMessage = "ドアが開いたようだ";
    private bool _didAnnounceDoorUnlocked = false; // 一度だけ表示

    // ========== 進行度参照 ==========
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    // ========== OpenDoor 制御 ==========
    [Header("制御対象（OpenDoorのみ）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ========== ドア：ロック時の入力フック（Step2トリガ） ==========
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public string DoorLockedMessage = "ドアはあかないようだ…";
    public float DoorLockedCooldown = 1.0f;
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ========== 初見パネル（幽霊）＆一時停止 ==========
    [Header("初見チュートリアル画像（幽霊）")]
    public GameObject Step4Panel_StateAny;   // 初めて幽霊が見えた
    public GameObject Step5Panel_State2;     // 初めて state=2 を見た
    private bool _didStep4 = false;
    private bool _didStep5 = false;

    // ========== 初見パネル（隠れる） ==========
    [Header("隠れるチュートリアル画像")]
    public HideCroset HideRef;               // HideCroset をアサイン
    public GameObject HidePanel;             // 隠れチュートリアル画像
    private bool _didHidePanel = false;

    // 保留フラグ：前段未完了やポーズ中に初回イベントが来たらいったん貯める
    private bool _pendingHidePanel = false;

    // パネル共通：一時停止のゲート
    private bool _pauseGate = false;         // パネル表示中は入力/演出を止めたい時に使う

    // ========== 抽選開始（Step3）制御 ==========
    [Header("幽霊スポナー（EnemyAI）")]
    public List<EnemyAI> Spawners = new();   // AutoStart=false 推奨（保険でStartで止める）
    public float StartSpawnDelayAfterStep2 = 2f; // Step2テキストが消えた後の待機

    private bool _didStep2 = false;          // ドアロックを初回検知したか
    private bool _didStep3 = false;          // 抽選開始を実行したか

    // Step3テキストを「最初の生成時に一度だけ」出すためのフラグ
    private bool _step3TextShown = false;

    // ========== 前段チュートリアル（移動／視点／ダッシュ） ==========
    [Header("前段チュートリアル（移動／視点／ダッシュ）")]
    public bool EnableBasicTutorial = true;
    public Transform CameraTransform;
    public PlayerController PlayerCtrl;   // PlayerController から状態参照

    [TextArea] public string BasicMoveText = "移動してみよう（WASD / 左スティック）";
    [TextArea] public string BasicLookText = "カメラを動かしてみよう（マウス / 右スティック）";
    [TextArea] public string BasicDashText = "シフトを押しながらダッシュしてみよう";
    [TextArea] public string BasicDoneText = "OK！準備完了。";

    [Header("前段チュートリアル：しきい値")]
    public float BasicLookYawTotal = 20f;        // ヨーの合計角度
    public float BasicLookPitchTotal = 10f;      // ピッチの合計角度
    public float BasicMoveMinDuration = 0.15f;   // 「動いている」継続時間
    public float BasicDashMinDuration = 0.15f;   // 「ダッシュ中」継続時間

    // 移動クリアに合計距離もしばる
    public float BasicMoveTotalDistanceRequired = 1.5f; // XZ合計[m]
    public bool BasicMoveCountOnlyWhenInput = true;      // 入力がある時だけ距離を積算
    public float BasicMoveMaxStepPerFrame = 2.0f;        // テレポ等の急加速を無視

    // 内部（前段チュートリアル）
    private bool _basicRunning = false;
    private bool _basicDone = false;
    private Quaternion _basicPrevCamRot;
    private float _basicAccYaw = 0f;
    private float _basicAccPitch = 0f;
    private Vector3 _basicMovePrevPos;
    private float _basicMoveTotal = 0f;

    // ========== ポーズ時のオーディオ制御 ==========
    [Header("ポーズ時のオーディオ制御")]
    public bool PauseAudioWhilePanel = true;
    private bool _prevListenerPause = false;

    // ========== ヒント（外部要求された台詞）の遅延表示 ==========
    private readonly Queue<string[]> _queuedHintTutorials = new Queue<string[]>();

    [Serializable]
    public class LineCue
    {
        public string Id;
        [TextArea] public string[] Lines;
    }

    [Header("行開示→台詞マッピング")]
    public List<LineCue> LineCues = new List<LineCue>();

    private Dictionary<string, string[]> _cueMap;

    // ========== ドア用ミッション（独立テキスト） ==========
    [Header("ドア用ミッション（別テキストUI）")]
    public bool EnableDoorMission = true;

    // ミッション専用の独立テキスト（BottomTextとは別）
    public TextMeshProUGUI MissionText;
    public float MissionCharsPerSecond = 40f;
    public float MissionLineInterval = 0.4f;
    public bool MissionHideWhenDone = false;

    [TextArea] public string Mission_DoorCheck = "ドアをしらべてみよう";
    [TextArea] public string Mission_FindGhost = "次は近くにいる幽霊を見つけてみよう";
    [TextArea] public string Mission_HearVoiceGoNext = "次は幽霊の声を聞いて次の部屋に行こう";
    [TextArea] public string Mission_AllDone = "ミッション完了";

    private enum DoorMissionStage { None, DoorCheck, FindGhost, HearVoiceGoNext, AllDone }
    private DoorMissionStage _doorMission = DoorMissionStage.None;
    private Coroutine _typingMission;
    private bool _heardVoice = false; // state=2 検知フラグ

    // ========== ライト表示制御 ==========
    [Header("チュートリアル中は非表示にするライト")]
    public List<GameObject> LightsToToggle = new List<GameObject>(); // 最初に隠したいライト（親ごとでも可）
    public bool HideLightsUntilMission3 = true;                       // ミッション3まで隠す
    private bool _lightsActivatedAfterM3 = false;                      // 二重防止

    // 前段が未完了の間は一切のイベント/進行を出さないための共通ゲート
    private bool IsEventAllowed() => !EnableBasicTutorial || _basicDone;

    // 現在のタイプ演出を強制スキップ
    private void SkipCurrentTyping()
    {
        if (_typing != null)
        {
            StopCoroutine(_typing);
            _typing = null;
        }
        if (BottomText) BottomText.gameObject.SetActive(false);
    }

    // ========== ライフサイクル ==========
    private void Awake()
    {
        if (!HintRef && AutoFindHintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        _input = new InputSystem_Actions();
        BuildCueMap();
    }

    private void BuildCueMap()
    {
        _cueMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (LineCues == null) return;

        for (int i = 0; i < LineCues.Count; i++)
        {
            var cue = LineCues[i];
            if (cue == null) continue;
            if (string.IsNullOrWhiteSpace(cue.Id)) continue;
            if (cue.Lines == null) continue;

            _cueMap[cue.Id.Trim()] = (string[])cue.Lines.Clone();
        }
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        // 幽霊・進行度のイベント
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);

            // ★ ここに移動（イベント購読はクラス直下では不可）
            HintRef.OnHintTutorialLinesRequested.AddListener(OnHintTutorialLinesRequested);
            HintRef.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);
        }

        // 隠れ案内 初回表示イベント（HideCroset側から）
        if (HideRef) HideRef.OnFirstHidePromptShown.AddListener(ShowHidePanelOnce);

        // 前段チュートリアル開始
        if (EnableBasicTutorial) StartCoroutine(CoRunBasicTutorial());

        // ドア用ミッション開始（独立表示）
        if (EnableDoorMission) StartDoorMissionIfNeeded();

        // スポーナー生成イベント購読：最初の1回だけ Step3Lines を出す
        if (Spawners != null)
        {
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].OnGhostSpawned.AddListener(OnAnyGhostSpawned_FirstTime);
        }
    }

    private void OnDisable()
    {
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.RemoveListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);

            // ★ 購読解除もここで
            HintRef.OnHintTutorialLinesRequested.RemoveListener(OnHintTutorialLinesRequested);
            HintRef.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
        if (HideRef) HideRef.OnFirstHidePromptShown.RemoveListener(ShowHidePanelOnce);

        _input.Player.Disable();
        _input.UI.Disable();

        if (Time.timeScale == 0f) Time.timeScale = 1f; // 念のため復旧

        // 購読解除
        if (Spawners != null)
        {
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].OnGhostSpawned.RemoveListener(OnAnyGhostSpawned_FirstTime);
        }
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);
        if (HidePanel) HidePanel.SetActive(false);

        // 念のため自動開始を止める（AutoStart=false推奨だが保険）
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
        Step1();

        // 保留されていたヒントがあれば消化
        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        // MissionText 初期化
        if (MissionText) { MissionText.text = ""; MissionText.gameObject.SetActive(false); }

        // ミッション3までライトは非表示
        if (HideLightsUntilMission3 && LightsToToggle != null)
        {
            for (int i = 0; i < LightsToToggle.Count; i++)
                if (LightsToToggle[i]) LightsToToggle[i].SetActive(false);
        }
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // パネル中は抑止

        // ミッション3：声を聞いた後、有効なドアにして完了
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext && !_pauseGate && IsEventAllowed())
        {
            TryCompleteDoorMissionByEnabledDoorInteract();
        }
    }

    // ========== Step2：ドアロック文言 → その後Step3（抽選開始） ==========
    private void HandleLockedDoorTapFeedback()
    {
        if (!IsEventAllowed()) return;
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            // 既に開けられる段階ならスルー（→ ミッション3の別処理で扱う）
            if (od.enabled) continue;

            // 距離
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // 表側チェック
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step1 などの読み上げ中なら一旦スキップ
            SkipCurrentTyping();

            // Step2：ロック文言（OneShot）
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            // ドア用ミッション：ステージ1達成
            if (EnableDoorMission && _doorMission == DoorMissionStage.DoorCheck)
            {
                AdvanceDoorMissionTo(DoorMissionStage.FindGhost);
            }

            // Step3の予約（初回だけ）
            if (!_didStep2)
            {
                _didStep2 = true;
                StartCoroutine(CoAfterStep2_StartStep3());
            }
            break;
        }
    }

    private IEnumerator CoAfterStep2_StartStep3()
    {
        // Step2 OneShot が消えるのを待つ
        while (BottomText && BottomText.gameObject.activeSelf) yield return null;

        // 少し間を置く
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);

        // Step3 実行（抽選開始のみ）
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;

        if (!IsEventAllowed()) return;

        // 抽選開始
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].BeginSpawning();

        // テキストは「最初の生成イベント」で出す
    }

    // 最初の生成時に一度だけ Step3Lines を表示
    private void OnAnyGhostSpawned_FirstTime()
    {
        if (!IsEventAllowed()) return;
        if (_step3TextShown) return;
        _step3TextShown = true;

        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            if (_typing != null) StopCoroutine(_typing);
            if (BottomText)
            {
                BottomText.gameObject.SetActive(true);
                _typing = StartCoroutine(CoTypeLines(Step3Lines));
            }
        }
    }

    // ========== Step4/5 パネル表示 ==========
    public void Step4_ShowPanel()
    {
        if (!IsEventAllowed()) return;
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));

        // ミッション：ステージ2達成（幽霊を見つけた）
        if (EnableDoorMission && _doorMission == DoorMissionStage.FindGhost)
            AdvanceDoorMissionTo(DoorMissionStage.HearVoiceGoNext);
    }

    public void Step5_ShowPanel()
    {
        if (!IsEventAllowed()) return;
        if (_didStep5) return;
        _didStep5 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));

        _heardVoice = true;

        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext)
            ShowMissionText(Mission_HearVoiceGoNext);
    }

    public void ShowHidePanelOnce()
    {
        if (_didHidePanel) return;

        if (_pauseGate || !IsEventAllowed())
        {
            _pendingHidePanel = true;
            return;
        }

        _didHidePanel = true;
        StartCoroutine(CoShowPausePanel(HidePanel));
    }

    // ========== 共通：パネル表示→一時停止→UI.Submitで閉じる ==========
    private IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        panel.SetActive(true);

        // ★ ここで現在の AudioListener.pause を保存してからポーズ
        _prevListenerPause = AudioListener.pause;
        if (PauseAudioWhilePanel) AudioListener.pause = true;

        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        // 1フレーム待ってから入力待ち
        yield return null;
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        panel.SetActive(false);

        // ★ 音声ポーズを元に戻す（ユーザが元々ミュートならそのまま）
        if (PauseAudioWhilePanel) AudioListener.pause = _prevListenerPause;

        Time.timeScale = prevScale;
        _pauseGate = false;

        // 解除後に保留があれば出す
        if (_pendingHidePanel && IsEventAllowed())
        {
            _pendingHidePanel = false;
            if (!_didHidePanel && HidePanel)
            {
                _didHidePanel = true;
                StartCoroutine(CoShowPausePanel(HidePanel));
            }
        }
    }

    // ========== ドア制御 ==========
    private void ApplyDoorEnableByProgress(int progress)
    {
        _lastAppliedProgress = progress;
        bool enableDoor = progress >= MinProgressToEnableDoor;

        bool anyJustEnabled = false; // この呼び出しで“初めて有効になった”ドアがあるか
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            bool wasEnabled = od.enabled;
            if (od.enabled != enableDoor) od.enabled = enableDoor;

            if (!wasEnabled && enableDoor) anyJustEnabled = true;
        }

        // ちょうど今ロック解除された → 一度だけテキストを出す（ポーズ中は出さない）
        if (enableDoor && anyJustEnabled && !_didAnnounceDoorUnlocked && !_pauseGate)
        {
            ShowOneShot(string.IsNullOrEmpty(DoorUnlockedMessage) ? "ドアが開いたようだ" : DoorUnlockedMessage);
            _didAnnounceDoorUnlocked = true;
        }
    }
    private void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // ========== タイプ演出 ==========
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    private IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            if (li < lines.Length - 1) yield return new WaitForSeconds(LineInterval);
        }
        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }
        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            // 時間停止中はタイプを止めたいので deltaTime を使用
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ========== 前段チュートリアル本体 ==========
    private IEnumerator CoRunBasicTutorial()
    {
        if (_basicRunning || _basicDone) yield break;
        if (!BottomText) yield break;

        _basicRunning = true;

        // 参照の初期化
        if (CameraTransform) _basicPrevCamRot = CameraTransform.rotation;

        // 1フレーム待って（Start() の処理が終わるのを待つ）→テキストを上書き
        yield return null;

        // ---- 移動してみよう ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicMoveText));

        // 合計距離トラッキング初期化
        _basicMoveTotal = 0f;
        _basicMovePrevPos = Player ? Player.position : Vector3.zero;

        float moveTimer = 0f;
        while (true)
        {
            // 「動いているか」判定
            bool moving = PlayerCtrl ? PlayerCtrl.IsMovingNow : (_input.Player.Move.ReadValue<Vector2>() != Vector2.zero);

            // 合計距離を積算（XZのみ）
            if (Player)
            {
                Vector3 cur = Player.position;
                Vector3 delta = cur - _basicMovePrevPos; delta.y = 0f;

                float step = delta.magnitude;
                step = Mathf.Min(step, BasicMoveMaxStepPerFrame); // テレポ/異常値防止

                if (!BasicMoveCountOnlyWhenInput || moving)
                    _basicMoveTotal += step;

                _basicMovePrevPos = cur;
            }

            // 継続時間カウント
            if (moving) moveTimer += Time.deltaTime;
            else moveTimer = 0f;

            // クリア条件
            if (moveTimer >= BasicMoveMinDuration && _basicMoveTotal >= BasicMoveTotalDistanceRequired)
                break;

            yield return null;
        }

        // ---- カメラを動かしてみよう ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicLookText));

        _basicAccYaw = 0f; _basicAccPitch = 0f;
        while (true)
        {
            if (CameraTransform)
            {
                Quaternion cur = CameraTransform.rotation;

                // 前方ベクトルからヨー／ピッチを近似
                Vector3 fPrev = _basicPrevCamRot * Vector3.forward;
                Vector3 fCur = cur * Vector3.forward;

                float yawPrev = Mathf.Atan2(fPrev.x, fPrev.z) * Mathf.Rad2Deg;
                float yawCur = Mathf.Atan2(fCur.x, fCur.z) * Mathf.Rad2Deg;
                float dyaw = Mathf.DeltaAngle(yawPrev, yawCur);
                _basicAccYaw += Mathf.Abs(dyaw);

                float pitchPrev = Mathf.Asin(Mathf.Clamp(fPrev.y, -1f, 1f)) * Mathf.Rad2Deg;
                float pitchCur = Mathf.Asin(Mathf.Clamp(fCur.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dpitch = Mathf.DeltaAngle(pitchPrev, pitchCur);
                _basicAccPitch += Mathf.Abs(dpitch);

                _basicPrevCamRot = cur;

                if (_basicAccYaw >= BasicLookYawTotal && _basicAccPitch >= BasicLookPitchTotal)
                    break;
            }
            yield return null;
        }

        // ---- ダッシュしてみよう ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicDashText));

        float dashTimer = 0f;
        float decayPerSec = 0.5f; // 一瞬の落ち込みに猶予
        while (true)
        {
            bool dashing = PlayerCtrl ? PlayerCtrl.IsDashingNow : false;

            if (dashing) dashTimer += Time.deltaTime;
            else dashTimer = Mathf.Max(0f, dashTimer - Time.deltaTime * decayPerSec);

            if (dashTimer >= BasicDashMinDuration) break;
            yield return null;
        }

        // 完了（即時表示）
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        BottomText.text = BasicDoneText;

        _basicDone = true;
        _basicRunning = false;

        // 保留されていたらこのタイミングで表示
        if (_pendingHidePanel)
        {
            _pendingHidePanel = false;
            if (!_pauseGate && !_didHidePanel && HidePanel)
            {
                _didHidePanel = true;
                StartCoroutine(CoShowPausePanel(HidePanel));
            }
        }

        // 本編チュートリアルへ
        Step1();

        // ドア用ミッションが未開始なら開始
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }

    // ========== ドア用ミッション制御 ==========
    private void StartDoorMissionIfNeeded()
    {
        if (_doorMission != DoorMissionStage.None) return;
        if (EnableBasicTutorial && !_basicDone) return;

        _doorMission = DoorMissionStage.DoorCheck;
        ShowMissionText(Mission_DoorCheck);
    }

    private void AdvanceDoorMissionTo(DoorMissionStage next)
    {
        _doorMission = next;
        switch (_doorMission)
        {
            case DoorMissionStage.FindGhost:
                ShowMissionText(Mission_FindGhost);
                break;

            case DoorMissionStage.HearVoiceGoNext:
                ShowMissionText(Mission_HearVoiceGoNext);
                // このタイミングでライトをアクティブにする
                if (HideLightsUntilMission3) ActivateLightsAfterMission3();
                break;

            case DoorMissionStage.AllDone:
                ShowMissionText(Mission_AllDone);
                if (MissionHideWhenDone && MissionText) StartCoroutine(CoHideMissionAfter(MissionLineInterval));
                break;
        }
    }

    private void ShowMissionText(string line)
    {
        if (!MissionText || string.IsNullOrEmpty(line)) return;

        if (_typingMission != null) { StopCoroutine(_typingMission); _typingMission = null; }
        MissionText.gameObject.SetActive(true);
        _typingMission = StartCoroutine(CoTypeOne_Mission(line));
    }

    private IEnumerator CoTypeOne_Mission(string text)
    {
        MissionText.text = "";
        if (MissionCharsPerSecond <= 0f) { MissionText.text = text; yield break; }

        float interval = 1f / MissionCharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                MissionText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    private IEnumerator CoHideMissionAfter(float wait)
    {
        yield return new WaitForSeconds(wait);
        if (MissionText) MissionText.gameObject.SetActive(false);
    }

    private void TryCompleteDoorMissionByEnabledDoorInteract()
    {
        // 「声を聞いた」必須にしたい場合は以下のガードを戻す
        // if (!_heardVoice) return;

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame();

        if (!pressed || !Player) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (!od.enabled) continue; // 有効なドアのみ

            // 距離
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // 表側チェック（必要なら）
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            AdvanceDoorMissionTo(DoorMissionStage.AllDone);
            break;
        }
    }

    // ========== ライト有効化 ==========
    private void ActivateLightsAfterMission3()
    {
        if (_lightsActivatedAfterM3) return;
        _lightsActivatedAfterM3 = true;

        if (LightsToToggle == null) return;
        for (int i = 0; i < LightsToToggle.Count; i++)
            if (LightsToToggle[i]) LightsToToggle[i].SetActive(true);
    }

    // ========== ヒント（外部要求の台詞）表示キュー ==========
    private void OnHintLineFullyRevealed(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_cueMap == null || !_cueMap.TryGetValue(id, out var lines)) return;
        if (lines == null || lines.Length == 0) return;

        if (!IsEventAllowed() || _pauseGate || _typing != null)
        {
            _queuedHintTutorials.Enqueue(DuplicateLines(lines));
            return;
        }

        ShowHintTutorialLinesNow(lines);
    }

    private void OnHintTutorialLinesRequested(string[] lines)
    {
        if (!HasAnyContent(lines)) return;

        if (!IsEventAllowed() || _pauseGate || _typing != null)
        {
            _queuedHintTutorials.Enqueue(DuplicateLines(lines));
            return;
        }

        ShowHintTutorialLinesNow(lines);
    }

    private void ShowHintTutorialLinesNow(string[] lines)
    {
        if (!BottomText) return;

        var copy = DuplicateLines(lines);
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(copy));
    }

    private static string[] DuplicateLines(string[] source)
    {
        if (source == null || source.Length == 0) return Array.Empty<string>();
        var copy = new string[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = source[i];
        return copy;
    }

    private static bool HasAnyContent(string[] lines)
    {
        if (lines == null) return false;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) return true;
        return false;
    }
}
