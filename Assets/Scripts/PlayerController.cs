using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    // ===== Input =====
    private InputSystem_Actions Input;

    // ===== Refs =====
    [Header("Refs")]
    [SerializeField] Transform Cam;                 // 未設定なら Camera.main
    [SerializeField] Animator animator;
    [SerializeField] TPSCamera tpsCamera;           // 任意（ログ用）
    [SerializeField] AudioManager audioManager;     // 任意（足音）

    // ===== Speed =====
    [Header("Speed")]
    [SerializeField] float MoveSpeed = 5f;
    [SerializeField] float DashSpeed = 7f;
    [SerializeField] float SlowSpeed = 2f;

    // ===== Input thresholds =====
    [Header("Input thresholds")]
    [SerializeField, Range(0f, 1f)] float analogPressPoint = 0.5f;
    [SerializeField, Range(0f, 1f)] float deadZone = 0.12f;     // 軸デッドゾーン
    [SerializeField, Range(0f, 0.3f)] float tieEpsilon = 0.08f; // 同点ブレ抑制
    [SerializeField] float stopGrace = 0.08f;                    // Idle への猶予

    // ===== Movement step (tunneling対策) =====
    [Header("Motion Split")]
    [SerializeField, Min(0.01f)] float maxStep = 0.2f; // 1サブステップの最大距離

    // ===== Collision (Rigidbodyなし) =====
    [Header("Block-on-Hit (Furniture専用)")]
    [SerializeField] LayerMask furnitureMask;  // ← Furniture レイヤーのみを指定
    [SerializeField] float skin = 0.02f;       // 計算安定用の余白
    [SerializeField] bool lockY = true;        // 常に初期Yを維持

    // カプセル（CapsuleCollider が無い場合の代替）
    [Header("Capsule (fallback)")]
    [SerializeField] float capsuleHeight = 1.7f;
    [SerializeField] float capsuleRadius = 0.3f;
    [SerializeField] Vector3 capsuleCenter = new Vector3(0f, 0.9f, 0f);

    // 誤検知緩和・寄せ具合のノブ
    [Header("Cast Tweaks")]
    [SerializeField, Range(0f, 1f)] float groundNormalY = 0.6f; // これ以上のY法線は床扱い
    [SerializeField] float topTrim = 0.02f;     // 頭側のキャストを少し短く
    [SerializeField] float bottomTrim = 0.08f;  // 足側のキャストを少し短く

    [Tooltip("接触直前に残すクリアランス。小さいほど壁に寄れる")]
    [SerializeField] float approachBuffer = 0.005f;

    [Tooltip("カプセル半径から差し引いて『細身に』キャスト。大きいほど近寄れる")]
    [SerializeField] float probeShrink = 0.02f;

    // ===== 状態 =====
    private int _dominantAxis = 0; // 0=未, 1=X, 2=Y(=前後)
    private float _noInputTimer = 0f;
    private bool _prevDash = false;
    private bool _footLoopOn = false;

    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private float _fixedY;

    private CapsuleCollider _cap;

    // ===== 公開状態 & イベント =====
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();

    // ===== Debug =====
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
        // ---- 入力取得
        Vector2 moveRaw = Input.Player.Move.ReadValue<Vector2>(); // -1..1
        bool slowHeld = Input.Player.SlowWalk.ReadValue<float>() >= analogPressPoint;
        bool dashHeld = Input.Player.Dash.ReadValue<float>() >= analogPressPoint;

        // 1軸だけ採用
        Vector2 moveSnap = SnapOneAxis(moveRaw);
        bool hasInput = (moveSnap.x != 0f) || (moveSnap.y != 0f);

        if (!hasInput)
        {
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

        // ---- yaw のみでワールド変換（ピッチ無視）
        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        // ---- スピード（ダッシュは前進時のみ）
        bool isBackward = moveSnap.y < 0f;
        float speed = SlowSpeed;
        bool isDashing = false;
        if (!slowHeld)
        {
            if (!isBackward && dashHeld) { speed = DashSpeed; isDashing = true; }
            else { speed = MoveSpeed; }
        }

        // ==== サブステップ移動（部分前進：ヒット手前まで寄せる）====
        Vector3 desired = moveDir * speed * Time.deltaTime;
        float remaining = desired.magnitude;
        Vector3 stepDir = (remaining > 1e-6f) ? (desired / remaining) : Vector3.zero;
        int steps = Mathf.Max(1, Mathf.CeilToInt(remaining / maxStep));
        float stepLen = remaining / steps;

        bool moved = false;
        for (int i = 0; i < steps; i++)
        {
            if (stepLen < 1e-6f) break;

            Vector3 stepDelta = stepDir * stepLen;

            // 家具に当たりそう？
            if (IsBlockedByFurniture(stepDelta, out float hitDist))
            {
                // ヒット手前まで寄せて終了（早止まりを抑える）
                float advance = Mathf.Clamp(hitDist - approachBuffer, 0f, stepLen);
                if (advance > 1e-5f)
                {
                    Vector3 newPosA = transform.position + stepDir * advance;
                    if (lockY) newPosA.y = _fixedY;
                    transform.position = newPosA;
                    moved = true;
                }
                break; // これ以上は進まない
            }
            else
            {
                // そのまま進む
                Vector3 newPosB = transform.position + stepDelta;
                if (lockY) newPosB.y = _fixedY;
                transform.position = newPosB;
                moved = true;
            }
        }

        // 後退以外で向き合わせ
        if (moved && !isBackward)
            transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);

        // ---- ロック更新
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        // ---- Animator
        if (animator)
        {
            animator.SetBool("IsMoving", moved);
            animator.SetBool("IsDashing", moved && isDashing);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        // ---- 公開状態＆イベント
        IsMovingNow = moved;
        IsDashingNow = moved && isDashing;
        IsSlowWalkingNow = slowHeld;

        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        ToggleFootLoop(moved);

        // ---- Debug（S押下時）
        if (EnableBackLog && Keyboard.current != null && Keyboard.current.sKey.isPressed)
            LogBackCheck(moveRaw, moveSnap, moveDir, yaw);
    }

    // ===== 1方向だけ動く：軸デッドゾーン→強い軸のみ採用（同点は前回維持）
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

    void ToggleFootLoop(bool on)
    {
        if (audioManager == null) return;
        if (on && !_footLoopOn) { audioManager.StartFootstepLoop(); _footLoopOn = true; }
        else if (!on && _footLoopOn) { audioManager.StopFootstepLoop(); _footLoopOn = false; }
    }

    // ===== Furniture への“事前衝突”チェック（CapsuleCast）
    bool IsBlockedByFurniture(Vector3 delta, out float hitDist)
    {
        hitDist = 0f;
        if (delta.sqrMagnitude < 1e-10f) return false;

        Vector3 dir = delta.normalized;
        float dist = delta.magnitude;

        GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r);

        // 少し上下を削って床/天井の誤検知を減らす
        p1 -= Vector3.up * topTrim;
        p2 += Vector3.up * bottomTrim;

        // 細身キャスト：寄りやすくする
        float castR = Mathf.Max(0.005f, r - Mathf.Max(0f, probeShrink));

        // 自分レイヤーは除外
        int mask = furnitureMask & ~(1 << gameObject.layer);

        if (Physics.CapsuleCast(p1, p2, castR, dir, out RaycastHit hit, dist + Mathf.Max(0f, approachBuffer),
            mask, QueryTriggerInteraction.Ignore))
        {
            // 自分は除外
            if (hit.collider && (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)))
                return false;

            // 床扱いは無視（必要に応じて調整）
            if (hit.normal.y >= groundNormalY) return false;

            hitDist = Mathf.Max(0f, hit.distance);
            return true; // 前方に Furniture → ブロック
        }
        return false;
    }

    // ===== Capsule をワールド実寸で取得（Scale対応）
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
            p1 = c + Vector3.up * half;
            p2 = c - Vector3.up * half;
        }
        else
        {
            Vector3 c = transform.TransformPoint(capsuleCenter);
            float height = Mathf.Max(capsuleHeight * scaleY, capsuleRadius * 2f * scaleXZ);
            r = capsuleRadius * scaleXZ;
            float half = Mathf.Max(0f, height * 0.5f - r);
            p1 = c + Vector3.up * half;
            p2 = c - Vector3.up * half;
        }
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
            $"| angle(moveDir,-camF)={ang:0.0} dot={dot:0.000} => MoveOK={(okMove ? "OK" : "NG")}"
        );
    }
}
