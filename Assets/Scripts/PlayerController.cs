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
    [SerializeField, Range(0f, 1f)] float slideFactor = 0.7f;  // 接線スライド係数（ホラー基準は弱め）

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

    // 他スクリプト互換
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();

    // debug
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
        // 入力
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
            return;
        }
        _noInputTimer = 0f;

        // 方向
        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        // 速度
        bool isBackward = moveSnap.y < 0f;
        float speed = slowHeld ? SlowSpeed : (dashHeld && !isBackward ? DashSpeed : MoveSpeed);
        bool isDashing = (!slowHeld && dashHeld && !isBackward);

        // サブステップ前進（経路で当たったら止める＋接線へ滑る）
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

                if (!TrySweepTo(startPos, targetPos, out Vector3 stopPos, out RaycastHit hit))
                {
                    // ログ出力（任意）
                    if (logBlockObjects && Time.frameCount % blockLogEveryNFrames == 0)
                    {
                        Vector3 moveDirLog = (targetPos - startPos).normalized;
                        // Overlap由来のログ関数を流用
                        LogBlock(stopPos, 1, hit.collider, moveDirLog);
                    }

                    // 接線方向に1回だけ押し出し（弱めのスライド）
                    Vector3 v = targetPos - startPos;
                    Vector3 n = hit.normal;
                    Vector3 tangent = v - Vector3.Project(v, n);
                    float tangMag = Mathf.Max(0f, v.magnitude - hit.distance + skin);
                    Vector3 slide = (tangent.sqrMagnitude > 1e-6f)
                        ? tangent.normalized * tangMag * slideFactor
                        : Vector3.zero;

                    transform.position = stopPos; // まず停止位置へ
                    if (slide.sqrMagnitude > 1e-6f)
                    {
                        Vector3 slideTarget = stopPos + slide;
                        if (lockY) slideTarget.y = _fixedY;
                        // 二度目もスイープで安全に
                        TrySweepTo(stopPos, slideTarget, out Vector3 slid, out _);
                        if ((slid - startPos).sqrMagnitude > 1e-6f)
                        {
                            transform.position = slid;
                            movedThisFrame = true;
                        }
                    }
                    // これ以上は進まない
                    break;
                }

                // ぶつからず到達
                transform.position = stopPos;
                movedThisFrame = true;
            }
        }

        if (movedThisFrame && !isBackward)
            transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);

        // lock & anim
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        if (animator)
        {
            // 壁に押し当てて進めなくても、入力が出ている限りIdleに戻さない
            animator.SetBool("IsMoving", hasInput);
            animator.SetBool("IsDashing", hasInput && isDashing);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        // 公開状態
        IsMovingNow = movedThisFrame;
        IsDashingNow = movedThisFrame && isDashing;
        IsSlowWalkingNow = slowHeld;
        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        ToggleFootLoop(movedThisFrame); // 壁押し中は足音を止めたいので据え置き
    }

    // --- 1方向限定
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

    // --- 次位置で家具レイヤーと重なるか？（最初のヒットも返す）
    bool WouldOverlapFurnitureAt(Vector3 nextPos, out int count, out Collider firstHit)
    {
        firstHit = null;
        // 現在のワールド・カプセルを取得 → nextPos に平行移動
        GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r);
        Vector3 offset = nextPos - transform.position;
        p1 += offset; p2 += offset;

        float rAdj = Mathf.Max(0.001f, r - Mathf.Max(0f, overlapShrink));

        count = Physics.OverlapCapsuleNonAlloc(
            p1, p2, rAdj, _overlapBuf, furnitureMask, QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            var c = _overlapBuf[i];
            if (!c) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            firstHit = c;         // 何か1つでもあればブロック
            return true;
        }
        return false;
    }

    // --- ログ
    void LogBlock(Vector3 nextPos, int hitCount, Collider first, Vector3 moveDir)
    {
        if (!first) return;

        string layer = LayerMask.LayerToName(first.gameObject.layer);
        float approxDist = ApproxPenetration(nextPos, first);
        Debug.Log(
            $"[Block] 停止: \"{first.name}\" (Layer={layer}) " +
            $"hits={hitCount} approxPenetration={approxDist:0.000}m dir=({moveDir.x:0.00},{moveDir.z:0.00})",
            first
        );
    }

    // Overlap結果からの「だいたいのめり込み量」
    float ApproxPenetration(Vector3 nextPos, Collider other)
    {
        // CapsuleCollider があるなら ComputePenetration で正確に
        if (_cap != null)
        {
            Vector3 posA = nextPos;                      // 次位置のルート
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
        // 相手の ClosestPoint との距離で近さを見る
        Vector3 p = other.ClosestPoint(nextPos);
        return Mathf.Max(0f, (nextPos - p).magnitude);
    }

    // --- 実寸カプセル
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

    // --- 経路スイープ（CapsuleCast）: toPosまで到達できるか。止め位置とヒット情報を返す
    bool TrySweepTo(Vector3 fromPos, Vector3 toPos, out Vector3 hitStopPos, out RaycastHit hitInfo)
    {
        hitInfo = default;
        hitStopPos = toPos;

        // 現在のワールド・カプセル（中心線p1-p2, 半径r）を取得
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
