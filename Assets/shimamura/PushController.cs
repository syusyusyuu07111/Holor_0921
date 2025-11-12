using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PushController : MonoBehaviour
{
    [Header("Detection / Physics")]
    [SerializeField] private Transform _rayOrigin;
    [SerializeField] private LayerMask _LayerPositoin;
    [SerializeField] private float _pushDistance = 2.0f;

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
    [SerializeField] private string _climbStateFullPath = "Base Layer/Climb";
    [SerializeField] private int _climbLayer = 0;
    [SerializeField] private float _crossFadeDur = 0.15f;
    [SerializeField] private float _fallbackAnimTime = 1.0f;

    [Header("Timed Raise (非RootMotion時のみ)")]
    [SerializeField] private bool _snapToChairTop = true;
    [SerializeField] private float _chairTopOffset = 0.25f;
    [SerializeField] private float _yRaiseTime = 0.6f;
    [SerializeField] private float _yRaiseAdd = 0.5f;

    [Header("Hold After Climb")]
    [SerializeField] private float _holdAfterClimbSec = 2.0f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    [Header("Root Motion")]
    [SerializeField] private bool _useRootMotionForClimb = true;

    [Header("Debug / Trace")]
    [SerializeField] private bool _debugLogging = true;
    [SerializeField] private float _traceSecondsAfterClimb = 2.0f;
    [SerializeField] private bool _lockRootYForTraceWindow = false;

    // 入力
    private InputSystem_Actions _inputs;
    private InputAction _doorOpen;
    private InputAction _rightClick;
    private InputAction _pushAction;

    // 押し
    private Rigidbody _pushingRb;
    private Transform _pushingTransform;
    private Vector3 _pushDir;
    private bool _isPushing;
    private bool _originalKinematic;

    // 登り
    private bool _isClimbing;
    private float _elapsedClimb;
    private bool _yRaised;

    // スナップ
    private float _plannedSnapY;
    private bool _hasPlannedSnapY;

    // 位置保持
    private bool _holdRaisedPos;
    private float _holdTimer;
    private Vector3 _lockedRaisedPos;

    // 実移動対象（Animatorの親）
    private Transform _wrapper;

    // FBS
    private ForceBoneScale _fbs;

    // 補助
    private const string LOG = "[CLIMB]";
    private Coroutine _climbCo;
    private int _climbBoolHash;

    // 追跡
    private Coroutine _traceCo;
    private float _rootYLockForTrace = float.NaN;

    // ===== 初期化 =====
    private void Awake()
    {
        if (_playerAnimator == null)
            Debug.LogError(LOG + " Animator is null.");

        if (_raiseTarget == null && _playerAnimator != null)
            _raiseTarget = _playerAnimator.transform;

        // 親Wrapper生成（Animator直動の戻り対策）
        if (_raiseTarget != null && _raiseTarget.GetComponent<Animator>() != null)
        {
            Transform child = _raiseTarget;
            Transform parent = child.parent;

            GameObject go = new GameObject(child.name + "_LiftWrapper");
            _wrapper = go.transform;

            int si = parent ? child.GetSiblingIndex() : 0;
            _wrapper.SetPositionAndRotation(child.position, child.rotation);
            _wrapper.localScale = Vector3.one;
            if (parent) { _wrapper.SetParent(parent, true); _wrapper.SetSiblingIndex(si); }

            child.SetParent(_wrapper, true);
            if (_debugLogging) Debug.Log(LOG + " Setup Wrapped -> " + _wrapper.name);
        }
        else
        {
            _wrapper = _raiseTarget;
            if (_wrapper != null && _debugLogging) Debug.Log(LOG + " Using raise target -> " + _wrapper.name);
        }

        if (_wrapper != null) _fbs = _wrapper.GetComponentInParent<ForceBoneScale>();
        if (_fbs == null && _playerAnimator != null) _fbs = _playerAnimator.GetComponentInParent<ForceBoneScale>();

        if (!string.IsNullOrEmpty(_climbBoolName))
            _climbBoolHash = Animator.StringToHash(_climbBoolName);
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
        _doorOpen = _inputs.Player.DoorOpen;
        _doorOpen.Enable();
        _doorOpen.performed += OnClimbPressed;

        _rightClick = new InputAction(type: InputActionType.Button, binding: "<Mouse>/rightButton");
        _rightClick.Enable();
        _rightClick.performed += OnClimbPressed;
    }

    private void OnDisable()
    {
        if (_pushAction != null)
        {
            _pushAction.performed -= OnPushPressed;
            _pushAction.canceled -= OnPushReleased;
            _pushAction.Disable();
        }

        if (_doorOpen != null)
        {
            _doorOpen.performed -= OnClimbPressed;
            _doorOpen.Disable();
        }
        if (_rightClick != null)
        {
            _rightClick.performed -= OnClimbPressed;
            _rightClick.Disable();
        }

        if (_inputs != null) _inputs.Disable();
    }

    // ===== 更新 =====
    private void Update()
    {
        if (_isClimbing) return;

        if (_isPushing && _pushingTransform != null)
            _pushingTransform.position += _pushDir * _pushSpeed * Time.deltaTime;

        if (_rayOrigin == null) return;

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin, QueryTriggerInteraction.Ignore))
        {
            Transform root = hit.rigidbody ? hit.rigidbody.transform : hit.collider.transform;
            if (root != null && root.CompareTag("Chair"))
            {
                if (_pushTextMeshPro != null) _pushTextMeshPro.SetText("左クリックで押す / 右クリック or DoorOpen で乗る");
                TryUpdatePush(hit);
            }
            else
            {
                _pushingRb = null;
                if (_pushTextMeshPro != null) _pushTextMeshPro.SetText("");
            }
        }
        else
        {
            _pushingRb = null;
            if (_pushTextMeshPro != null) _pushTextMeshPro.SetText("");
        }
    }

    private void LateUpdate()
    {
        if (!_isClimbing && _holdRaisedPos && _wrapper != null)
        {
            _wrapper.position = _lockedRaisedPos;
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= _holdAfterClimbSec)
                _holdRaisedPos = false;
        }

        // 追跡期間中に root のYを固定（原因切り分け用）
        if (_lockRootYForTraceWindow && !float.IsNaN(_rootYLockForTrace))
        {
            Transform root = _wrapper ? _wrapper.parent : null;
            if (root != null)
            {
                Vector3 rp = root.position;
                root.position = new Vector3(rp.x, _rootYLockForTrace, rp.z);
            }
        }
    }

    // ===== 押す =====
    private void OnPushPressed(InputAction.CallbackContext _)
    {
        if (_isClimbing || _rayOrigin == null) return;

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin, QueryTriggerInteraction.Ignore))
        {
            Transform root = hit.rigidbody ? hit.rigidbody.transform : hit.collider.transform;
            if (root != null && root.CompareTag("Chair"))
            {
                _pushingRb = root.GetComponent<Rigidbody>();
                _pushingTransform = root;

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

    private void TryUpdatePush(RaycastHit hit)
    {
        if (_pushingTransform == null) return;
        Transform root = hit.rigidbody ? hit.rigidbody.transform : hit.collider.transform;
        if (root != _pushingTransform) _pushingRb = null;
    }

    // ===== 上る =====
    private void OnClimbPressed(InputAction.CallbackContext ctx)
    {
        if (_isClimbing || _playerAnimator == null || _wrapper == null || _rayOrigin == null) return;

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin, QueryTriggerInteraction.Ignore)) return;

        Transform target = hit.rigidbody ? hit.rigidbody.transform : hit.collider.transform;
        if (target == null || !target.CompareTag("Chair")) return;

        _hasPlannedSnapY = false;
        if (_snapToChairTop)
        {
            Bounds b = hit.collider.bounds;
            _plannedSnapY = b.center.y + b.extents.y + _chairTopOffset;
            _hasPlannedSnapY = true;
        }

        if (_isPushing) OnPushReleased(default);

        if (_fbs != null) _fbs.SetClimbOverride(true);

        if (_playerController != null && _playerController.enabled)
            _playerController.enabled = false;

        _playerAnimator.applyRootMotion = _useRootMotionForClimb;

        if (!string.IsNullOrEmpty(_climbBoolName))
            _playerAnimator.SetBool(_climbBoolHash, true);

        if (!string.IsNullOrEmpty(_climbStateFullPath))
            _playerAnimator.CrossFadeInFixedTime(_climbStateFullPath, _crossFadeDur, _climbLayer);

        _isClimbing = true;
        _elapsedClimb = 0f;
        _yRaised = false;

        if (_climbCo != null) StopCoroutine(_climbCo);
        _climbCo = StartCoroutine(ClimbRoutine());

        if (_debugLogging)
        {
            Debug.Log(LOG + " Start climb. applyRM=" + _playerAnimator.applyRootMotion + " input=" + (ctx.control != null ? ctx.control.name : "null"));
            DumpRootStack("ClimbStart");
        }
    }

    private IEnumerator ClimbRoutine()
    {
        float t = 0f;
        bool sawTag = false;

        yield return null;

        while (true)
        {
            _elapsedClimb += Time.deltaTime;
            t += Time.deltaTime;

            if (!_useRootMotionForClimb && !_yRaised && _elapsedClimb >= _yRaiseTime && _wrapper != null)
            {
                Vector3 p = _wrapper.position;
                float targetY = _hasPlannedSnapY ? _plannedSnapY : (p.y + _yRaiseAdd);
                _wrapper.position = new Vector3(p.x, targetY, p.z);
                _lockedRaisedPos = _wrapper.position; _holdRaisedPos = true; _holdTimer = 0f;
                _yRaised = true;
            }

            if (_playerAnimator != null)
            {
                AnimatorStateInfo st = _playerAnimator.GetCurrentAnimatorStateInfo(_climbLayer);
                if (st.IsTag(_climbTag))
                {
                    sawTag = true;
                    if (st.normalizedTime >= 1.0f) break;
                }
                else
                {
                    if (!sawTag && t >= _fallbackAnimTime) break;
                }
            }
            yield return null;
        }

        FinalizeAtChildWorld();
        PromoteWrapperHeightToRoot(); // 親ルートを持ち上げ

        if (_fbs != null) _fbs.RebaseFeetGround("ClimbEnd from PushController");

        _playerAnimator.applyRootMotion = false;

        if (!string.IsNullOrEmpty(_climbBoolName))
            _playerAnimator.SetBool(_climbBoolHash, false);

        if (_traceCo != null) StopCoroutine(_traceCo);
        _traceCo = StartCoroutine(TraceAfterClimb(_traceSecondsAfterClimb));

        yield return null;
        if (_fbs != null) _fbs.SetClimbOverride(false);

        if (_playerController != null)
            _playerController.enabled = true;

        if (_debugLogging)
            Debug.Log(LOG + " End climb.");

        _isClimbing = false;
        _climbCo = null;
    }

    private void FinalizeAtChildWorld()
    {
        if (_playerAnimator == null || _wrapper == null) return;

        Transform child = _playerAnimator.transform;
        Vector3 wp = child.position;
        Quaternion wr = child.rotation;

        _wrapper.SetPositionAndRotation(wp, wr);

        child.SetPositionAndRotation(_wrapper.position, _wrapper.rotation);
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;

        _lockedRaisedPos = _wrapper.position;
        _holdRaisedPos = true;
        _holdTimer = 0f;

        if (_debugLogging) Debug.Log(LOG + " Finalize wrapperY=" + _wrapper.position.y.ToString("F3"));
    }

    private void PromoteWrapperHeightToRoot()
    {
        if (_wrapper == null) return;
        Transform root = _wrapper.parent;
        if (root == null) return;

        Vector3 desiredPos = _wrapper.position;

        if (_debugLogging)
            Debug.Log(LOG + " Promote root: beforeY=" + root.position.y.ToString("F3") + " -> afterY=" + desiredPos.y.ToString("F3"));

        root.position = desiredPos; // 必要ならYだけにしてもOK
        _wrapper.localPosition = Vector3.zero;
        _wrapper.localRotation = Quaternion.identity;
    }

    // ===== 診断 =====
    private IEnumerator TraceAfterClimb(float seconds)
    {
        Transform root = _wrapper ? _wrapper.parent : null;
        Transform child = _playerAnimator ? _playerAnimator.transform : null;

        float endTime = Time.time + Mathf.Max(0.1f, seconds);
        float prevRootY = root ? root.position.y : 0f;
        float prevWrapY = _wrapper ? _wrapper.position.y : 0f;
        float prevChildY = child ? child.position.y : 0f;

        _rootYLockForTrace = float.NaN;
        if (_lockRootYForTraceWindow && root != null)
            _rootYLockForTrace = root.position.y;

        DumpRootStack("TraceBegin");

        while (Time.time < endTime)
        {
            string extra = "";
            if (root != null)
            {
                Rigidbody rb = root.GetComponent<Rigidbody>();
                CharacterController cc = root.GetComponent<CharacterController>();
                UnityEngine.AI.NavMeshAgent nma = root.GetComponent<UnityEngine.AI.NavMeshAgent>();

                if (rb != null)
                {
                    extra += " RB[kin=" + rb.isKinematic + ",grav=" + rb.useGravity + ",velY=" + rb.linearVelocity.y.ToString("F3") + "]";
                }
                if (cc != null)
                {
                    extra += " CC[grounded=" + cc.isGrounded + "]";
                }
                if (nma != null)
                {
                    extra += " NMA[enabled=" + nma.enabled + ",updatePos=" + nma.updatePosition + "]";
                }
            }

            float rootY = root ? root.position.y : float.NaN;
            float wrapY = _wrapper ? _wrapper.position.y : float.NaN;
            float childY = child ? child.position.y : float.NaN;

            float dRoot = rootY - prevRootY;
            float dWrap = wrapY - prevWrapY;
            float dChild = childY - prevChildY;

            Debug.Log(
                LOG + " TRACE f#" + Time.frameCount +
                " RM=" + _playerAnimator.applyRootMotion +
                " climbing=" + _isClimbing +
                " | rootY=" + rootY.ToString("F3") + " (d" + dRoot.ToString("+0.000;-0.000") + ")" +
                " wrapY=" + wrapY.ToString("F3") + " (d" + dWrap.ToString("+0.000;-0.000") + ")" +
                " childY=" + childY.ToString("F3") + " (d" + dChild.ToString("+0.000;-0.000") + ")" +
                extra
            );

            prevRootY = rootY; prevWrapY = wrapY; prevChildY = childY;
            yield return null;
        }

        if (_lockRootYForTraceWindow) _rootYLockForTrace = float.NaN;
        DumpRootStack("TraceEnd");
    }

    private void DumpRootStack(string tag)
    {
        Transform root = _wrapper ? _wrapper.parent : null;
        Rigidbody rb = root ? root.GetComponent<Rigidbody>() : null;
        CharacterController cc = root ? root.GetComponent<CharacterController>() : null;
        UnityEngine.AI.NavMeshAgent nma = root ? root.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;

        string msg =
            LOG + " [" + tag + "] " +
            "root=" + (root != null ? root.name : "null") +
            " y=" + (root != null ? root.position.y.ToString("F3") : "NaN") +
            " wrapY=" + (_wrapper != null ? _wrapper.position.y.ToString("F3") : "NaN") +
            " childY=" + (_playerAnimator != null ? _playerAnimator.transform.position.y.ToString("F3") : "NaN") +
            " | RB=" + (rb != null ? "yes" : "no") +
            " CC=" + (cc != null ? "yes" : "no") +
            " NMA=" + (nma != null ? "yes" : "no");

        Debug.Log(msg);
    }

    // デバッグRay可視化（常時OK：プリプロセッサを使わない）
    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
    }
}
