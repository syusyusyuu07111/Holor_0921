/*
 * PushController.cs
 *
 * 椅子（Chair）に対して
 *  - 左クリック長押しで「押し続ける」
 *  - 右クリック or DoorOpen で「乗る／降りる」
 * を制御するスクリプト。
 *
 * 椅子押し時：
 *  - Raycast で椅子を検知
 *  - Rigidbody.SweepTest + MovePosition で「壁の手前まで」物理的に押す
 *  - 椅子の移動量だけプレイヤーにも加算して、椅子と一緒に動かす
 *
 * 登り／降り：
 *  - 既存の登りアニメーション制御＋位置補正処理
 */

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using CriWare; // CRI

[DisallowMultipleComponent]
public class PushController : MonoBehaviour
{
    // ========================
    // 検知 / 物理設定
    // ========================
    [Header("検知 / 物理設定")]
    [Tooltip("Raycast を飛ばす起点（カメラやプレイヤーの目線など）")]
    [SerializeField] private Transform _rayOrigin;

    [Tooltip("Raycast でヒットさせるレイヤーマスク")]
    [SerializeField] private LayerMask _detectMask;

    [Tooltip("椅子オブジェクトに付けるタグ名")]
    [SerializeField] private string _chairTag = "Chair";

    [Tooltip("押す / 乗る 判定用 Raycast の距離")]
    [SerializeField] private float _pushDistance = 2.5f;

    // ========================
    // 押す（左クリック）
    // ========================
    [Header("押す設定（左クリック）")]
    [Tooltip("押している間、椅子を動かす速度")]
    [SerializeField] private float _pushSpeed = 1.5f;

    [Tooltip("押し入力用の InputActionReference（Player.Push をアサイン）")]
    [SerializeField] private InputActionReference _pushActionRef;

    // 押し・内部状態
    private InputAction _pushAction;
    private Rigidbody _pushingRb;
    private Transform _pushingTransform;
    private Vector3 _pushDir;
    private bool _isPushing;
    private bool _originalKinematic;
    private Vector3 _prevChairPos;
    private bool _hasPrevChairPos;

    [Tooltip("押し中に PlayerController を一時停止するための参照")]
    [SerializeField] private PlayerController _playerController;
    private bool _pushPCWasEnabled = false;

    // ========================
    // 押し・壁判定の調整
    // ========================
    [Header("押し・壁判定調整")]
    [Tooltip("SweepTest で壁の手前にどれくらい余白を残すか（大きいほど手前で止まる）")]
    [SerializeField] private float _pushSweepSkin = 0.01f;

    [Tooltip("この距離未満しか進めない場合は「もう止まった扱い」にする最小移動距離")]
    [SerializeField] private float _pushMinMoveDistance = 0.001f;

    // ========================
    // 乗る（右クリック / DoorOpen）
    // ========================
    [Header("乗る設定（右クリック / DoorOpen）")]
    [Tooltip("プレイヤーの Animator")]
    [SerializeField] private Animator _playerAnimator;

    [Tooltip("登り時に Y 方向を持ち上げる対象（未指定なら Animator の Transform）")]
    [SerializeField] private Transform _raiseTarget;

    // ========================
    // 登りアニメーション設定
    // ========================
    [Header("登りアニメーション設定")]
    [Tooltip("登り中フラグとして使う Animator Bool 名")]
    [SerializeField] private string _climbBoolName = "IsClimbing";

    [Tooltip("登りステートに付けた Animator の Tag 名")]
    [SerializeField] private string _climbTag = "Climb";

    [Tooltip("登りステートの State 名")]
    [SerializeField] private string _climbStateName = "Climb";

    [Tooltip("登りステートが存在する Animator レイヤー番号")]
    [SerializeField] private int _climbLayer = 0;

    [Tooltip("登りステートへ CrossFade する時間（秒）")]
    [SerializeField] private float _crossFadeDur = 0.15f;

    [Tooltip("Tag が見つからない場合のフェイルセーフ用・強制終了までの時間（秒）")]
    [SerializeField] private float _fallbackAnimTime = 1.0f;

    // ========================
    // 非 RootMotion 時の Y 持ち上げ設定
    // ========================
    [Header("Y 持ち上げ設定（RootMotion を使わない時のみ）")]
    [Tooltip("椅子の上面にスナップさせるかどうか")]
    [SerializeField] private bool _snapToChairTop = true;

    [Tooltip("椅子の上面からどれだけ上にオフセットするか")]
    [SerializeField] private float _chairTopOffset = 0.25f;

    [Tooltip("登り開始から何秒後に Y を持ち上げるか")]
    [SerializeField] private float _yRaiseTime = 0.6f;

    [Tooltip("Y をどれだけ持ち上げるか（snapToChairTop=false のときに使用）")]
    [SerializeField] private float _yRaiseAdd = 0.5f;

    // ========================
    // 乗り始め位置・向きの調整
    // ========================
    [Header("乗り始め位置・向きの調整")]
    [Tooltip("椅子ローカル座標でのプレイヤーの待機位置 (x:右, y:上, z:前)")]
    [SerializeField] private Vector3 _climbLocalOffset = new Vector3(0f, 0f, -0.5f);

    [Tooltip("椅子の向き（Y 回転）に対するプレイヤーの向きオフセット（度単位）")]
    [SerializeField] private float _climbYawOffsetDeg = 0f;

    // ========================
    // 登り後のホールド
    // ========================
    [Header("登り後のホールド時間")]
    [Tooltip("登り切った後、椅子上の位置を何秒ホールドするか")]
    [SerializeField] private float _holdAfterClimbSec = 2.0f;

    // ========================
    // Root Motion 使用設定
    // ========================
    [Header("Root Motion 設定")]
    [Tooltip("登りアニメーション中に RootMotion を使用するかどうか")]
    [SerializeField] private bool _useRootMotionForClimb = true;

    // ========================
    // UI 表示
    // ========================
    [Header("UI 表示設定")]
    [Tooltip("「押す」「乗る」「降りる」などのテキストを表示する TextMeshProUGUI")]
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    // ========================
    // SE 再生（CRI）
    // ========================
    [Header("SE 設定（CRI）")]
    [Tooltip("椅子を押している間にループ再生する SE 用 CriAtomSource（ループ設定のキューを指定）")]
    [SerializeField] private CriAtomSource _pushLoopSource;

    [Tooltip("椅子に上った瞬間に一度だけ再生する SE 用 CriAtomSource")]
    [SerializeField] private CriAtomSource _climbSeSource;

    // ========================
    // ForceBoneScale 連携（任意）
    // ========================
    [Header("ForceBoneScale 参照（任意）")]
    [Tooltip("明示的に指定したい ForceBoneScale。未指定の場合は自動検索")]
    [SerializeField] private ForceBoneScale _fbsRef;

    // ========================
    // デバッグ表示
    // ========================
    [Header("デバッグログ設定")]
    [Tooltip("true の場合、ログを出力する")]
    [SerializeField] private bool _debugLogging = true;

    [Tooltip("true の場合、重要なものだけにログを絞る")]
    [SerializeField] private bool _logMinimal = true;

    // ========================
    // 降りた後のトレース
    // ========================
    [Header("Trace 設定")]
    [Tooltip("降り処理開始後、何フレーム分詳細スナップを取るか")]
    [SerializeField] private int traceAfterDescendFrames = 3;

    // ========================
    // 内部状態・定数
    // ========================
    private const string LOG = "[CLIMB] ";

    private enum MountState { Grounded, Climbing, Mounted, Descending }

    [Tooltip("入力を連打されたときに受け付けないクールダウン時間（秒）")]
    [SerializeField] private float _inputCooldownSec = 0.12f;

    private MountState _state = MountState.Grounded;
    private bool _canAcceptToggle = true;
    private bool _toggleLatched = false;
    private float _nextAcceptTime = 0f;

    // 入力
    private InputSystem_Actions _inputs;
    private InputAction _doorOpen;
    private InputAction _rightClick;
    private InputAction _interact;

    // 登り / 降り
    private bool _isClimbing;
    private float _elapsedClimb;
    private bool _yRaised;
    private bool _isDescending;
    private Coroutine _descendCo;

    // 登る前のルート姿勢
    private Vector3 _preClimbRootPos;
    private Quaternion _preClimbRootRot;
    private bool _hasPreClimbRootTR;

    // 登る前の足元 Y（FBS）
    private float _preClimbFeetGroundY;
    private bool _hasPreClimbFeetGroundY;

    // RayOrigin のスナップ
    private Transform _snapRayParent;
    private Vector3 _snapRayLocalPos;
    private Quaternion _snapRayLocalRot;
    private Vector3 _snapRayLocalScale;
    private bool _hasSnapRay;

    // 各コンポーネント状態
    private bool _snapAnimatorApplyRM;
    private bool _hasSnapAnimatorRM;
    private bool _snapPCEnabled;
    private bool _hasSnapPCEnabled;
    private bool _snapCCEnabled;
    private bool _snapNMAEnabled;

    // 椅子上ホールド
    private bool _holdRaisedPos;
    private float _holdTimer;
    private Vector3 _lockedRaisedPos;

    // Wrapper（登り用親）
    private Transform _wrapper;

    // FBS
    private ForceBoneScale _fbs;

    // Animator ハッシュ
    private int _climbBoolHash;
    private int _climbStateHash;

    // ログ系
    private void Log(string msg)
    {
        if (!_debugLogging) return;
        if (_logMinimal)
        {
            if (!(msg.StartsWith("STATE") ||
                  msg.StartsWith("ClimbStart") || msg.StartsWith("ClimbEnd") ||
                  msg.StartsWith("DescendEnd") || msg.StartsWith("Interact") ||
                  msg.StartsWith("FBS") || msg.StartsWith("PreClimb") ||
                  msg.StartsWith("Restore") || msg.StartsWith("TRACE") ||
                  msg.StartsWith("PUSH TryStartPush") ||
                  msg.StartsWith("PUSH StopPush") ||
                  msg.StartsWith("PUSH SweepBlock")))
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

    // ========================
    // ライフサイクル
    // ========================
    private void Awake()
    {
        if (_playerAnimator == null) Err("Animator が設定されていません。");
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
        if (_pushAction != null) { _pushAction.Disable(); _pushAction = null; }

        if (_isPushing) StopPush();

        if (_doorOpen != null) { _doorOpen.performed -= OnClimbPressed; _doorOpen.Disable(); }
        if (_rightClick != null) { _rightClick.performed -= OnClimbPressed; _rightClick.Disable(); }

        if (_interact != null)
        {
            _interact.started -= OnDescendPressed;
            if (_interact.bindings.Count > 0) _interact.RemoveBindingOverride(0);
            _interact.Disable();
        }

        if (_inputs != null) _inputs.Disable();
    }

    private string _lastUiText = null;

    // ========================
    // Update
    // ========================
    private void Update()
    {
        if (_isClimbing) return;

        // 押し可能な状態（Grounded かつ RayOrigin がある）
        bool canPushNow = (_rayOrigin != null && _state == MountState.Grounded);

        // 押し入力（Action or Mouse 左）
        bool actionPressed = (_pushAction != null && _pushAction.IsPressed());
        bool mousePressed = (Mouse.current != null && Mouse.current.leftButton.isPressed);
        bool isPushPressed = actionPressed || mousePressed;

        // 押し開始 / 終了
        if (canPushNow && isPushPressed)
        {
            if (!_isPushing) TryStartPush();
        }
        else
        {
            if (_isPushing) StopPush();
        }

        // === 椅子を押している間の処理 ===
        if (_isPushing)
        {
            if (_pushingTransform == null)
            {
                Log("PUSH ERROR: _isPushing==true だが _pushingTransform が null");
            }
            else
            {
                var moveRoot = ResolveMovementRoot();
                if (moveRoot == null)
                {
                    Log("PUSH ERROR: moveRoot が見つからない");
                }
                else
                {
                    Vector3 dir = _pushDir.normalized;
                    float dist = _pushSpeed * Time.deltaTime;

                    // 現在位置
                    Vector3 chairCurrent =
                        (_pushingRb != null) ? _pushingRb.position : _pushingTransform.position;

                    float usedDist = dist;

                    // Rigidbody.SweepTest で「進んでよい距離」を事前にチェック
                    if (_pushingRb != null && dist > 0f)
                    {
                        float sweepDist = dist + _pushSweepSkin;
                        if (_pushingRb.SweepTest(dir, out RaycastHit hit, sweepDist))
                        {
                            float safeDist = hit.distance - _pushSweepSkin;
                            if (safeDist < 0f) safeDist = 0f;

                            // ほとんど進めない場合は「止まった扱い」
                            if (safeDist < _pushMinMoveDistance)
                            {
                                usedDist = 0f;
                                Log($"PUSH SweepBlock: hit={hit.collider.name}, hitDist={hit.distance:0.000}, safeDist={safeDist:0.000}");
                            }
                            else
                            {
                                usedDist = Mathf.Min(dist, safeDist);
                            }
                        }
                    }

                    Vector3 chairTarget = chairCurrent + dir * usedDist;

                    // 椅子の実移動
                    if (_pushingRb != null && !_pushingRb.isKinematic)
                    {
                        _pushingRb.MovePosition(chairTarget);
                    }
                    else
                    {
                        _pushingTransform.position = chairTarget;
                    }

                    // フレームごとの椅子の移動量
                    if (_hasPrevChairPos)
                    {
                        Vector3 delta = _pushingTransform.position - _prevChairPos;

                        // プレイヤーも同じだけ動かす
                        MoveRootWithCollision(moveRoot, delta);
                    }

                    _prevChairPos = _pushingTransform.position;
                    _hasPrevChairPos = true;
                }
            }
        }

        if (_rayOrigin == null) return;

        // --- UI テキスト更新 ---
        if (_pushTextMeshPro != null)
        {
            string nextText = "";

            if (_state == MountState.Grounded)
            {
                Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);

                if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
                {
                    Transform chairRoot = ResolveChairRoot(hitInfo);
                    nextText = (chairRoot != null)
                        ? "左クリック長押し:押す / 右クリック or Q：上る"
                        : "";
                }
                else
                {
                    nextText = "";
                }
            }
            else if (_state == MountState.Mounted)
            {
                nextText = "(E): 降りる";
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
        if (_rightClick != null && !_rightClick.IsPressed()) _toggleLatched = false;
        if (_doorOpen != null && !_doorOpen.IsPressed()) _toggleLatched = false;

        if (!_isClimbing && _holdRaisedPos && _wrapper != null)
        {
            _wrapper.position = _lockedRaisedPos;
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= _holdAfterClimbSec) _holdRaisedPos = false;
        }
    }

    // ========================
    // 押す処理
    // ========================
    private void TryStartPush()
    {
        if (_isClimbing || _rayOrigin == null)
        {
            Log($"PUSH TryStartPush: rejected isClimbing={_isClimbing}, rayOriginNull={_rayOrigin == null}");
            return;
        }

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);

        if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
        {
            Transform chairRoot = ResolveChairRoot(hitInfo);
            if (chairRoot == null)
            {
                Log("PUSH TryStartPush: hit したが Chair タグの親が見つからないので中止");
                return;
            }

            _pushingTransform = chairRoot;
            _pushingRb = chairRoot.GetComponent<Rigidbody>();

            Log($"PUSH TryStartPush: chairRoot={chairRoot.name}, rb={(_pushingRb ? "あり" : "なし")}, chairPos={chairRoot.position}");

            if (_pushingRb != null)
            {
                _originalKinematic = _pushingRb.isKinematic;
                _pushingRb.isKinematic = false; // 動的 Rigidbody として扱う
            }

            _pushDir = _rayOrigin.forward.normalized;
            _isPushing = true;

            _prevChairPos = _pushingTransform.position;
            _hasPrevChairPos = true;

            // 押し中は PlayerController.Update を止める
            if (_playerController != null)
            {
                _pushPCWasEnabled = _playerController.enabled;
                _playerController.enabled = false;
            }

            // 押しループSE再生
            if (_pushLoopSource != null)
            {
                _pushLoopSource.Play();
            }
        }
    }

    private void StopPush()
    {
        Log("PUSH StopPush: 呼び出し");

        if (_pushingRb != null)
        {
            _pushingRb.isKinematic = _originalKinematic;
        }

        _pushingRb = null;
        _pushingTransform = null;
        _isPushing = false;
        _hasPrevChairPos = false;

        if (_playerController != null && _pushPCWasEnabled)
        {
            _playerController.enabled = true;
        }
        _pushPCWasEnabled = false;

        // 押しループSE停止
        if (_pushLoopSource != null)
        {
            _pushLoopSource.Stop();
        }
    }

    // ========================
    // 上る（Climb）／降りる（Descend）
    // ========================

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
        if (_playerAnimator == null || _rayOrigin == null) { reason = "Animator または RayOrigin が null"; return false; }
        if (_toggleLatched) { reason = "ラッチ中"; return false; }
        if (Time.time < _nextAcceptTime) { reason = "クールダウン中"; return false; }
        if (!_canAcceptToggle) { reason = "ゲート閉"; return false; }
        if (_playerAnimator.IsInTransition(_climbLayer)) { reason = "遷移中"; return false; }
        if (_state != MountState.Grounded) { reason = $"State != Grounded ({_state})"; return false; }

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (!Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
        { reason = "Ray miss"; return false; }
        if (ResolveChairRoot(hitInfo) == null)
        { reason = "Chair タグが見つからない"; return false; }

        reason = "OK"; return true;
    }

    private void TryStartClimb(InputAction.CallbackContext ctx)
    {
        _toggleLatched = true;
        _nextAcceptTime = Time.time + _inputCooldownSec;

        // ルート姿勢保存
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

        if (_isPushing) StopPush();

        // 椅子の向きに合わせて乗り始め位置・向きを調整
        Transform chairRoot = null;
        if (_rayOrigin != null)
        {
            Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
            if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
                chairRoot = ResolveChairRoot(hitInfo);
        }
        AlignForClimb(chairRoot);

        // 上りSE再生（登り開始が確定したタイミング）
        if (_climbSeSource != null)
        {
            _climbSeSource.Play();
        }

        CreateWrapperIfNeeded(initialSetup: false);
        _canAcceptToggle = false;

        // RayOrigin スナップ
        if (_rayOrigin != null)
        {
            _snapRayParent = _rayOrigin.parent;
            _snapRayLocalPos = _rayOrigin.localPosition;
            _snapRayLocalRot = _rayOrigin.localRotation;
            _snapRayLocalScale = _rayOrigin.localScale;
            _hasSnapRay = true;
        }

        // コンポーネント状態スナップ
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

            if (cc && cc.enabled) cc.enabled = false;
            if (nma && nma.enabled) nma.enabled = false;
        }

        Log("ClimbStart");

        // 椅子上面へのスナップ位置
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
            Warn($"Animator state '{_climbStateName}' not found on layer {_climbLayer}.");

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

    private void OnDescendPressed(InputAction.CallbackContext ctx)
    {
        Log("Interact started");
        if (_playerAnimator == null) { Log("Descend rejected: Animator null"); return; }
        if (!_canAcceptToggle) { Log("Descend rejected: gate closed"); return; }
        if (Time.time < _nextAcceptTime) { Log("Descend rejected: cooldown"); return; }

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

        if (_isPushing) StopPush();

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

        if (rb)
        {
#if UNITY_600_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.linearVelocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        if (ccWasEnabled) cc.enabled = false;
        if (nmaWasEnabled) nma.enabled = false;

        {
            var moveRoot = ResolveMovementRoot();
            if (_hasPreClimbRootTR && moveRoot != null)
            {
                moveRoot.SetPositionAndRotation(_preClimbRootPos, _preClimbRootRot);
                Physics.SyncTransforms();
                Log($"Restore to PreClimb pos={_preClimbRootPos:F3}");
            }
        }

        if (_hasSnapRay && _rayOrigin != null)
        {
            _rayOrigin.SetParent(_snapRayParent, worldPositionStays: false);
            _rayOrigin.localPosition = _snapRayLocalPos;
            _rayOrigin.localRotation = _snapRayLocalRot;
            _rayOrigin.localScale = _snapRayLocalScale;
        }

        if (_hasSnapAnimatorRM && _playerAnimator != null)
            _playerAnimator.applyRootMotion = _snapAnimatorApplyRM;

        Physics.SyncTransforms();

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
        if (cc && cc.enabled && epsilonUp > 0f)
        {
            cc.Move(Vector3.up * epsilonUp);
            LogTrace("Descend: epsilonUp");
        }

        if (nma) nma.enabled = _snapNMAEnabled;
        Physics.SyncTransforms();

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

    // ========================
    // 補助
    // ========================
    private void AlignForClimb(Transform chairRoot)
    {
        if (chairRoot == null) return;

        var moveRoot = ResolveMovementRoot();
        if (moveRoot == null) return;

        Vector3 targetPos = chairRoot.TransformPoint(_climbLocalOffset);
        Vector3 currentPos = moveRoot.position;
        targetPos.y = currentPos.y;

        moveRoot.position = targetPos;

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
        if (parent)
        {
            _wrapper.SetParent(parent, true);
            _wrapper.SetSiblingIndex(si);
        }

        child.SetParent(_wrapper, true);
    }

    private void DemoteAndDestroyWrapper(string reason)
    {
        if (_playerAnimator == null) return;
        Transform child = _playerAnimator.transform;

        if (_wrapper == null || child.parent != _wrapper)
        {
            _wrapper = child;
            return;
        }

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
        if (_fbsRef != null)
        {
            _fbs = _fbsRef;
            Log("FBS via inspector: " + _fbs.name);
            return;
        }
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

    /// <summary>
    /// プレイヤーを「椅子の移動量」で動かす。
    /// 優先順：
    ///  - PlayerController.ExternalMoveByDelta() があればそれを使う
    ///  - なければ CharacterController.Move(delta)
    ///  - それもなければ position 直接加算
    /// </summary>
    private void MoveRootWithCollision(Transform moveRoot, Vector3 delta)
    {
        if (delta == Vector3.zero || moveRoot == null) return;

        if (_playerController != null)
        {
            _playerController.ExternalMoveByDelta(delta);
            return;
        }

        var cc = moveRoot.GetComponent<CharacterController>();
        if (cc != null && cc.enabled)
        {
            cc.Move(delta);
        }
        else
        {
            moveRoot.position += delta;
        }
    }
}
