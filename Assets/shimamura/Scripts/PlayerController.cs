using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    // ===== Input =====
    private InputSystem_Actions Input;

    // ===== Refs =====
    [Header("Refs")]
    [SerializeField] Transform Cam;
    [SerializeField] Animator animator;
    [SerializeField] TPSCamera tpsCamera;
    [SerializeField] AudioManager audioManager;

    // ===== Move =====
    [Header("Speed")]
    [SerializeField] float MoveSpeed = 5f;
    [SerializeField] float DashSpeed = 7f;
    [SerializeField] float SlowSpeed = 2f;

    [Header("Input thresholds")]
    [SerializeField, Range(0f, 1f)] float analogPressPoint = 0.5f;
    [SerializeField, Range(0f, 1f)] float deadZone = 0.12f;
    [SerializeField, Range(0f, 0.3f)] float tieEpsilon = 0.08f;
    [SerializeField] float stopGrace = 0.08f;

    [Header("Motion Split")]
    [SerializeField, Min(0.01f)] float maxStep = 0.2f; // 1サブステップ最大距離

    // ===== Collision (Overlap) =====
    [Header("Overlap Block (Furnitureのみ)")]
    [SerializeField] LayerMask furnitureMask;     // ドア/壁/家具のレイヤーだけ触れるように
    [SerializeField] bool lockY = true;

    [Header("Capsule (fallback)")]
    [SerializeField] float capsuleHeight = 1.7f;
    [SerializeField] float capsuleRadius = 0.3f;
    [SerializeField] Vector3 capsuleCenter = new Vector3(0f, 0.9f, 0f);

    [Header("Overlap Tweaks")]
    [Tooltip("Overlap判定用に半径をわずかに細く。0〜0.005 推奨")]
    [SerializeField] float overlapShrink = 0.002f;
    [Tooltip("頭/足の誤反応を減らすため上下を少し削る")]
    [SerializeField] float topTrim = 0.02f, bottomTrim = 0.08f;

    // ===== Sweep/Slide =====
    [Header("Sweep/Slide")]
    [SerializeField] float skin = 0.01f;                       // 壁手前で止める余白
    [SerializeField, Range(0f, 1f)] float slideFactor = 1.0f;  // 残り距離に対するスライド反映率

    // ===== 切り返し/微小移動対策 =====
    [Header("Turn / Micro-move")]
    [Tooltip("右↔左や前↔後の“符号反転フレーム”はスライドを無効化")]
    [SerializeField] bool disableSlideOnFlip = true;
    [Tooltip("フレーム移動量がこの値未満なら位置を元に戻す")]
    [SerializeField] float microMoveEps = 0.0025f;
    [Tooltip("衝突で移動できなくても入力があれば回頭だけは行う")]
    [SerializeField] bool rotateEvenIfBlocked = true;

    // ===== 回頭スムージング =====
    [Header("Rotation (Turn Smoothing)")]
    [Tooltip("1秒に何度回れるか（360〜900あたりで調整）")]
    [SerializeField] float turnSpeedDeg = 540f;

    // ===== Block Logging =====
    [Header("Block Logging")]
    [SerializeField] bool logBlockObjects = false;
    [SerializeField, Min(1)] int blockLogEveryNFrames = 10;

    // ===== State =====
    private int _dominantAxis = 0; // 0=未, 1=X, 2=Y
    private float _noInputTimer = 0f;
    private bool _prevDash = false;
    private bool _footLoopOn = false;
    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private float _fixedY;
    private CapsuleCollider _cap;

    // 入力方向の符号記録（反転検出用）
    private int _lastSignX = 0, _lastSignY = 0;

    // 公開（他スクリプト互換）
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();

    // temp buffers
    private static readonly Collider[] _overlapBuf = new Collider[16];

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
        // ===== 入力 =====
        Vector2 moveRaw = Input.Player.Move.ReadValue<Vector2>();
        bool slowHeld = Input.Player.SlowWalk.ReadValue<float>() >= analogPressPoint;
        bool dashHeld = Input.Player.Dash.ReadValue<float>() >= analogPressPoint;

        Vector2 moveSnap = SnapOneAxis(moveRaw);
        bool hasInput = (moveSnap.x != 0f) || (moveSnap.y != 0f);

        if (!hasInput)
        {
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);
            if (lockY) transform.position = new Vector3(transform.position.x, _fixedY, transform.position.z);

            _noInputTimer += Time.deltaTime;
            if (_noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                animator.SetBool("IsMoving", false);
            if (animator) { animator.SetBool("IsDashing", false); animator.SetBool("IsSlowWalking", false); }

            IsMovingNow = IsDashingNow = IsSlowWalkingNow = false;
            if (_prevDash) { OnDashEnd.Invoke(); _prevDash = false; }
            ToggleFootLoop(false);
            _dominantAxis = 0;
            _lastSignX = 0; _lastSignY = 0;
            return;
        }
        _noInputTimer = 0f;

        // 符号反転検出
        int signX = (moveSnap.x > 0f) ? 1 : (moveSnap.x < 0f ? -1 : 0);
        int signY = (moveSnap.y > 0f) ? 1 : (moveSnap.y < 0f ? -1 : 0);
        bool flipped = (signX != 0 && _lastSignX != 0 && signX != _lastSignX)
                    || (signY != 0 && _lastSignY != 0 && signY != _lastSignY);

        // 方向
        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        // 速度
        bool isBackward = moveSnap.y < 0f;
        float speed = slowHeld ? SlowSpeed : (dashHeld && !isBackward ? DashSpeed : MoveSpeed);
        bool isDashing = (!slowHeld && dashHeld && !isBackward);

        // ===== サブステップ移動 =====
        Vector3 frameStart = transform.position; // 微小移動キャンセル用に保持
        Vector3 desired = moveDir * speed * Time.deltaTime;
        float remain = desired.magnitude;
        bool movedThisFrame = false;

        if (remain > 0f)
        {
            Vector3 dir = desired / remain;
            int steps = Mathf.Max(1, Mathf.CeilToInt(remain / maxStep));
            float stepLen = remain / steps;

            for (int i = 0; i < steps; i++)
            {
                Vector3 startPos = transform.position;
                Vector3 targetPos = startPos + dir * stepLen;
                if (lockY) targetPos.y = _fixedY;

                // 1回目：目標へスイープ
                if (!TrySweepTo(startPos, targetPos, out Vector3 stopPos, out RaycastHit hit))
                {
                    // ログ（任意）
                    if (logBlockObjects && Time.frameCount % blockLogEveryNFrames == 0)
                    {
                        Vector3 moveDirLog = (targetPos - startPos).normalized;
                        LogBlock(stopPos, 1, hit.collider, moveDirLog);
                    }

                    // 当たった → 止め位置まで進める
                    transform.position = stopPos;

                    // 残り距離（純粋な未到達分のみ）
                    float traveled = (stopPos - startPos).magnitude;
                    float leftover = Mathf.Max(0f, stepLen - traveled);

                    // 方向反転フレームはスライド無効化
                    if (disableSlideOnFlip && flipped) leftover = 0f;

                    // 残りを壁面へ投影して二度目のスイープ（壁沿いスライド）
                    if (leftover > 1e-5f)
                    {
                        Vector3 n = hit.normal;
                        Vector3 slideDir = Vector3.ProjectOnPlane(dir, n).normalized;
                        if (slideDir.sqrMagnitude > 1e-6f)
                        {
                            Vector3 slideTarget = stopPos + slideDir * (leftover * slideFactor);
                            if (lockY) slideTarget.y = _fixedY;

                            // 2回目
                            TrySweepTo(stopPos, slideTarget, out Vector3 slidPos, out _);
                            if ((slidPos - stopPos).sqrMagnitude > 1e-8f)
                            {
                                transform.position = slidPos;
                                movedThisFrame = true;
                            }
                        }
                    }
                }
                else
                {
                    // 当たらず到達
                    transform.position = stopPos;
                    movedThisFrame = true;
                }
            }
        }

        // 微小移動カット（視覚的“ピクッ”抑止）
        float frameMoved = (transform.position - frameStart).magnitude;
        if (frameMoved < microMoveEps)
        {
            transform.position = frameStart;
            movedThisFrame = false;
        }

        // ===== スムーズ回頭（RotateTowards） =====
        bool canRotateThisFrame = !isBackward && (movedThisFrame || rotateEvenIfBlocked);
        if (canRotateThisFrame && moveDir.sqrMagnitude > 1e-6f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                turnSpeedDeg * Time.deltaTime
            );
        }

        // lock & anim
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        if (animator)
        {
            // 衝突で実際は動けなくても、入力がある限りIdleに戻さない
            animator.SetBool("IsMoving", hasInput);
            animator.SetBool("IsDashing", hasInput && isDashing && movedThisFrame);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        // 公開状態
        IsMovingNow = movedThisFrame;
        IsDashingNow = movedThisFrame && isDashing;
        IsSlowWalkingNow = slowHeld;
        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        ToggleFootLoop(movedThisFrame); // 壁押し中は足音オフ

        // 符号の記録
        _lastSignX = signX; _lastSignY = signY;
    }

    // --- 1方向限定（上下左右どちらか一方のみ通す）
    Vector2 SnapOneAxis(Vector2 raw)
    {
        float ax = Mathf.Abs(raw.x), ay = Mathf.Abs(raw.y);
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

    void ToggleFootLoop(bool on)
    {
        if (audioManager == null) return;
        if (on && !_footLoopOn) { audioManager.StartFootstepLoop(); _footLoopOn = true; }
        else if (!on && _footLoopOn) { audioManager.StopFootstepLoop(); _footLoopOn = false; }
    }

    // --- ログ
    void LogBlock(Vector3 nextPos, int hitCount, Collider first, Vector3 moveDir)
    {
        if (!first) return;
        string layer = LayerMask.LayerToName(first.gameObject.layer);
        float approxDist = ApproxPenetration(nextPos, first);
        Debug.Log(
            $"[Block] 停止: \"{first.name}\" (Layer={layer}) hits={hitCount} approxPenetration={approxDist:0.000}m dir=({moveDir.x:0.00},{moveDir.z:0.00})",
            first
        );
    }

    float ApproxPenetration(Vector3 nextPos, Collider other)
    {
        if (_cap != null)
        {
            Vector3 posA = nextPos;
            Quaternion rotA = transform.rotation;
            Vector3 direction; float distance;
            if (Physics.ComputePenetration(
                    _cap, posA, rotA,
                    other, other.transform.position, other.transform.rotation,
                    out direction, out distance))
            {
                return distance; // 離すのに必要な距離
            }
        }
        Vector3 p = other.ClosestPoint(nextPos);
        return Mathf.Max(0f, (nextPos - p).magnitude);
    }

    // --- カプセル形状（現在位置）
    void GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r)
    {
        var s = transform.lossyScale;
        float scaleY = Mathf.Abs(s.y);
        float scaleXZ = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));

        if (_cap != null)
        {
            Vector3 c = transform.TransformPoint(_cap.center);
            float height = Mathf.Max(_cap.height * scaleY, _cap.radius * 2f * scaleXZ);
            r = _cap.radius * scaleXZ;
            float half = Mathf.Max(0f, height * 0.5f - r);
            p1 = c + Vector3.up * (half - topTrim);
            p2 = c - Vector3.up * (half - bottomTrim);
        }
        else
        {
            Vector3 c = transform.TransformPoint(capsuleCenter);
            float height = Mathf.Max(capsuleHeight * scaleY, capsuleRadius * 2f * scaleXZ);
            r = capsuleRadius * scaleXZ;
            float half = Mathf.Max(0f, height * 0.5f - r);
            p1 = c + Vector3.up * (half - topTrim);
            p2 = c - Vector3.up * (half - bottomTrim);
        }
    }

    // --- 経路スイープ（CapsuleCast）
    bool TrySweepTo(Vector3 fromPos, Vector3 toPos, out Vector3 hitStopPos, out RaycastHit hitInfo)
    {
        hitInfo = default;
        hitStopPos = toPos;

        GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r);

        Vector3 dir = toPos - fromPos;
        float dist = dir.magnitude;
        if (dist <= 0f) return true;

        dir /= dist;
        float rAdj = Mathf.Max(0.001f, r - Mathf.Max(0f, overlapShrink));

        if (Physics.CapsuleCast(p1, p2, rAdj, dir, out RaycastHit hit, dist, furnitureMask, QueryTriggerInteraction.Ignore))
        {
            float stopDist = Mathf.Max(0f, hit.distance - skin);
            hitStopPos = fromPos + dir * stopDist;
            hitInfo = hit;
            return false; // 途中で止まった
        }

        return true; // ぶつからず到達
    }
}
