using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    // ===== Input =====
    private InputSystem_Actions Input;

    // ===== Refs / Params =====
    [Header("Refs")]
    [SerializeField] Transform Cam;               // 未設定なら Camera.main を使用
    [SerializeField] Animator animator;
    [SerializeField] TPSCamera tpsCamera;         // 任意（ログ用）
    [SerializeField] AudioManager audioManager;   // 任意（足音）

    [Header("Speed")]
    [SerializeField] float MoveSpeed = 5f;
    [SerializeField] float DashSpeed = 7f;
    [SerializeField] float SlowSpeed = 2f;

    [Header("Input thresholds")]
    [SerializeField, Range(0f, 1f)] float analogPressPoint = 0.5f;
    [SerializeField, Range(0f, 1f)] float deadZone = 0.12f;     // 軸デッドゾーン
    [SerializeField, Range(0f, 0.3f)] float tieEpsilon = 0.08f; // 同点ブレ抑制
    [SerializeField] float stopGrace = 0.08f;                    // Idle 遷移猶予

    [Header("Collision (Rigidbodyなし)")]
    [SerializeField] LayerMask collisionMask = ~0;     // 壁/床のレイヤーのみ推奨（自分は外す）
    [SerializeField] float skin = 0.02f;               // 壁からの余白
    [SerializeField] bool lockY = true;                // 常に初期Yに固定
    [SerializeField] float capsuleHeight = 1.7f;       // CapsuleCollider が無い時の代替
    [SerializeField] float capsuleRadius = 0.3f;       // CapsuleCollider が無い時の代替
    [SerializeField] Vector3 capsuleCenter = new Vector3(0, 0.9f, 0); // 代替センター

    // 状態
    private int _dominantAxis = 0; // 0=未, 1=X, 2=Y(=前後)
    private float _noInputTimer = 0f;
    private bool _prevDash = false;
    private bool _footLoopOn = false;

    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private float _fixedY; // lockY 用

    private CapsuleCollider _cap;

    // ===== 公開状態 & イベント =====
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();

    // Debug
    [Header("Debug")]
    [SerializeField] bool EnableBackLog = true;
    [SerializeField] int LogEveryNFrames = 1;

    void Awake()
    {
        Input = new InputSystem_Actions();
        _cap = GetComponent<CapsuleCollider>();
    }

    void OnEnable()
    {
        Input.Player.Enable();
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;
        _fixedY = transform.position.y;
    }
    void OnDisable() => Input.Player.Disable();

    void Update()
    {
        // ==== 入力 ====
        Vector2 moveRaw = Input.Player.Move.ReadValue<Vector2>(); // -1..1
        bool slowHeld = Input.Player.SlowWalk.ReadValue<float>() >= analogPressPoint;
        bool dashHeld = Input.Player.Dash.ReadValue<float>() >= analogPressPoint;

        // 1軸だけ採用（WASDどれか1方向）
        Vector2 moveSnap = SnapOneAxis(moveRaw);
        bool hasInput = (moveSnap.x != 0f) || (moveSnap.y != 0f);

        if (!hasInput)
        {
            // 入力なし：微小ドリフト止め
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);
            if (lockY) transform.position = new Vector3(transform.position.x, _fixedY, transform.position.z);

            _noInputTimer += Time.deltaTime;
            if (_noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                animator.SetBool("IsMoving", false);
            if (animator)
            {
                animator.SetBool("IsDashing", false);
                animator.SetBool("IsSlowWalking", false);
            }

            IsMovingNow = false;
            IsDashingNow = false;
            IsSlowWalkingNow = false;

            if (_prevDash) { OnDashEnd.Invoke(); _prevDash = false; }

            ToggleFootLoop(false);
            _dominantAxis = 0;
            return;
        }
        _noInputTimer = 0f;

        // ==== yaw のみでワールド変換 ====
        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        // ==== スピード ====
        bool isBackward = moveSnap.y < 0f;
        float speed = SlowSpeed;
        bool isDashing = false;
        if (!slowHeld)
        {
            if (!isBackward && dashHeld) { speed = DashSpeed; isDashing = true; }
            else { speed = MoveSpeed; }
        }

        // ==== 移動（キャストで事前判定、当たるなら進まない） ====
        Vector3 desiredDelta = moveDir * speed * Time.deltaTime;
        bool blocked = IsBlocked(desiredDelta, out float hitDist);

        bool moved = false;
        if (!blocked)
        {
            Vector3 newPos = transform.position + desiredDelta;
            if (lockY) newPos.y = _fixedY;
            transform.position = newPos;

            // 回転：後退以外のみ
            if (!isBackward && desiredDelta.sqrMagnitude > 0f)
                transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);

            moved = true;
        }
        else
        {
            // ぶつかっているので移動しない（スライド無し仕様）
            moved = false;
        }

        // ==== ロック更新 ====
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        // ==== Animator ====
        if (animator)
        {
            animator.SetBool("IsMoving", moved);
            animator.SetBool("IsDashing", moved && isDashing);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        // ==== 公開状態 & イベント ====
        IsMovingNow = moved;
        IsDashingNow = moved && isDashing;
        IsSlowWalkingNow = slowHeld;

        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        // 足音は実際に動いた時だけ
        ToggleFootLoop(moved);

        // Debug ログ（S押下時）
        if (EnableBackLog && Keyboard.current != null && Keyboard.current.sKey.isPressed)
            LogBackCheck(moveRaw, moveSnap, moveDir, yaw);
    }

    // ==== “1方向だけ動く”：軸デッドゾーン→強い軸のみ採用 ====
    Vector2 SnapOneAxis(Vector2 raw)
    {
        float ax = Mathf.Abs(raw.x);
        float ay = Mathf.Abs(raw.y);

        float x = (ax >= deadZone) ? Mathf.Sign(raw.x) : 0f;
        float y = (ay >= deadZone) ? Mathf.Sign(raw.y) : 0f;

        if (x == 0f && y == 0f) { _dominantAxis = 0; return Vector2.zero; }

        if (ax > ay + tieEpsilon) _dominantAxis = 1;
        else if (ay > ax + tieEpsilon) _dominantAxis = 2;
        else if (_dominantAxis == 0) _dominantAxis = (ay >= ax) ? 2 : 1;

        return (_dominantAxis == 1) ? new Vector2(x, 0f) : new Vector2(0f, y);
    }

    float GetYaw()
    {
        Transform src = Cam ? Cam : (Camera.main ? Camera.main.transform : transform);
        return src.eulerAngles.y;
    }

    // ==== Rigidbodyなしの衝突ブロック（カプセルキャスト）====
    bool IsBlocked(Vector3 delta, out float hitDist)
    {
        hitDist = 0f;
        if (delta.sqrMagnitude < 1e-10f) return false;

        Vector3 dir = delta.normalized;
        float dist = delta.magnitude;

        GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r);

        // 自分のレイヤーを collisionMask から除外しておくのが前提
        if (Physics.CapsuleCast(p1, p2, r - skin, dir, out RaycastHit hit, dist + skin,
                                collisionMask, QueryTriggerInteraction.Ignore))
        {
            hitDist = hit.distance;
            return true;
        }
        return false;
    }

    void GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r)
    {
        if (_cap != null)
        {
            // ※ Y 方向カプセル前提（一般的なプレイヤー想定）
            Vector3 c = transform.TransformPoint(_cap.center);
            float height = Mathf.Max(_cap.height, _cap.radius * 2f);
            r = _cap.radius;
            float half = Mathf.Max(0f, height * 0.5f - r);
            p1 = c + Vector3.up * half;
            p2 = c - Vector3.up * half;
        }
        else
        {
            // 代替パラメータ
            Vector3 c = transform.TransformPoint(capsuleCenter);
            float height = Mathf.Max(capsuleHeight, capsuleRadius * 2f);
            r = capsuleRadius;
            float half = Mathf.Max(0f, height * 0.5f - r);
            p1 = c + Vector3.up * half;
            p2 = c - Vector3.up * half;
        }
    }

    void ToggleFootLoop(bool on)
    {
        if (audioManager == null) return;
        if (on && !_footLoopOn) { audioManager.StartFootstepLoop(); _footLoopOn = true; }
        else if (!on && _footLoopOn) { audioManager.StopFootstepLoop(); _footLoopOn = false; }
    }

    // ===== Debug =====
    void LogBackCheck(Vector2 moveRaw, Vector2 moveSnap, Vector3 moveDir, float yawUsed)
    {
        if (Time.frameCount % Mathf.Max(1, LogEveryNFrames) != 0) return;

        Quaternion yawRot = Quaternion.Euler(0f, yawUsed, 0f);
        Vector3 camFh = yawRot * Vector3.forward;
        Vector3 expected = -camFh;

        float ang = moveDir.sqrMagnitude > 0f ? Vector3.Angle(moveDir, expected) : 999f;
        float dot = moveDir.sqrMagnitude > 0f ? Vector3.Dot(moveDir, expected) : -1f;
        bool okMove = (moveSnap.y < 0f && ang <= 12f && dot > 0.98f);

        string camStat = "Unknown";
        float distNow = -1f, distUsed = -1f, yaw = -999f, pitch = -999f;
        if (tpsCamera != null && Cam != null)
        {
            distNow = Vector3.Distance(Cam.position, tpsCamera.Pivot.position);
            distUsed = tpsCamera.CurrentDistance;
            yaw = tpsCamera.yaw; pitch = tpsCamera.pitch;
            camStat = Mathf.Abs(distNow - distUsed) <= 0.15f ? "OK" : "NG";
        }

        Debug.Log(
            $"[BackCheck] S-press | raw=({moveRaw.x:0.00},{moveRaw.y:0.00}) snap=({moveSnap.x:0.00},{moveSnap.y:0.00}) " +
            $"| camYaw={yaw:0.0} camPitch={pitch:0.0} | moveDir=({moveDir.x:0.000},{moveDir.z:0.000}) " +
            $"| angle(moveDir,-camF)={ang:0.0} dot={dot:0.000} => MoveOK={(okMove ? "OK" : "NG")} " +
            $"| camDistNow={distNow:0.00} used~{distUsed:0.00} => CamOK={camStat} | axis={_dominantAxis} tie={tieEpsilon:0.00}"
        );
    }
}
