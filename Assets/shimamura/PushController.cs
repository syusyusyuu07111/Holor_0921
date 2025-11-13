// PushController.cs (Trace付き 完全版 + インスペクタ調整可能な整列版)
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[DisallowMultipleComponent]
public class PushController : MonoBehaviour
{
    [Header("Detection / Physics")]
    [SerializeField] private Transform _rayOrigin;
    [SerializeField] private LayerMask _detectMask;
    [SerializeField] private string _chairTag = "Chair";
    [SerializeField] private float _pushDistance = 2.5f;

    [Header("Push (Left Click)")]
    [SerializeField] private float _pushSpeed = 1.5f;
    [SerializeField] private InputActionReference _pushActionRef;

    [Header("Climb (Right Click / DoorOpen)")]
    [SerializeField] private Animator _playerAnimator;
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private Transform _raiseTarget;

    [Header("Climb Animation")]
    [SerializeField] private string _climbBoolName = "IsClimbing";
    [SerializeField] private string _climbTag = "Climb";
    [SerializeField] private string _climbStateName = "Climb";
    [SerializeField] private int _climbLayer = 0;
    [SerializeField] private float _crossFadeDur = 0.15f;
    [SerializeField] private float _fallbackAnimTime = 1.0f;

    [Header("Timed Raise (非RootMotion時のみ)")]
    [SerializeField] private bool _snapToChairTop = true;
    [SerializeField] private float _chairTopOffset = 0.25f;
    [SerializeField] private float _yRaiseTime = 0.6f;
    [SerializeField] private float _yRaiseAdd = 0.5f;

    [Header("Climb Align")]
    [Tooltip("椅子ローカル座標でのプレイヤー待機位置 (x:右, y:上, z:前)")]
    [SerializeField] private Vector3 _climbLocalOffset = new Vector3(0f, 0f, -0.5f);

    [Tooltip("椅子の向きに対するプレイヤーのヨー角オフセット (deg)")]
    [SerializeField] private float _climbYawOffsetDeg = 0f;

    [Header("Hold After Climb")]
    [SerializeField] private float _holdAfterClimbSec = 2.0f;

    [Header("Root Motion")]
    [SerializeField] private bool _useRootMotionForClimb = true;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    [Header("ForceBoneScale (任意)")]
    [SerializeField] private ForceBoneScale _fbsRef;

    [Header("Debug")]
    [SerializeField] private bool _debugLogging = true;
    [SerializeField] private bool _logMinimal = true;

    [Header("Trace")]
    [Tooltip("降り開始後、詳細スナップを何フレーム出すか")]
    [SerializeField] private int traceAfterDescendFrames = 3;

    private const string LOG = "[CLIMB] ";
    private enum MountState { Grounded, Climbing, Mounted, Descending }
    [SerializeField] private float _inputCooldownSec = 0.12f;

    private MountState _state = MountState.Grounded;
    private bool _canAcceptToggle = true;
    private bool _toggleLatched = false;
    private float _nextAcceptTime = 0f;

    // Inputs
    private InputSystem_Actions _inputs;
    private InputAction _doorOpen;
    private InputAction _rightClick;
    private InputAction _pushAction;
    private InputAction _interact;

    // Push
    private Rigidbody _pushingRb;
    private Transform _pushingTransform;
    private Vector3 _pushDir;
    private bool _isPushing;
    private bool _originalKinematic;

    // Climb/Descend
    private bool _isClimbing;
    private float _elapsedClimb;
    private bool _yRaised;

    private bool _isDescending;
    private Coroutine _descendCo;

    // Pre-climb root pose
    private Vector3 _preClimbRootPos;
    private Quaternion _preClimbRootRot;
    private bool _hasPreClimbRootTR;

    // Pre-climb ground Y
    private float _preClimbFeetGroundY;
    private bool _hasPreClimbFeetGroundY;

    // RayOrigin snapshot
    private Transform _snapRayParent;
    private Vector3 _snapRayLocalPos;
    private Quaternion _snapRayLocalRot;
    private Vector3 _snapRayLocalScale;
    private bool _hasSnapRay;

    // Animator / PlayerController / CC/NMA snapshot
    private bool _snapAnimatorApplyRM;
    private bool _hasSnapAnimatorRM;
    private bool _snapPCEnabled;
    private bool _hasSnapPCEnabled;
    private bool _snapCCEnabled;
    private bool _snapNMAEnabled;

    // Chair top hold
    private bool _holdRaisedPos;
    private float _holdTimer;
    private Vector3 _lockedRaisedPos;

    // Wrapper
    private Transform _wrapper;

    // FBS
    private ForceBoneScale _fbs;

    private int _climbBoolHash;
    private int _climbStateHash;

    // ===== Logging helpers =====
    private void Log(string msg)
    {
        if (!_debugLogging) return;
        if (_logMinimal)
        {
            if (!(msg.StartsWith("STATE") || msg.StartsWith("ClimbStart") || msg.StartsWith("ClimbEnd") ||
                  msg.StartsWith("DescendEnd") || msg.StartsWith("Interact") || msg.StartsWith("FBS") ||
                  msg.StartsWith("PreClimb") || msg.StartsWith("Restore") || msg.StartsWith("TRACE")))
                return;
        }
        Debug.Log($"{LOG}f#{Time.frameCount} t={Time.time:0.000} {msg}");
    }
    private void Warn(string msg) => Debug.LogWarning($"{LOG}{msg}");
    private void Err(string msg) => Debug.LogError($"{LOG}{msg}");

    private void DumpState(string tag)
    {
        if (!_debugLogging) return;
        string rm = _playerAnimator ? (_playerAnimator.applyRootMotion ? "RM=ON" : "RM=OFF") : "RM=?";
        string climbB = (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator) ? _playerAnimator.GetBool(_climbBoolHash).ToString() : "?";
        string fbs = _fbs
            ? $"FBS(YFree={_fbs.IsYFree},HardLock={_fbs.HardLockFramesLeft},Base={(_fbs.HasGroundBase ? _fbs.FeetGroundY.ToString("F3") : "NA")})"
            : "FBS=?";
        Debug.Log($"{LOG}STATE {tag} f#{Time.frameCount} {_state} can={_canAcceptToggle} latched={_toggleLatched} nextAccept={_nextAcceptTime:0.00} isClimb={_isClimbing} isDesc={_isDescending} {rm} IsClimbingBool={climbB} {fbs}");
    }

    private void Awake()
    {
        if (_playerAnimator == null) Err("Animator is null.");
        if (_raiseTarget == null && _playerAnimator != null) _raiseTarget = _playerAnimator.transform;

        CreateWrapperIfNeeded(initialSetup: true);
        RefreshFBS("Awake");

        if (_fbs != null)
        {
            _fbs.bypassFramesAfterRebase = 0;
            _fbs.rebaseOnClimbEnd = false;
            _fbs.lockFramesAfterExternalSnap = 2;
        }

        if (!string.IsNullOrEmpty(_climbBoolName)) _climbBoolHash = Animator.StringToHash(_climbBoolName);
        if (!string.IsNullOrEmpty(_climbStateName)) _climbStateHash = Animator.StringToHash(_climbStateName);

        DumpState("Awake");
    }

    private void OnEnable()
    {
        if (_pushActionRef != null)
        {
            _pushAction = _pushActionRef.action;
            _pushAction.Enable();
            _pushAction.performed += OnPushPressed;
            _pushAction.canceled += OnPushReleased;
        }

        if (_inputs == null) _inputs = new InputSystem_Actions();
        _inputs.Enable();

        _doorOpen = _inputs.Player.DoorOpen; _doorOpen.Enable(); _doorOpen.performed += OnClimbPressed;

        _rightClick = new InputAction(type: InputActionType.Button, binding: "<Mouse>/rightButton");
        _rightClick.Enable();
        _rightClick.performed += OnClimbPressed;

        _interact = _inputs.Player.Interact;
        if (_interact != null)
        {
            _interact.Enable();
            _interact.started += OnDescendPressed;
            if (_interact.bindings.Count > 0)
            {
                var b = _interact.bindings[0];
                _interact.ApplyBindingOverride(0, new InputBinding { path = b.path, interactions = "Press(behavior=1)" });
            }
        }

        DumpState("OnEnable");
    }

    private void OnDisable()
    {
        if (_pushAction != null) { _pushAction.performed -= OnPushPressed; _pushAction.canceled -= OnPushReleased; _pushAction.Disable(); }
        if (_doorOpen != null) { _doorOpen.performed -= OnClimbPressed; _doorOpen.Disable(); }
        if (_rightClick != null) { _rightClick.performed -= OnClimbPressed; _rightClick.Disable(); }
        if (_interact != null)
        {
            _interact.started -= OnDescendPressed;
            if (_interact.bindings.Count > 0) _interact.RemoveBindingOverride(0);
            _interact.Disable();
        }
        if (_isPushing) OnPushReleased(default);
        if (_inputs != null) _inputs.Disable();
    }

    private string _lastUiText = null;

    private void Update()
    {
        if (_isClimbing) return;

        if (_isPushing && _pushingTransform != null)
            _pushingTransform.position += _pushDir * _pushSpeed * Time.deltaTime;

        if (_rayOrigin == null) return;

        // UI
        if (_pushTextMeshPro != null)
        {
            string nextText = "";
            if (_state == MountState.Grounded)
            {
                Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
                bool hasHit = Physics.Raycast(ray, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore);
                nextText = hasHit ? "左クリック:押す / 右クリックorDoorOpen:乗る" : "";
            }
            else if (_state == MountState.Mounted)
            {
                nextText = "Interact: 降りる";
            }
            if (_lastUiText != nextText)
            {
                _pushTextMeshPro.SetText(nextText);
                _lastUiText = nextText;
            }
        }
    }

    private void LateUpdate()
    {
        // unlatch
        if (_rightClick != null && !_rightClick.IsPressed()) _toggleLatched = false;
        if (_doorOpen != null && !_doorOpen.IsPressed()) _toggleLatched = false;

        // chair hold
        if (!_isClimbing && _holdRaisedPos && _wrapper != null)
        {
            _wrapper.position = _lockedRaisedPos;
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= _holdAfterClimbSec) _holdRaisedPos = false;
        }
    }

    // Push
    private void OnPushPressed(InputAction.CallbackContext _)
    {
        if (_isClimbing || _rayOrigin == null) return;

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
        {
            Transform chairRoot = ResolveChairRoot(hitInfo);
            if (chairRoot != null)
            {
                _pushingRb = chairRoot.GetComponent<Rigidbody>();
                _pushingTransform = chairRoot;

                if (_pushingRb != null)
                {
                    _originalKinematic = _pushingRb.isKinematic;
                    _pushingRb.isKinematic = true;
                }

                _pushDir = _rayOrigin.forward.normalized;
                _isPushing = true;
            }
        }
    }

    private void OnPushReleased(InputAction.CallbackContext _)
    {
        if (_pushingRb != null) _pushingRb.isKinematic = _originalKinematic;
        _pushingRb = null;
        _pushingTransform = null;
        _isPushing = false;
    }

    private void TryUpdatePush(RaycastHit hitInfo)
    {
        if (_pushingTransform == null) return;
        Transform root = ResolveChairRoot(hitInfo);
        if (root != _pushingTransform)
        {
            if (_pushingRb) _pushingRb.isKinematic = _originalKinematic;
            _pushingRb = null;
            _pushingTransform = null;
            _isPushing = false;
        }
    }

    // Climb
    private void OnClimbPressed(InputAction.CallbackContext ctx)
    {
        string reason;
        if (!CanStartClimbGate(out reason))
        {
            Log("Climb rejected: " + reason);
            return;
        }
        TryStartClimb(ctx);
    }

    private bool CanStartClimbGate(out string reason)
    {
        if (_playerAnimator == null || _rayOrigin == null) { reason = "Animator or RayOrigin is null"; return false; }
        if (_toggleLatched) { reason = "Latched"; return false; }
        if (Time.time < _nextAcceptTime) { reason = "Cooldown"; return false; }
        if (!_canAcceptToggle) { reason = "Global gate closed"; return false; }
        if (_playerAnimator.IsInTransition(_climbLayer)) { reason = "Animator in transition"; return false; }
        if (_state != MountState.Grounded) { reason = $"State != Grounded ({_state})"; return false; }

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (!Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
        { reason = "Ray miss"; return false; }
        if (ResolveChairRoot(hitInfo) == null)
        { reason = "Ray hit but no Chair in parents"; return false; }

        reason = "OK"; return true;
    }

    private void TryStartClimb(InputAction.CallbackContext ctx)
    {
        _toggleLatched = true;
        _nextAcceptTime = Time.time + _inputCooldownSec;

        // snapshot move root（降りたときに戻すための元位置）
        {
            var moveRoot = ResolveMovementRoot();
            if (moveRoot != null)
            {
                Physics.SyncTransforms();
                _preClimbRootPos = moveRoot.position;
                _preClimbRootRot = moveRoot.rotation;
                _hasPreClimbRootTR = true;
                Log("PreClimb SNAP pos=" + _preClimbRootPos.ToString("F3"));
            }
        }

        // 椅子を再レイキャストして、インスペクタの設定どおりに整列
        Transform chairRoot = null;
        if (_rayOrigin != null)
        {
            Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
            if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
            {
                chairRoot = ResolveChairRoot(hitInfo);
            }
        }
        AlignForClimb(chairRoot);

        CreateWrapperIfNeeded(initialSetup: false);
        _canAcceptToggle = false;

        // snapshot ray origin
        if (_rayOrigin != null)
        {
            _snapRayParent = _rayOrigin.parent;
            _snapRayLocalPos = _rayOrigin.localPosition;
            _snapRayLocalRot = _rayOrigin.localRotation;
            _snapRayLocalScale = _rayOrigin.localScale;
            _hasSnapRay = true;
        }

        // snapshot components
        _snapAnimatorApplyRM = _playerAnimator ? _playerAnimator.applyRootMotion : false;
        _hasSnapAnimatorRM = _playerAnimator != null;

        _snapPCEnabled = _playerController && _playerController.enabled;
        _hasSnapPCEnabled = _playerController != null;

        if (GetRoot(out var root))
        {
            var cc = root.GetComponent<CharacterController>();
            var nma = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
            _snapCCEnabled = cc && cc.enabled;
            _snapNMAEnabled = nma && nma.enabled;
        }

        Log("ClimbStart");

        if (_snapToChairTop)
        {
            Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
            if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
            {
                Bounds b = hitInfo.collider.bounds;
                float plannedY = b.center.y + b.extents.y + _chairTopOffset;
                _lockedRaisedPos = new Vector3(transform.position.x, plannedY, transform.position.z);
            }
        }

        if (_isPushing) OnPushReleased(default);

        // save ground Y
        RefreshFBS("PreClimb");
        if (_fbs != null)
        {
            _fbs.RebaseFeetGround("PreClimb");
            if (_fbs.TryGetFeetGroundY(out var gy))
            {
                _preClimbFeetGroundY = gy;
                _hasPreClimbFeetGroundY = true;
                Log($"PreClimb GroundY={gy:F4}");
            }
            else _hasPreClimbFeetGroundY = false;

            _fbs.SetClimbOverride(true);
        }

        if (_playerController != null && _playerController.enabled)
            _playerController.enabled = false;

        _playerAnimator.applyRootMotion = _useRootMotionForClimb;

        if (!string.IsNullOrEmpty(_climbBoolName))
            _playerAnimator.SetBool(_climbBoolHash, true);

        if (_playerAnimator.layerCount > _climbLayer && _playerAnimator.HasState(_climbLayer, _climbStateHash))
            _playerAnimator.CrossFadeInFixedTime(_climbStateHash, _crossFadeDur, _climbLayer);
        else
            Warn($"Animator state '{_climbStateName}' not found on layer {_climbLayer}. Skip CrossFade.");

        _isClimbing = true;
        _elapsedClimb = 0f;
        _yRaised = false;
        _state = MountState.Climbing;

        DumpState("ClimbStart");
        StartCoroutine(ClimbRoutine());
    }

    private IEnumerator ClimbRoutine()
    {
        float t = 0f; bool sawTag = false;
        yield return null;
        while (true)
        {
            _elapsedClimb += Time.deltaTime; t += Time.deltaTime;

            if (!_useRootMotionForClimb && !_yRaised && _elapsedClimb >= _yRaiseTime)
            {
                var w = (_raiseTarget != null) ? _raiseTarget : (_playerAnimator ? _playerAnimator.transform : null);
                if (w != null)
                {
                    Vector3 p = w.position;
                    float targetY = _lockedRaisedPos == default ? (p.y + _yRaiseAdd) : _lockedRaisedPos.y;
                    w.position = new Vector3(p.x, targetY, p.z);
                    _lockedRaisedPos = w.position; _holdRaisedPos = true; _holdTimer = 0f;
                    _yRaised = true;
                }
            }

            if (_playerAnimator != null)
            {
                var st = _playerAnimator.GetCurrentAnimatorStateInfo(_climbLayer);
                if (st.IsTag(_climbTag)) { sawTag = true; if (st.normalizedTime >= 1.0f) break; }
                else { if (!sawTag && t >= _fallbackAnimTime) break; }
            }
            yield return null;
        }

        RefreshFBS("ClimbEnd");
        if (_fbs != null) _fbs.RebaseFeetGround("ClimbEnd");
        _playerAnimator.applyRootMotion = false;

        if (_fbs != null) _fbs.SetClimbOverride(false);

        _isClimbing = false;
        _state = MountState.Mounted;
        _canAcceptToggle = true;

        Log("ClimbEnd");
        DumpState("ClimbEnd");
    }

    // Descend
    private void OnDescendPressed(InputAction.CallbackContext ctx)
    {
        Log("Interact started");
        if (_playerAnimator == null) { Log("Descend rejected: Animator null"); return; }
        if (!_canAcceptToggle) { Log("Descend rejected: Global gate closed"); return; }
        if (Time.time < _nextAcceptTime) { Log("Descend rejected: Cooldown"); return; }

        if (_state == MountState.Mounted)
        {
            _nextAcceptTime = Time.time + _inputCooldownSec;
            if (_descendCo != null) StopCoroutine(_descendCo);
            _descendCo = StartCoroutine(DescendRoutine());
        }
        else { Log($"Descend rejected: state={_state}"); }
    }

    private IEnumerator DescendRoutine()
    {
        _isDescending = true;
        _state = MountState.Descending;
        _canAcceptToggle = false;

        _playerAnimator.applyRootMotion = false;

        if (_isPushing) OnPushReleased(default);

        // lock side first
        if (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator != null)
            _playerAnimator.SetBool(_climbBoolHash, false);
        RefreshFBS("Descend");
        if (_fbs != null) _fbs.SetClimbOverride(false);

        DemoteAndDestroyWrapper("Descend");

        GetRoot(out var root);
        var rb = root ? root.GetComponent<Rigidbody>() : null;
        var cc = root ? root.GetComponent<CharacterController>() : null;
        var nma = root ? root.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;

        bool ccWasEnabled = cc && cc.enabled;
        bool nmaWasEnabled = nma && nma.enabled;

        if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (ccWasEnabled) cc.enabled = false;
        if (nmaWasEnabled) nma.enabled = false;

        // restore TR
        {
            var moveRoot = ResolveMovementRoot();
            if (_hasPreClimbRootTR && moveRoot != null)
            {
                moveRoot.SetPositionAndRotation(_preClimbRootPos, _preClimbRootRot);
                Physics.SyncTransforms();
                Log($"Restore to PreClimb pos={_preClimbRootPos:F3}");
            }
        }

        // restore ray origin
        if (_hasSnapRay && _rayOrigin != null)
        {
            _rayOrigin.SetParent(_snapRayParent, worldPositionStays: false);
            _rayOrigin.localPosition = _snapRayLocalPos;
            _rayOrigin.localRotation = _snapRayLocalRot;
            _rayOrigin.localScale = _snapRayLocalScale;
        }

        // restore RM flag
        if (_hasSnapAnimatorRM && _playerAnimator != null)
            _playerAnimator.applyRootMotion = _snapAnimatorApplyRM;

        Physics.SyncTransforms();

        // === order for non-penetrating snap ===
        if (cc) cc.enabled = _snapCCEnabled;
        LogTrace("Descend: CC ON");

        RefreshFBS("Descend-BeforeSnap");
        if (_fbs != null)
        {
            _fbs.bypassFramesAfterRebase = 0;
            _fbs.rebaseOnClimbEnd = false;
            _fbs.lockFramesAfterExternalSnap = Mathf.Max(_fbs.lockFramesAfterExternalSnap, 2);
        }

        if (_fbs != null && _hasPreClimbFeetGroundY)
        {
            _fbs.SetClimbOverride(false);
            if (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator != null)
                _playerAnimator.SetBool(_climbBoolHash, false);

            _fbs.SetGroundYAndSnap(_preClimbFeetGroundY, "Descend(PreClimbGroundY)");
            Physics.SyncTransforms();
            LogTrace($"Descend: SnapToGroundY={_preClimbFeetGroundY:F4}");
        }

        const float epsilonUp = 0.005f;
        if (cc && cc.enabled && epsilonUp > 0f) { cc.Move(Vector3.up * epsilonUp); LogTrace("Descend: epsilonUp"); }

        if (nma) nma.enabled = _snapNMAEnabled;
        Physics.SyncTransforms();

        // === frame-by-frame snapshot for a few frames ===
        if (traceAfterDescendFrames > 0) StartCoroutine(TraceFramesAfterDescend(traceAfterDescendFrames));

        yield return null;
        if (_playerController != null) _playerController.enabled = true;
        if (cc && cc.enabled) cc.Move(Vector3.zero);

        _isDescending = false;
        _isClimbing = false;
        _state = MountState.Grounded;
        _canAcceptToggle = true;
        _toggleLatched = false;

        if (_pushTextMeshPro != null) { _lastUiText = null; _pushTextMeshPro.SetText(""); }

        Log("DescendEnd");
        DumpState("DescendEnd");
        _descendCo = null;

        CreateWrapperIfNeeded(initialSetup: false);
    }

    private IEnumerator TraceFramesAfterDescend(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Snapshot("TRACE PostDescend");
            yield return new WaitForEndOfFrame();
        }
    }

    private void Snapshot(string label)
    {
        GetRoot(out var root);
        var cc = root ? root.GetComponent<CharacterController>() : null;
        float y = transform.position.y;
        string climbB = (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator) ? _playerAnimator.GetBool(_climbBoolHash).ToString() : "?";
        string fbs = _fbs
            ? $"YFree={_fbs.IsYFree},HardLock={_fbs.HardLockFramesLeft},FeetBase={(_fbs.HasGroundBase ? _fbs.FeetGroundY.ToString("F3") : "NA")}"
            : "FBS=?";
        Log($"TRACE {label}: posY={y:F4} CC={(cc && cc.enabled ? "ON" : "OFF")} RM={(_playerAnimator && _playerAnimator.applyRootMotion ? "ON" : "OFF")} IsClimbingBool={climbB} {fbs}");
    }

    private void LogTrace(string msg) => Log("TRACE " + msg);

    // ===== Helpers =====

    // 椅子基準で、インスペクタで指定したローカルオフセット＆回転でプレイヤーを整列
    private void AlignForClimb(Transform chairRoot)
    {
        if (chairRoot == null) return;

        var moveRoot = ResolveMovementRoot();
        if (moveRoot == null) return;

        // 位置：椅子ローカルの _climbLocalOffset をワールドに変換
        Vector3 targetPos = chairRoot.TransformPoint(_climbLocalOffset);

        // 高さは現在の足元と合わせる（必要ならここを椅子基準に変えてもOK）
        Vector3 currentPos = moveRoot.position;
        targetPos.y = currentPos.y;

        moveRoot.position = targetPos;

        // 回転：椅子の回転にヨーオフセットを足す
        Quaternion baseRot = chairRoot.rotation;
        Quaternion yawOffset = Quaternion.Euler(0f, _climbYawOffsetDeg, 0f);
        Quaternion finalRot = baseRot * yawOffset;

        moveRoot.rotation = finalRot;

        Physics.SyncTransforms();
        Log($"AlignForClimbInspector: pos={targetPos:F3}, yawOffset={_climbYawOffsetDeg}");
    }

    private void CreateWrapperIfNeeded(bool initialSetup)
    {
        if (_playerAnimator == null) return;
        if (_raiseTarget == null) _raiseTarget = _playerAnimator.transform;

        if (_wrapper != null && _playerAnimator.transform.parent == _wrapper) return;

        Transform child = _raiseTarget;
        Transform parent = child.parent;

        GameObject go = new GameObject(child.name + "_LiftWrapper");
        _wrapper = go.transform;

        int si = parent ? child.GetSiblingIndex() : 0;
        _wrapper.SetPositionAndRotation(child.position, child.rotation);
        _wrapper.localScale = Vector3.one;
        if (parent) { _wrapper.SetParent(parent, true); _wrapper.SetSiblingIndex(si); }

        child.SetParent(_wrapper, true);
    }

    private void DemoteAndDestroyWrapper(string reason)
    {
        if (_playerAnimator == null) return;
        Transform child = _playerAnimator.transform;

        if (_wrapper == null || child.parent != _wrapper)
        { _wrapper = child; return; }

        Transform parent = _wrapper.parent;
        Vector3 wp = child.position;
        Quaternion wr = child.rotation;

        child.SetParent(parent, true);
        var go = _wrapper.gameObject;
        _wrapper = child;
        Object.Destroy(go);

        child.SetPositionAndRotation(wp, wr);
    }

    private Transform ResolveMovementRoot()
    {
        Transform t = _playerAnimator ? _playerAnimator.transform : transform;
        while (t != null)
        {
            if (t.GetComponent<CharacterController>() != null) return t;
            if (t.GetComponent<Rigidbody>() != null) return t;
            if (t.GetComponent<UnityEngine.AI.NavMeshAgent>() != null) return t;
            t = t.parent;
        }
        if (_wrapper != null) return _wrapper.parent != null ? _wrapper.parent : _wrapper;
        return transform;
    }

    private Transform ResolveChairRoot(RaycastHit hitInfo)
    {
        Transform t = hitInfo.rigidbody ? hitInfo.rigidbody.transform : hitInfo.collider.transform;
        while (t != null && !t.CompareTag(_chairTag)) t = t.parent;
        return t;
    }

    private bool GetRoot(out Transform root)
    {
        root = null;
        if (_wrapper == null) return false;
        root = _wrapper.parent != null ? _wrapper.parent : _wrapper;
        return root != null;
    }

    private void RefreshFBS(string reason)
    {
        if (_fbsRef != null) { _fbs = _fbsRef; Log("FBS via inspector: " + _fbs.name); return; }
        _fbs = null;
        if (_playerAnimator != null) _fbs = _playerAnimator.GetComponentInParent<ForceBoneScale>();
        if (_fbs == null) _fbs = GetComponentInParent<ForceBoneScale>();
        if (_fbs == null) _fbs = GetComponentInChildren<ForceBoneScale>(true);
        if (_fbs == null && transform.root != null) _fbs = transform.root.GetComponentInChildren<ForceBoneScale>(true);
        if (_fbs == null) Warn("FBS not found");
        else Log("FBS resolved (" + _fbs.name + ") : " + reason);
    }

    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
    }
}
