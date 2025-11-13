/*
 * PushController.cs
 *
 * 椅子（Chair）を
 *  - 左クリックで「押す」
 *  - 右クリック or DoorOpen で「乗る」
 * ことを制御するコンポーネント。
 *
 *  - Raycast で椅子を検出し、UI テキストを表示
 *  - 押す処理（Rigidbody をキネマティックにして前方へ移動）
 *  - 乗る処理（アニメーション再生＋位置・向きの補正）
 *  - 降りる処理（位置を元に戻し、各コンポーネントの状態を復元）
 *
 *  - 「Climb Align」で椅子を基準にした乗り始め位置・向きをインスペクタから調整可能
 *  - Climb / Mounted / Descending 中は CC / NavMeshAgent / PlayerController を止めて、
 *    上りモーション中に勝手に動けないようにしています
 */

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[DisallowMultipleComponent]
public class PushController : MonoBehaviour
{
    // ========================
    // 検知 / 物理関連
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

    [Tooltip("押し入力用の InputActionReference")]
    [SerializeField] private InputActionReference _pushActionRef;

    // ========================
    // 乗る（右クリック / DoorOpen）
    // ========================
    [Header("乗る設定（右クリック / DoorOpen）")]
    [Tooltip("プレイヤーの Animator")]
    [SerializeField] private Animator _playerAnimator;

    [Tooltip("通常時の移動を制御する PlayerController")]
    [SerializeField] private PlayerController _playerController;

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
    // ForceBoneScale 連携（任意）
    // ========================
    [Header("ForceBoneScale 参照（任意）")]
    [Tooltip("明示的に指定したい ForceBoneScale。未指定の場合は自動検索")]
    [SerializeField] private ForceBoneScale _fbsRef;

    // ========================
    // デバッグ表示
    // ========================
    [Header("デバッグログ設定")]
    [Tooltip("true の場合、詳細ログを出力する")]
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

    // 乗り状態
    private enum MountState { Grounded, Climbing, Mounted, Descending }

    [Tooltip("入力を連打されたときに受け付けないクールダウン時間（秒）")]
    [SerializeField] private float _inputCooldownSec = 0.12f;

    private MountState _state = MountState.Grounded;  // 現在の状態
    private bool _canAcceptToggle = true;             // 全体的に入力を受け付けてよいか
    private bool _toggleLatched = false;              // トグル入力のラッチ（押しっぱなし対策）
    private float _nextAcceptTime = 0f;               // 次に入力を受け付ける時間

    // ========================
    // 入力関連の参照
    // ========================
    private InputSystem_Actions _inputs;
    private InputAction _doorOpen;
    private InputAction _rightClick;
    private InputAction _pushAction;
    private InputAction _interact;

    // ========================
    // 押す動作用の状態
    // ========================
    private Rigidbody _pushingRb;
    private Transform _pushingTransform;
    private Vector3 _pushDir;
    private bool _isPushing;
    private bool _originalKinematic;

    // ========================
    // 登り / 降り 状態
    // ========================
    private bool _isClimbing;
    private float _elapsedClimb;
    private bool _yRaised;

    private bool _isDescending;
    private Coroutine _descendCo;

    // ========================
    // 登り前の姿勢保存（降りたときに戻す）
    // ========================
    private Vector3 _preClimbRootPos;
    private Quaternion _preClimbRootRot;
    private bool _hasPreClimbRootTR;

    // ========================
    // 登り前の足元の地面 Y（FBS 経由）
    // ========================
    private float _preClimbFeetGroundY;
    private bool _hasPreClimbFeetGroundY;

    // ========================
    // RayOrigin の親子関係・ローカル TR スナップ
    // ========================
    private Transform _snapRayParent;
    private Vector3 _snapRayLocalPos;
    private Quaternion _snapRayLocalRot;
    private Vector3 _snapRayLocalScale;
    private bool _hasSnapRay;

    // ========================
    // Animator / PlayerController / CC / NMA の状態スナップ
    // ========================
    private bool _snapAnimatorApplyRM;
    private bool _hasSnapAnimatorRM;
    private bool _snapPCEnabled;
    private bool _hasSnapPCEnabled;
    private bool _snapCCEnabled;
    private bool _snapNMAEnabled;

    // ========================
    // 椅子上でのホールド
    // ========================
    private bool _holdRaisedPos;
    private float _holdTimer;
    private Vector3 _lockedRaisedPos;

    // ========================
    // Wrapper（持ち上げ用ルート）
    // ========================
    private Transform _wrapper;

    // ========================
    // ForceBoneScale への参照
    // ========================
    private ForceBoneScale _fbs;

    // ========================
    // Animator ハッシュ
    // ========================
    private int _climbBoolHash;
    private int _climbStateHash;

    // ========================
    // ログ出力ヘルパー
    // ========================
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
        if (_playerAnimator == null) Err("Animator が設定されていません。");
        if (_raiseTarget == null && _playerAnimator != null) _raiseTarget = _playerAnimator.transform;

        // 持ち上げ用 Wrapper を作成
        CreateWrapperIfNeeded(initialSetup: true);

        // ForceBoneScale を探す
        RefreshFBS("Awake");

        if (_fbs != null)
        {
            _fbs.bypassFramesAfterRebase = 0;
            _fbs.rebaseOnClimbEnd = false;
            _fbs.lockFramesAfterExternalSnap = 2;
        }

        // Animator パラメータ名をハッシュ化
        if (!string.IsNullOrEmpty(_climbBoolName)) _climbBoolHash = Animator.StringToHash(_climbBoolName);
        if (!string.IsNullOrEmpty(_climbStateName)) _climbStateHash = Animator.StringToHash(_climbStateName);

        DumpState("Awake");
    }

    private void OnEnable()
    {
        // 押す入力
        if (_pushActionRef != null)
        {
            _pushAction = _pushActionRef.action;
            _pushAction.Enable();
            _pushAction.performed += OnPushPressed;
            _pushAction.canceled += OnPushReleased;
        }

        // 共通 InputActions 有効化
        if (_inputs == null) _inputs = new InputSystem_Actions();
        _inputs.Enable();

        // DoorOpen（上る）
        _doorOpen = _inputs.Player.DoorOpen;
        _doorOpen.Enable();
        _doorOpen.performed += OnClimbPressed;

        // マウス右クリック（上る）
        _rightClick = new InputAction(type: InputActionType.Button, binding: "<Mouse>/rightButton");
        _rightClick.Enable();
        _rightClick.performed += OnClimbPressed;

        // Interact（降りる）
        _interact = _inputs.Player.Interact;
        if (_interact != null)
        {
            _interact.Enable();
            _interact.started += OnDescendPressed;

            // 最初のバインディングに Press(behavior=1) を付けてトグル的に扱う
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
        // イベント登録解除 & 無効化
        if (_pushAction != null)
        {
            _pushAction.performed -= OnPushPressed;
            _pushAction.canceled -= OnPushReleased;
            _pushAction.Disable();
        }
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

    // 最後に反映した UI テキスト
    private string _lastUiText = null;

    // ========================
    // Update
    // ========================
    private void Update()
    {
        // 登りアニメ中は押す処理などを止める
        if (_isClimbing) return;

        // 押している間は椅子を前方へ移動
        if (_isPushing && _pushingTransform != null)
            _pushingTransform.position += _pushDir * _pushSpeed * Time.deltaTime;

        if (_rayOrigin == null) return;

        // UI テキストの更新
        if (_pushTextMeshPro != null)
        {
            string nextText = "";

            if (_state == MountState.Grounded)
            {
                // Grounded 中だけ、「椅子タグ付きオブジェクト」に当たっているときにテキストを出す
                Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);

                if (Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
                {
                    // レイヤーだけでなく、タグ（_chairTag）を持つ親まであるか確認
                    Transform chairRoot = ResolveChairRoot(hitInfo);

                    if (chairRoot != null)
                    {
                        // 椅子に当たっているときだけ表示
                        nextText = "左クリック:押す / 右クリック or DoorOpen:乗る";
                    }
                    else
                    {
                        // 同じレイヤーでも Chair タグを持っていないなら非表示
                        nextText = "";
                    }
                }
                else
                {
                    nextText = "";
                }
            }
            else if (_state == MountState.Mounted)
            {
                // 乗っている間は「降りる」ガイドのみ
                nextText = "Interact: 降りる";
            }

            if (_lastUiText != nextText)
            {
                _pushTextMeshPro.SetText(nextText);
                _lastUiText = nextText;
            }
        }
    }

    // ========================
    // LateUpdate
    // ========================
    private void LateUpdate()
    {
        // 右クリック / DoorOpen のラッチ解除（押しっぱなし判定をリセット）
        if (_rightClick != null && !_rightClick.IsPressed()) _toggleLatched = false;
        if (_doorOpen != null && !_doorOpen.IsPressed()) _toggleLatched = false;

        // 椅子上ホールド中は Wrapper の位置をロック
        if (!_isClimbing && _holdRaisedPos && _wrapper != null)
        {
            _wrapper.position = _lockedRaisedPos;
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= _holdAfterClimbSec) _holdRaisedPos = false;
        }
    }

    // ========================
    // 押す関連
    // ========================
    private void OnPushPressed(InputAction.CallbackContext _)
    {
        if (_isClimbing || _rayOrigin == null) return;

        // Raycast で椅子を検出
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
                    // 押している間はキネマティックにして手動で動かす
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
        // 押すの終了：Rigidbody を元の isKinematic に戻す
        if (_pushingRb != null) _pushingRb.isKinematic = _originalKinematic;
        _pushingRb = null;
        _pushingTransform = null;
        _isPushing = false;
    }

    // 押している最中に対象が変わってしまった場合のチェック
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

    // ========================
    // 上る（Climb）関連
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

    // 上り開始可能かを判定するゲート
    private bool CanStartClimbGate(out string reason)
    {
        if (_playerAnimator == null || _rayOrigin == null) { reason = "Animator または RayOrigin が null"; return false; }
        if (_toggleLatched) { reason = "すでにトグル入力がラッチされている"; return false; }
        if (Time.time < _nextAcceptTime) { reason = "クールダウン中"; return false; }
        if (!_canAcceptToggle) { reason = "グローバルゲートが閉じている"; return false; }
        if (_playerAnimator.IsInTransition(_climbLayer)) { reason = "Animator が遷移中"; return false; }
        if (_state != MountState.Grounded) { reason = $"状態が Grounded ではない({_state})"; return false; }

        // 目の前に椅子があるかどうか（ここではレイヤーでヒット、タグは ResolveChairRoot で見る）
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        if (!Physics.Raycast(ray, out var hitInfo, _pushDistance, _detectMask, QueryTriggerInteraction.Ignore))
        { reason = "Raycast がヒットしなかった"; return false; }
        if (ResolveChairRoot(hitInfo) == null)
        { reason = "Raycast ヒットしたが Chair タグが見つからない"; return false; }

        reason = "OK"; return true;
    }

    // 実際に上り処理を開始する
    private void TryStartClimb(InputAction.CallbackContext ctx)
    {
        _toggleLatched = true;
        _nextAcceptTime = Time.time + _inputCooldownSec;

        // === 登り前のルート位置・回転を保存（降りるときに戻す） ===
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

        // === 目の前の椅子を再度 Raycast し、乗り始め位置・向きを補正 ===
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

        // Wrapper 作成（持ち上げ制御用の親）
        CreateWrapperIfNeeded(initialSetup: false);
        _canAcceptToggle = false;

        // === RayOrigin の状態を保存（降りるときに戻す） ===
        if (_rayOrigin != null)
        {
            _snapRayParent = _rayOrigin.parent;
            _snapRayLocalPos = _rayOrigin.localPosition;
            _snapRayLocalRot = _rayOrigin.localRotation;
            _snapRayLocalScale = _rayOrigin.localScale;
            _hasSnapRay = true;
        }

        // === 各コンポーネントの状態スナップ & Climb 中は移動系をロック ===
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

            // Climb 中は CC / NavMeshAgent を無効化して完全に位置ロック
            if (cc && cc.enabled)
            {
                cc.enabled = false;
                Log("ClimbStart: CharacterController disabled");
            }
            if (nma && nma.enabled)
            {
                nma.enabled = false;
                Log("ClimbStart: NavMeshAgent disabled");
            }
        }

        Log("ClimbStart");

        // 椅子上面へのスナップ位置を計算（必要な場合）
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

        // 押し中なら押す処理を終了しておく
        if (_isPushing) OnPushReleased(default);

        // === FBS を使って登り前の足元の高さを記録 ===
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

        // 通常の PlayerController は登り中は無効化
        if (_playerController != null && _playerController.enabled)
            _playerController.enabled = false;

        // 登りアニメ中の RootMotion 使用切り替え
        _playerAnimator.applyRootMotion = _useRootMotionForClimb;

        // 登りフラグを Animator に設定
        if (!string.IsNullOrEmpty(_climbBoolName))
            _playerAnimator.SetBool(_climbBoolHash, true);

        // 指定レイヤーにステートがあればそこへ CrossFade
        if (_playerAnimator.layerCount > _climbLayer && _playerAnimator.HasState(_climbLayer, _climbStateHash))
            _playerAnimator.CrossFadeInFixedTime(_climbStateHash, _crossFadeDur, _climbLayer);
        else
            Warn($"Animator state '{_climbStateName}' がレイヤー {_climbLayer} に存在しません。CrossFade をスキップします。");

        _isClimbing = true;
        _elapsedClimb = 0f;
        _yRaised = false;
        _state = MountState.Climbing;

        DumpState("ClimbStart");
        StartCoroutine(ClimbRoutine());
    }

    // 登りアニメの進行を監視し、終了を待つコルーチン
    private IEnumerator ClimbRoutine()
    {
        float t = 0f;
        bool sawTag = false;

        // 一フレ遅らせてから監視開始
        yield return null;

        while (true)
        {
            _elapsedClimb += Time.deltaTime;
            t += Time.deltaTime;

            // RootMotion を使わない場合は Y 持ち上げを時間で制御
            if (!_useRootMotionForClimb && !_yRaised && _elapsedClimb >= _yRaiseTime)
            {
                var w = (_raiseTarget != null) ? _raiseTarget : (_playerAnimator ? _playerAnimator.transform : null);
                if (w != null)
                {
                    Vector3 p = w.position;
                    float targetY = _lockedRaisedPos == default ? (p.y + _yRaiseAdd) : _lockedRaisedPos.y;
                    w.position = new Vector3(p.x, targetY, p.z);
                    _lockedRaisedPos = w.position;
                    _holdRaisedPos = true;
                    _holdTimer = 0f;
                    _yRaised = true;
                }
            }

            // Animator の状態を見て、Tag 付きステートの終了 or fallback で抜ける
            if (_playerAnimator != null)
            {
                var st = _playerAnimator.GetCurrentAnimatorStateInfo(_climbLayer);
                if (st.IsTag(_climbTag))
                {
                    sawTag = true;
                    if (st.normalizedTime >= 1.0f) break;
                }
                else
                {
                    // 登り Tag を一度も見ていない状態で一定時間経過したら安全のため抜ける
                    if (!sawTag && t >= _fallbackAnimTime) break;
                }
            }
            yield return null;
        }

        // 登り終了時の FBS 再ベース
        RefreshFBS("ClimbEnd");
        if (_fbs != null) _fbs.RebaseFeetGround("ClimbEnd");

        // 登り終了後は RootMotion を OFF に戻す
        _playerAnimator.applyRootMotion = false;

        if (_fbs != null) _fbs.SetClimbOverride(false);

        _isClimbing = false;
        _state = MountState.Mounted; // 乗っている状態に遷移
        _canAcceptToggle = true;

        Log("ClimbEnd");
        DumpState("ClimbEnd");
    }

    // ========================
    // 降りる（Descend）関連
    // ========================
    private void OnDescendPressed(InputAction.CallbackContext ctx)
    {
        Log("Interact started");
        if (_playerAnimator == null) { Log("Descend rejected: Animator が null"); return; }
        if (!_canAcceptToggle) { Log("Descend rejected: グローバルゲートが閉じている"); return; }
        if (Time.time < _nextAcceptTime) { Log("Descend rejected: クールダウン中"); return; }

        if (_state == MountState.Mounted)
        {
            _nextAcceptTime = Time.time + _inputCooldownSec;
            if (_descendCo != null) StopCoroutine(_descendCo);
            _descendCo = StartCoroutine(DescendRoutine());
        }
        else
        {
            Log($"Descend rejected: state={_state}");
        }
    }

    // 降り処理のコルーチン
    private IEnumerator DescendRoutine()
    {
        _isDescending = true;
        _state = MountState.Descending;
        _canAcceptToggle = false;

        // 降り中は RootMotion を切る
        _playerAnimator.applyRootMotion = false;

        // 押している最中なら押す処理を終了
        if (_isPushing) OnPushReleased(default);

        // 登りフラグを OFF にして FBS にも通知
        if (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator != null)
            _playerAnimator.SetBool(_climbBoolHash, false);
        RefreshFBS("Descend");
        if (_fbs != null) _fbs.SetClimbOverride(false);

        // Wrapper を外し、通常の階層に戻す
        DemoteAndDestroyWrapper("Descend");

        // Root（CC / NMA / Rigidbody）を取得
        GetRoot(out var root);
        var rb = root ? root.GetComponent<Rigidbody>() : null;
        var cc = root ? root.GetComponent<CharacterController>() : null;
        var nma = root ? root.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;

        bool ccWasEnabled = cc && cc.enabled;
        bool nmaWasEnabled = nma && nma.enabled;

        // 物理系の速度をクリア & コントローラ類を一旦無効化
        if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (ccWasEnabled) cc.enabled = false;
        if (nmaWasEnabled) nma.enabled = false;

        // === 登り前に保存しておいたルート位置・回転に戻す ===
        {
            var moveRoot = ResolveMovementRoot();
            if (_hasPreClimbRootTR && moveRoot != null)
            {
                moveRoot.SetPositionAndRotation(_preClimbRootPos, _preClimbRootRot);
                Physics.SyncTransforms();
                Log($"Restore to PreClimb pos={_preClimbRootPos:F3}");
            }
        }

        // RayOrigin の親子関係・ローカル TR を復元
        if (_hasSnapRay && _rayOrigin != null)
        {
            _rayOrigin.SetParent(_snapRayParent, worldPositionStays: false);
            _rayOrigin.localPosition = _snapRayLocalPos;
            _rayOrigin.localRotation = _snapRayLocalRot;
            _rayOrigin.localScale = _snapRayLocalScale;
        }

        // Animator の RootMotion フラグを元に戻す
        if (_hasSnapAnimatorRM && _playerAnimator != null)
            _playerAnimator.applyRootMotion = _snapAnimatorApplyRM;

        Physics.SyncTransforms();

        // === キャラコンを再度有効化する順番調整（めり込み防止） ===
        if (cc) cc.enabled = _snapCCEnabled;
        LogTrace("Descend: CC ON");

        RefreshFBS("Descend-BeforeSnap");
        if (_fbs != null)
        {
            _fbs.bypassFramesAfterRebase = 0;
            _fbs.rebaseOnClimbEnd = false;
            _fbs.lockFramesAfterExternalSnap = Mathf.Max(_fbs.lockFramesAfterExternalSnap, 2);
        }

        // 登り前に記録した足元の地面 Y にスナップ
        if (_fbs != null && _hasPreClimbFeetGroundY)
        {
            _fbs.SetClimbOverride(false);
            if (!string.IsNullOrEmpty(_climbBoolName) && _playerAnimator != null)
                _playerAnimator.SetBool(_climbBoolHash, false);

            _fbs.SetGroundYAndSnap(_preClimbFeetGroundY, "Descend(PreClimbGroundY)");
            Physics.SyncTransforms();
            LogTrace($"Descend: SnapToGroundY={_preClimbFeetGroundY:F4}");
        }

        // 少しだけ上方向に Move して足元のめり込みを避ける
        const float epsilonUp = 0.005f;
        if (cc && cc.enabled && epsilonUp > 0f)
        {
            cc.Move(Vector3.up * epsilonUp);
            LogTrace("Descend: epsilonUp");
        }

        // NavMeshAgent の有効/無効を元に戻す
        if (nma) nma.enabled = _snapNMAEnabled;
        Physics.SyncTransforms();

        // 降り直後数フレームの状態を詳細ログ出し
        if (traceAfterDescendFrames > 0) StartCoroutine(TraceFramesAfterDescend(traceAfterDescendFrames));

        // 1フレーム待ってから PlayerController を再有効化
        yield return null;
        if (_playerController != null) _playerController.enabled = true;
        if (cc && cc.enabled) cc.Move(Vector3.zero);

        _isDescending = false;
        _isClimbing = false;
        _state = MountState.Grounded;
        _canAcceptToggle = true;
        _toggleLatched = false;

        // UI テキストクリア
        if (_pushTextMeshPro != null) { _lastUiText = null; _pushTextMeshPro.SetText(""); }

        Log("DescendEnd");
        DumpState("DescendEnd");
        _descendCo = null;

        // 最終的に Wrapper を再作成して状態を揃えておく
        CreateWrapperIfNeeded(initialSetup: false);
    }

    // 降り後数フレームのスナップを取る
    private IEnumerator TraceFramesAfterDescend(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Snapshot("TRACE PostDescend");
            yield return new WaitForEndOfFrame();
        }
    }

    // 状態スナップ用ログ
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
    // 補助メソッド群
    // ========================

    /// <summary>
    /// 椅子を基準に、インスペクタで指定したローカルオフセット＆回転でプレイヤーを整列させる
    /// </summary>
    private void AlignForClimb(Transform chairRoot)
    {
        if (chairRoot == null) return;

        var moveRoot = ResolveMovementRoot();
        if (moveRoot == null) return;

        // 椅子ローカル座標の _climbLocalOffset をワールド座標に変換
        Vector3 targetPos = chairRoot.TransformPoint(_climbLocalOffset);

        // Y だけは現在の足元と合わせたい場合はここで上書き
        Vector3 currentPos = moveRoot.position;
        targetPos.y = currentPos.y;

        moveRoot.position = targetPos;

        // 椅子の回転に対して、Y 軸回転だけオフセットを加える
        Quaternion baseRot = chairRoot.rotation;
        Quaternion yawOffset = Quaternion.Euler(0f, _climbYawOffsetDeg, 0f);
        Quaternion finalRot = baseRot * yawOffset;

        moveRoot.rotation = finalRot;

        Physics.SyncTransforms();
        Log($"AlignForClimbInspector: pos={targetPos:F3}, yawOffset={_climbYawOffsetDeg}");
    }

    /// <summary>
    /// 登り用の Wrapper を生成（ない場合のみ）
    /// </summary>
    private void CreateWrapperIfNeeded(bool initialSetup)
    {
        if (_playerAnimator == null) return;
        if (_raiseTarget == null) _raiseTarget = _playerAnimator.transform;

        // すでに Wrapper 配下なら何もしない
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

    /// <summary>
    /// Wrapper を外し、子を元の親階層に戻す
    /// </summary>
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

    /// <summary>
    /// 実際に移動させるルート Transform を解決する
    /// （CharacterController / Rigidbody / NavMeshAgent を持つ Transform を上側へ遡って探す）
    /// </summary>
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

    /// <summary>
    /// RaycastHit から椅子（ChairTag）を持つルート Transform を解決する
    /// </summary>
    private Transform ResolveChairRoot(RaycastHit hitInfo)
    {
        Transform t = hitInfo.rigidbody ? hitInfo.rigidbody.transform : hitInfo.collider.transform;
        while (t != null && !t.CompareTag(_chairTag)) t = t.parent;
        return t;
    }

    /// <summary>
    /// Wrapper の親を Root として取得する
    /// </summary>
    private bool GetRoot(out Transform root)
    {
        root = null;
        if (_wrapper == null) return false;
        root = _wrapper.parent != null ? _wrapper.parent : _wrapper;
        return root != null;
    }

    /// <summary>
    /// ForceBoneScale の参照を探し、ログを出す
    /// </summary>
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

    // Gizmos で Raycast の範囲を可視化
    private void OnDrawGizmosSelected()
    {
        if (_rayOrigin == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
    }
}
