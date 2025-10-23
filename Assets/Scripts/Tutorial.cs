using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ========== メインテキスト ==========
    [Header("メインテキスト")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "……ここはどこだろう。", "さっきまでの記憶が曖昧だ。", "とにかく、出口を探さないと。" };
    [TextArea] public string[] Step3Lines = { "……何か音がしたぞ！", "周りを探してみよう。" };
    private Coroutine _typing;

    // ========== 解錠/施錠メッセージ ==========
    [Header("ロック/ロック解除メッセージ")]
    [TextArea] public string DoorLockedMessage = "ドアはあかないようだ…";
    [TextArea] public string DoorUnlockedMessage = "ドアが開いたようだ";
    private bool _didAnnounceDoorUnlocked = false;

    // ========== 「チュートリアルをスキップ」表示 ==========
    [Header("チュートリアル中だけ出すスキップ表示")]
    public TextMeshProUGUI SkipTutorialText;   // 別のTMPをアタッチ

    // ========== 進行度/連携 ==========
    [Header("進行度参照")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    [Header("制御対象（OpenDoor のみ）")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ========== ドア：ロック時の入力フック ==========
    [Header("ドア：ロック時の入力フック")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.0f;
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ========== 初見パネル ==========
    [Header("初見チュートリアル画像")]
    public GameObject Step4Panel_StateAny;
    public GameObject Step5Panel_State2;
    private bool _didStep4 = false;
    private bool _didStep5 = false;

    [Header("隠れるチュートリアル画像")]
    public HideCroset HideRef;
    public GameObject HidePanel;
    private bool _didHidePanel = false;
    private bool _pendingHidePanel = false;

    private bool _pauseGate = false;

    // ========== スポーン/Step3 ==========
    [Header("幽霊スポナー（EnemyAI）")]
    public List<EnemyAI> Spawners = new();
    public float StartSpawnDelayAfterStep2 = 2f;
    private bool _didStep2 = false;
    private bool _didStep3 = false;
    private bool _step3TextShown = false;

    // ========== 前段チュートリアル ==========
    [Header("前段チュートリアル（移動／視点／ダッシュ）")]
    public bool EnableBasicTutorial = true;        // 前段のみ管理
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

    // Attack×3で前段のみスキップ
    [Header("スキップ入力（Attack）")]
    public int AttackSkipPresses = 3;
    public float AttackSkipWindow = 1.2f;
    private int _attackSkipCounter = 0;
    private float _attackSkipTimer = 0f;
    private bool _skipRequested = false;

    // 点滅抑止
    private bool _muteBottomTyping = false;

    // ========== ポーズ時のオーディオ ==========
    [Header("ポーズ時のオーディオ制御")]
    public bool PauseAudioWhilePanel = true;
    private bool _prevListenerPause = false;

    // ========== Hint 連携：キュー ==========
    private readonly Queue<string[]> _queuedHintTutorials = new Queue<string[]>();

    // ========== ドア用ミッション ==========
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
    private bool _heardVoice = false;
    private bool _lightsActivatedAfterM3 = false;

    // ===== 共通ゲート =====
    private bool IsEventAllowed() => !EnableBasicTutorial || _basicDone;

    // ===== ユーティリティ =====
    private void SkipCurrentTyping()
    {
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (BottomText) BottomText.gameObject.SetActive(false);
    }
    private void SetSkipHintVisible(bool v)
    {
        if (SkipTutorialText)
            SkipTutorialText.gameObject.SetActive(v);
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
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);
            HintRef.OnLineFullyRevealed.AddListener(OnHintAllRevealed);
            HintRef.OnHintTutorialLinesRequested.AddListener(OnHintTutorialLinesRequested);
        }

        if (HideRef) HideRef.OnFirstHidePromptShown.AddListener(ShowHidePanelOnce);

        if (EnableBasicTutorial)
        {
            SetSkipHintVisible(true);
            StartCoroutine(CoRunBasicTutorial());
        }

        if (EnableDoorMission) StartDoorMissionIfNeeded();

        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].OnGhostSpawned.AddListener(OnAnyGhostSpawned_FirstTime);
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

        if (Time.timeScale == 0f) Time.timeScale = 1f;

        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].OnGhostSpawned.RemoveListener(OnAnyGhostSpawned_FirstTime);

        SetSkipHintVisible(false);
    }

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

        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        SetSkipHintVisible(EnableBasicTutorial && !_basicDone);

        Step1();

        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        if (MissionText) { MissionText.text = ""; MissionText.gameObject.SetActive(false); }
    }

    private void Update()
    {
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            Player = p ? p.transform : null;
        }

        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback();

        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext && !_pauseGate && IsEventAllowed())
            TryCompleteDoorMissionByEnabledDoorInteract();

        // Attack×3 で前段のみスキップ
        if (EnableBasicTutorial && !_basicDone && _input != null)
        {
            if (_input.Player.Attack.WasPressedThisFrame())
            {
                _attackSkipCounter++;
                _attackSkipTimer = AttackSkipWindow;

                if (_attackSkipCounter >= AttackSkipPresses && !_skipRequested)
                {
                    _skipRequested = true;
                    StartCoroutine(CoSkipBasicOnly());
                }
            }
            if (_attackSkipTimer > 0f) _attackSkipTimer -= Time.deltaTime;
            else _attackSkipCounter = 0;
        }
    }

    // ========== Step1/Step2/Step3 ==========
    public void Step1()
    {
        if (!BottomText || _muteBottomTyping) return;
        if (_typing != null) StopCoroutine(_typing);
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
            if (od.enabled) continue;
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            SkipCurrentTyping();
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            if (EnableDoorMission && _doorMission == DoorMissionStage.DoorCheck)
                AdvanceDoorMissionTo(DoorMissionStage.FindGhost);

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
        while (BottomText && BottomText.gameObject.activeSelf) yield return null;
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;
        if (!IsEventAllowed()) return;

        if (Spawners != null)
            for (int i = 0; i < Spawners.Count; i++)
                if (Spawners[i]) Spawners[i].BeginSpawning();
    }

    private void OnAnyGhostSpawned_FirstTime()
    {
        if (!IsEventAllowed() || _step3TextShown) return;
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

    // ========== 初見パネル ==========
    public void Step4_ShowPanel()
    {
        if (!IsEventAllowed() || _didStep4 || _pauseGate) return;
        _didStep4 = true;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));
        if (EnableDoorMission && _doorMission == DoorMissionStage.FindGhost)
            AdvanceDoorMissionTo(DoorMissionStage.HearVoiceGoNext);
    }

    public void Step5_ShowPanel()
    {
        if (!IsEventAllowed() || _didStep5 || _pauseGate) return;
        _didStep5 = true;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));
        _heardVoice = true;
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext)
            ShowMissionText(Mission_HearVoiceGoNext);
    }

    public void ShowHidePanelOnce()
    {
        if (_didHidePanel) return;
        if (_pauseGate || !IsEventAllowed()) { _pendingHidePanel = true; return; }
        _didHidePanel = true;
        StartCoroutine(CoShowPausePanel(HidePanel));
    }

    private IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;
        _pauseGate = true;

        panel.SetActive(true);
        _prevListenerPause = AudioListener.pause;
        if (PauseAudioWhilePanel) AudioListener.pause = true;

        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        yield return null;
        while (!_input.UI.Submit.WasPressedThisFrame()) yield return null;

        panel.SetActive(false);
        if (PauseAudioWhilePanel) AudioListener.pause = _prevListenerPause;

        Time.timeScale = prevScale;
        _pauseGate = false;

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

    private void OnHintAllRevealed(string id)
    {
        if (id == "state1.element0") { /*必要なら台詞*/ return; }
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

    // ========== ミッションUI ==========
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
                ShowMissionText(Mission_FindGhost); break;
            case DoorMissionStage.HearVoiceGoNext:
                ShowMissionText(Mission_HearVoiceGoNext);
                if (!_lightsActivatedAfterM3) ActivateLightsAfterMission3();
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
        // 必要ならライトON処理
    }

    // ========== テキスト演出 ==========
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
        if (BottomText) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines)
    {
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
        if (!BottomText) yield break;
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }
        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ========== 前段：本体 ==========
    private IEnumerator CoRunBasicTutorial()
    {
        if (_basicRunning || _basicDone) yield break;
        if (!BottomText) yield break;

        _basicRunning = true;
        SetSkipHintVisible(true);

        if (CameraTransform) _basicPrevCamRot = CameraTransform.rotation;
        yield return null;

        // 移動
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping) { BottomText.gameObject.SetActive(true); yield return StartCoroutine(CoTypeOne(BasicMoveText)); }

        _basicMoveTotal = 0f;
        _basicMovePrevPos = Player ? Player.position : Vector3.zero;
        float moveTimer = 0f;
        while (true)
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

            if (moving) moveTimer += Time.deltaTime; else moveTimer = 0f;
            if (moveTimer >= BasicMoveMinDuration && _basicMoveTotal >= BasicMoveTotalDistanceRequired) break;
            yield return null;
        }

        // 視点
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping) { BottomText.gameObject.SetActive(true); yield return StartCoroutine(CoTypeOne(BasicLookText)); }

        _basicAccYaw = 0f; _basicAccPitch = 0f;
        while (true)
        {
            if (CameraTransform)
            {
                Quaternion cur = CameraTransform.rotation;
                Vector3 fPrev = _basicPrevCamRot * Vector3.forward;
                Vector3 fCur = cur * Vector3.forward;

                float yawPrev = Mathf.Atan2(fPrev.x, fPrev.z) * Mathf.Rad2Deg;
                float yawCur = Mathf.Atan2(fCur.x, fCur.z) * Mathf.Rad2Deg;
                float dyaw = Mathf.DeltaAngle(yawPrev, yawCur); _basicAccYaw += Mathf.Abs(dyaw);

                float pitchPrev = Mathf.Asin(Mathf.Clamp(fPrev.y, -1f, 1f)) * Mathf.Rad2Deg;
                float pitchCur = Mathf.Asin(Mathf.Clamp(fCur.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dpitch = Mathf.DeltaAngle(pitchPrev, pitchCur); _basicAccPitch += Mathf.Abs(dpitch);

                _basicPrevCamRot = cur;
                if (_basicAccYaw >= BasicLookYawTotal && _basicAccPitch >= BasicLookPitchTotal) break;
            }
            yield return null;
        }

        // ダッシュ
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (!_muteBottomTyping) { BottomText.gameObject.SetActive(true); yield return StartCoroutine(CoTypeOne(BasicDashText)); }

        float dashTimer = 0f;
        float decayPerSec = 0.5f;
        while (true)
        {
            bool dashing = PlayerCtrl ? PlayerCtrl.IsDashingNow : false;
            if (dashing) dashTimer += Time.deltaTime; else dashTimer = Mathf.Max(0f, dashTimer - Time.deltaTime * decayPerSec);
            if (dashTimer >= BasicDashMinDuration) break;
            yield return null;
        }

        // 完了表示
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (BottomText && !_muteBottomTyping) { BottomText.gameObject.SetActive(true); BottomText.text = BasicDoneText; }

        _basicDone = true;
        _basicRunning = false;

        SetSkipHintVisible(false);

        if (_pendingHidePanel && !_pauseGate && !_didHidePanel && HidePanel)
        {
            _pendingHidePanel = false;
            _didHidePanel = true;
            StartCoroutine(CoShowPausePanel(HidePanel));
        }

        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }

    // ========== 前段のみスキップ ==========
    private IEnumerator CoSkipBasicOnly()
    {
        _muteBottomTyping = true;
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }

        _basicDone = true;
        _basicRunning = false;
        EnableBasicTutorial = false;

        SetSkipHintVisible(false);

        if (_pendingHidePanel && !_pauseGate && !_didHidePanel && HidePanel)
        {
            _pendingHidePanel = false;
            _didHidePanel = true;
            StartCoroutine(CoShowPausePanel(HidePanel));
        }

        yield return null; // 1フレ待ちで点滅抑止
        _muteBottomTyping = false;

        Step1();
        if (EnableDoorMission) StartDoorMissionIfNeeded();

        if (_queuedHintTutorials.Count > 0 && !_pauseGate && IsEventAllowed() && _typing == null)
        {
            var pending = _queuedHintTutorials.Dequeue();
            ShowHintTutorialLinesNow(pending);
        }

        _attackSkipCounter = 0;
        _attackSkipTimer = 0f;
        _skipRequested = false;
    }
}
