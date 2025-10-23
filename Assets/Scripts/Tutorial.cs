using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ========== テキスト／タイプ演出 ==========
    [Header("メインテキスト")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };
    [TextArea] public string[] Step3Lines = { "……何か音がしたぞ！", "周りを探してみよう。" }; // 最初の生成時に出す
    private Coroutine _typing;
    private bool _muteBottomTyping = false; // スキップ中の点滅防止

    // ロック解除テキスト
    [Header("ロック解除メッセージ")]
    [TextArea] public string DoorUnlockedMessage = "ドアが開いたようだ";
    private bool _didAnnounceDoorUnlocked = false;

    // ========== 進行度参照 ==========
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    // ========== OpenDoor 制御 ==========
    [Header("制御対象（OpenDoor のみ）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ========== ドア：ロック時の入力フック（Step2 トリガ） ==========
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    [TextArea] public string DoorLockedMessage = "ドアはあかないようだ…";
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
    private bool _pendingHidePanel = false;  // パネル中や前段未完了なら保留

    // パネル表示中のゲート
    private bool _pauseGate = false;

    // ========== 抽選開始（Step3）制御 ==========
    [Header("幽霊スポナー（EnemyAI）")]
    public List<EnemyAI> Spawners = new();         // AutoStart=false 推奨
    public float StartSpawnDelayAfterStep2 = 2f;   // Step2テキストが消えた後の待機
    private bool _didStep2 = false;                // ドアロック検知したか
    private bool _didStep3 = false;                // 抽選開始したか
    private bool _step3TextShown = false;          // Step3Lines を一度だけ

    // ========== 前段チュートリアル（移動／視点／ダッシュ） ==========
    [Header("前段チュートリアル（移動／視点／ダッシュ）")]
    public bool EnableBasicTutorial = true;        // ※セーブなしなので毎回ここで制御
    public Transform CameraTransform;
    public PlayerController PlayerCtrl;

    [TextArea] public string BasicMoveText = "移動してみよう（WASD / 左スティック）";
    [TextArea] public string BasicLookText = "カメラを動かしてみよう（マウス / 右スティック）";
    [TextArea] public string BasicDashText = "シフトを押しながらダッシュしてみよう";
    [TextArea] public string BasicDoneText = "OK！準備完了。";

    [Header("前段チュートリアル：しきい値")]
    public float BasicLookYawTotal = 20f;
    public float BasicLookPitchTotal = 10f;
    public float BasicMoveMinDuration = 0.15f;
    public float BasicDashMinDuration = 0.15f;

    public float BasicMoveTotalDistanceRequired = 1.5f; // XZ合計[m]
    public bool BasicMoveCountOnlyWhenInput = true;
    public float BasicMoveMaxStepPerFrame = 2.0f;

    private bool _basicRunning = false;
    private bool _basicDone = false;
    private Quaternion _basicPrevCamRot;
    private float _basicAccYaw = 0f;
    private float _basicAccPitch = 0f;
    private Vector3 _basicMovePrevPos;
    private float _basicMoveTotal = 0f;

    // 「前段」コルーチンの取っ手（スキップ時に止めるため）
    private Coroutine _basicRoutine = null;

    // Attack×3 スキップ
    [Header("前段スキップ（Attack×3）")]
    public int AttackSkipThreshold = 3;
    public float AttackSkipWindow = 2.0f;  // N秒以内に3回
    private int _attackSkipCounter = 0;
    private float _attackSkipTimer = 0f;
    private bool _skipRequested = false;

    // ========== ポーズ時のオーディオ制御 ==========
    [Header("ポーズ時のオーディオ制御")]
    public bool PauseAudioWhilePanel = true;
    private bool _prevListenerPause = false;

    // ========== Hint 連携：キュー ==========
    private readonly Queue<string[]> _queuedHintTutorials = new Queue<string[]>();

    // ========== ドア用ミッション（別テキストUI） ==========
    [Header("ドア用ミッション（別テキストUI）")]
    public bool EnableDoorMission = true;
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

    // ========== ライトの表示タイミング ==========
    [Header("チュートリアル中は非表示にするライト")]
    public List<GameObject> LightsToToggle = new List<GameObject>();
    public bool HideLightsUntilMission3 = true;
    private bool _lightsActivatedAfterM3 = false;

    // ===== 共通ゲート =====
    private bool IsEventAllowed() => !EnableBasicTutorial || _basicDone;

    // ===== 型内ユーティリティ =====
    private void SkipCurrentTyping()
    {
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (BottomText) BottomText.gameObject.SetActive(false);
    }

    // ========== ライフサイクル ==========
    private void Awake()
    {
        // 参照の自動検索（新API）
        if (!HintRef && AutoFindHintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        // Hint 連携イベント
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);

            // 全文開示トリガ（stateX.elementY）
            HintRef.OnLineFullyRevealed.AddListener(OnHintAllRevealed);

            // Hint 側からの台詞行（HintText.TutorialLinesOnFullyRevealed）
            HintRef.OnHintTutorialLinesRequested.AddListener(OnHintTutorialLinesRequested);
        }

        // 隠れ案内
        if (HideRef) HideRef.OnFirstHidePromptShown.AddListener(ShowHidePanelOnce);

        // 前段チュートリアル開始（セーブ無しなので毎回ここで判定）
        if (EnableBasicTutorial && _basicRoutine == null)
            _basicRoutine = StartCoroutine(CoRunBasicTutorial());

        // ドア用ミッション開始（独立表示）
        if (EnableDoorMission) StartDoorMissionIfNeeded();

        // スポーナーの生成イベントで Step3Lines を一度だけ
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
            HintRef.OnLineFullyRevealed.RemoveListener(OnHintAllRevealed);
            HintRef.OnHintTutorialLinesRequested.RemoveListener(OnHintTutorialLinesRequested);
        }
        if (HideRef) HideRef.OnFirstHidePromptShown.RemoveListener(ShowHidePanelOnce);

        if (_input != null)
        {
            _input.Player.Disable();
            _input.UI.Disable();
        }

        if (Time.timeScale == 0f) Time.timeScale = 1f; // 念のため

        if (Spawners != null)
        {
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].OnGhostSpawned.RemoveListener(OnAnyGhostSpawned_FirstTime);
        }
    }

    // ★リーク対策（破棄時も確実に開放）
    private void OnDestroy()
    {
        try
        {
            if (_input != null)
            {
                _input.Player.Disable();
                _input.UI.Disable();
                _input.Dispose();
                _input = null;
            }
        }
        catch { }
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);
        if (HidePanel) HidePanel.SetActive(false);

        // 念のためスポナーは止めておく（AutoStart=false 前提でも保険）
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
        Step1();

        // Hint からのキューがあればこのタイミングで処理
        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        // ミッション初期化
        if (MissionText) { MissionText.text = ""; MissionText.gameObject.SetActive(false); }

        // ライトOFF（ミッション3まで）
        if (HideLightsUntilMission3 && LightsToToggle != null)
        {
            for (int i = 0; i < LightsToToggle.Count; i++)
                if (LightsToToggle[i]) LightsToToggle[i].SetActive(false);
        }
    }

    private void Update()
    {
        // 参照が切れてたら取り直し（リトライ安全化）
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            Player = p ? p.transform : null;
        }

        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // パネル中は抑止

        // ミッション3中：声を聞いたあとに有効ドアで完了
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext && !_pauseGate && IsEventAllowed())
        {
            TryCompleteDoorMissionByEnabledDoorInteract();
        }

        // --- Attack×3 で前段だけスキップ ---
        if (EnableBasicTutorial && !_basicDone)
        {
            if (_input.Player.Attack.WasPressedThisFrame())
            {
                if (_attackSkipTimer <= 0f) { _attackSkipCounter = 0; }
                _attackSkipCounter++;
                _attackSkipTimer = AttackSkipWindow;

                if (_attackSkipCounter >= AttackSkipThreshold)
                {
                    StartSkipBasicOnly();
                }
            }
            if (_attackSkipTimer > 0f) _attackSkipTimer -= Time.deltaTime;
        }
    }

    // ========== Step1/Step2/Step3 ==========
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        if (_muteBottomTyping) return; // スキップ直後の点滅防止
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

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

            // Step1 のタイプ演出などが走っていればスキップ
            SkipCurrentTyping();

            // Step2：ロック文言（OneShot）
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            // ミッション：ステージ1達成
            if (EnableDoorMission && _doorMission == DoorMissionStage.DoorCheck)
                AdvanceDoorMissionTo(DoorMissionStage.FindGhost);

            // Step3 を予約（初回だけ）
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
        // 「ドアはあかない…」のOneShotが消えるまで待つ（HideWhenDone=true前提）
        while (BottomText && BottomText.gameObject.activeSelf) yield return null;

        // 少し間を置く
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);

        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;

        if (!IsEventAllowed()) return;

        // 1) 抽選開始
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].BeginSpawning();

        // 2) Step3Lines は「初回のスポーンで」出す（OnAnyGhostSpawned_FirstTime）
    }

    private void OnAnyGhostSpawned_FirstTime()
    {
        if (!IsEventAllowed()) return;
        if (_step3TextShown) return;
        _step3TextShown = true;

        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            if (_typing != null) StopCoroutine(_typing);
            if (BottomText && !_muteBottomTyping)
            {
                BottomText.gameObject.SetActive(true);
                _typing = StartCoroutine(CoTypeLines(Step3Lines));
            }
        }
    }

    // ========== Step4/5：初見パネル ==========
    public void Step4_ShowPanel()
    {
        if (!IsEventAllowed()) return;
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;

        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));

        // ミッション：ステージ2達成
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

    // ========== パネル表示→一時停止→Submitで閉じる ==========
    private IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        panel.SetActive(true);

        // ★Audio を保存→停止
        _prevListenerPause = AudioListener.pause;
        if (PauseAudioWhilePanel) AudioListener.pause = true;

        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        // 1フレーム待って入力待ち
        yield return null;
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        panel.SetActive(false);

        // ★Audio を元へ
        if (PauseAudioWhilePanel) AudioListener.pause = _prevListenerPause;

        Time.timeScale = prevScale;
        _pauseGate = false;

        // 保留分があれば出す
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

        bool anyJustEnabled = false;
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            bool wasEnabled = od.enabled;
            if (od.enabled != enableDoor) od.enabled = enableDoor;
            if (!wasEnabled && enableDoor) anyJustEnabled = true;
        }

        // いまロック解除された → 一度だけテキスト（パネル中は出さない）
        if (enableDoor && anyJustEnabled && !_didAnnounceDoorUnlocked && !_pauseGate)
        {
            ShowOneShot(string.IsNullOrEmpty(DoorUnlockedMessage) ? "ドアが開いたようだ" : DoorUnlockedMessage);
            _didAnnounceDoorUnlocked = true;
        }
    }

    private void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // ========== Hint 連携：全部開示トリガ ==========
    private void OnHintAllRevealed(string id)
    {
        if (id == "state1.element0")
        {
            // ShowOneShot("……気配が近い。慎重に。");
            return;
        }
    }

    // ========== Hint 連携：外部から渡された行（キュー処理） ==========
    private void OnHintTutorialLinesRequested(string[] lines)
    {
        if (!HasAnyContent(lines)) return;

        // パネル中 or 前段未完了 or 既にタイプ中 → キューへ
        if (!IsEventAllowed() || _pauseGate || _typing != null || _muteBottomTyping)
        {
            _queuedHintTutorials.Enqueue(DuplicateLines(lines));
            return;
        }

        ShowHintTutorialLinesNow(lines);
    }

    private void ShowHintTutorialLinesNow(string[] lines)
    {
        if (!BottomText || _muteBottomTyping) return;

        var copy = DuplicateLines(lines);
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(copy));
    }

    private string[] DuplicateLines(string[] source)
    {
        if (source == null || source.Length == 0) return Array.Empty<string>();
        var copy = new string[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = source[i];
        return copy;
    }

    private bool HasAnyContent(string[] lines)
    {
        if (lines == null) return false;
        for (int i = 0; i < lines.Length; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) return true;
        return false;
    }

    // ========== ミッション ==========
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
        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame();

        if (!pressed || !Player) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (!od.enabled) continue;

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

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

    private void ActivateLightsAfterMission3()
    {
        if (_lightsActivatedAfterM3) return;
        _lightsActivatedAfterM3 = true;

        if (LightsToToggle == null) return;
        for (int i = 0; i < LightsToToggle.Count; i++)
            if (LightsToToggle[i]) LightsToToggle[i].SetActive(true);
    }

    // ========== テキスト演出（メイン） ==========
    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line) || _muteBottomTyping) return;
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
        if (_muteBottomTyping) yield break;
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            if (li < lines.Length - 1) yield return new WaitForSeconds(LineInterval);
        }
        if (HideWhenDone && BottomText) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeOne(string text)
    {
        if (_muteBottomTyping) yield break;
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }
        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length && !_muteBottomTyping)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length && !_muteBottomTyping)
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
        if (_basicRunning || _basicDone) { _basicRoutine = null; yield break; }
        if (!BottomText) { _basicRoutine = null; yield break; }

        _basicRunning = true;

        if (CameraTransform) _basicPrevCamRot = CameraTransform.rotation;

        yield return null; // Start() 後に

        // もし途中でスキップされたら即終了
        if (_basicDone) { _basicRunning = false; _basicRoutine = null; yield break; }

        // ---- 移動 ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping)
        {
            BottomText.gameObject.SetActive(true);
            yield return StartCoroutine(CoTypeOne(BasicMoveText));
        }

        _basicMoveTotal = 0f;
        _basicMovePrevPos = Player ? Player.position : Vector3.zero;

        float moveTimer = 0f;
        while (!_basicDone) // スキップされたら抜ける
        {
            bool moving = PlayerCtrl ? PlayerCtrl.IsMovingNow : (_input.Player.Move.ReadValue<Vector2>() != Vector2.zero);

            if (Player)
            {
                Vector3 cur = Player.position;
                Vector3 delta = cur - _basicMovePrevPos; delta.y = 0f;
                float step = Mathf.Min(delta.magnitude, BasicMoveMaxStepPerFrame);
                if (!BasicMoveCountOnlyWhenInput || moving) _basicMoveTotal += step;
                _basicMovePrevPos = cur;
            }

            if (moving) moveTimer += Time.deltaTime;
            else moveTimer = 0f;

            if (moveTimer >= BasicMoveMinDuration && _basicMoveTotal >= BasicMoveTotalDistanceRequired)
                break;

            yield return null;
        }
        if (_basicDone) { _basicRunning = false; _basicRoutine = null; yield break; }

        // ---- 視点 ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping)
        {
            BottomText.gameObject.SetActive(true);
            yield return StartCoroutine(CoTypeOne(BasicLookText));
        }

        _basicAccYaw = 0f; _basicAccPitch = 0f;
        while (!_basicDone)
        {
            if (CameraTransform)
            {
                Quaternion cur = CameraTransform.rotation;

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
        if (_basicDone) { _basicRunning = false; _basicRoutine = null; yield break; }

        // ---- ダッシュ ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping)
        {
            BottomText.gameObject.SetActive(true);
            yield return StartCoroutine(CoTypeOne(BasicDashText));
        }

        float dashTimer = 0f;
        float decayPerSec = 0.5f;
        while (!_basicDone)
        {
            bool dashing = PlayerCtrl ? PlayerCtrl.IsDashingNow : false;

            if (dashing) dashTimer += Time.deltaTime;
            else dashTimer = Mathf.Max(0f, dashTimer - Time.deltaTime * decayPerSec);

            if (dashTimer >= BasicDashMinDuration) break;
            yield return null;
        }
        if (_basicDone) { _basicRunning = false; _basicRoutine = null; yield break; }

        // 完了表示
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping)
        {
            BottomText.gameObject.SetActive(true);
            BottomText.text = BasicDoneText;
        }

        _basicDone = true;
        _basicRunning = false;
        _basicRoutine = null;

        // 保留されていた案内をここで
        if (_pendingHidePanel)
        {
            _pendingHidePanel = false;
            if (!_pauseGate && !_didHidePanel && HidePanel)
            {
                _didHidePanel = true;
                StartCoroutine(CoShowPausePanel(HidePanel));
            }
        }

        // 本編へ
        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }

    // ====== Attack×3 で「前段だけ」スキップ ======
    private void StartSkipBasicOnly()
    {
        if (_skipRequested) return;
        _skipRequested = true;

        // 前段コルーチンを確実に停止
        if (_basicRoutine != null)
        {
            StopCoroutine(_basicRoutine);
            _basicRoutine = null;
        }

        StartCoroutine(CoSkipBasicOnly());
    }

    private IEnumerator CoSkipBasicOnly()
    {
        // テキスト点滅防止：一時的にミュートして消す
        _muteBottomTyping = true;
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }

        // 前段フラグを完了へ（前段のみスキップ）
        _basicDone = true;
        _basicRunning = false;
        EnableBasicTutorial = false;

        // 保留パネルだけ処理
        if (_pendingHidePanel && !_pauseGate && !_didHidePanel && HidePanel)
        {
            _pendingHidePanel = false;
            _didHidePanel = true;
            StartCoroutine(CoShowPausePanel(HidePanel));
        }

        // 1フレームだけ待ってからミュート解除（点滅防止用のワンテンポ）
        yield return null;
        _muteBottomTyping = false;

        // ★ここで初めて本編の表示をトリガ（ミュート解除後！）
        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();

        // キューに貯まってた台詞があれば流す
        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        // カウンタもリセット
        _attackSkipCounter = 0;
        _attackSkipTimer = 0f;
        _skipRequested = false;
    }

}
