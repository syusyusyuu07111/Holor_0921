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

    // 競合しうる代表コンポーネント
    [Header("Optional Components (競合対策)")]
    [SerializeField] Rigidbody rb;                   // 付いていれば自動参照
    [SerializeField] CharacterController cc;         // 付いていれば自動参照
    [Tooltip("地面スナップ/RootDown系など。ここに入れたBehaviourは登り中に無効化")]
    [SerializeField] Behaviour[] groundSnapScripts;

    [Header("Ground Snap Permanent Off")]
    [Tooltip("ONなら groundSnapScripts を常時OFF（登り終了でも復帰しない）")]
    [SerializeField] bool disableGroundSnapAlways = true;

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
    [SerializeField, Min(0.01f)] float maxStep = 0.2f;

    // ===== Collision (Overlap) =====
    [Header("Overlap Block (Furnitureのみ)")]
    [SerializeField] LayerMask furnitureMask;
    [SerializeField] bool lockY = true;

    [Header("Capsule (fallback)")]
    [SerializeField] float capsuleHeight = 1.7f;
    [SerializeField] float capsuleRadius = 0.3f;
    [SerializeField] Vector3 capsuleCenter = new Vector3(0f, 0.9f, 0f);

    [Header("Overlap Tweaks")]
    [SerializeField] float overlapShrink = 0.002f;
    [SerializeField] float topTrim = 0.02f, bottomTrim = 0.08f;

    // ===== Sweep/Slide =====
    [Header("Sweep/Slide")]
    [SerializeField] float skin = 0.01f;
    [SerializeField, Range(0f, 1f)] float slideFactor = 1.0f;

    // ===== Turn / Micro =====
    [Header("Turn / Micro-move")]
    [SerializeField] bool disableSlideOnFlip = true;
    [SerializeField] float microMoveEps = 0.0025f;
    [SerializeField] bool rotateEvenIfBlocked = true;

    // ===== Rotation =====
    [Header("Rotation (Turn Smoothing)")]
    [SerializeField] float turnSpeedDeg = 540f;

    // ===== 椅子登り（アニメ主導） =====
    [Header("Chair Climb (Animator)")]
    [SerializeField] bool useRootMotionOnClimb = true;
    [SerializeField] bool rotateWhileClimb = false;
    [SerializeField] string ClimbBoolName = "IsClimbing";
    [SerializeField] string ClimbTag = "Climb";

    // ゴール高さの決定
    [Header("Climb Height (Inspector)")]
    [SerializeField] bool preferSeatTopChild = true;
    [SerializeField] string seatTopChildName = "SeatTop";
    [SerializeField, Min(0f)] float seatLiftOffsetY = 0.25f;
    [SerializeField] float extraLiftY = 0f;

    // 上がり方（時間制御）
    [Header("Chair Lift Timing")]
    [SerializeField] bool liftByNormalizedTime = false;        // false=秒, true=normalizedTime
    [SerializeField, Min(0f)] float liftDelaySec = 0.20f;
    [SerializeField, Min(0.01f)] float liftDurationSec = 0.45f;
    [SerializeField, Range(0f, 1f)] float liftStartNT = 0.20f;
    [SerializeField, Range(0f, 1f)] float liftEndNT = 0.80f;
    [SerializeField] AnimationCurve liftCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // フレーム最後でYを確定（他処理に勝つ）
    [Header("Y Override (競合対策)")]
    [SerializeField] bool forceYInLateUpdate = true;
    private bool _hadYOverride = false;
    private float _yOverride = 0f;

    // ===== ランタイム（登り） =====
    private bool _isChairClimbing = false;
    private bool _lockYBeforeClimb = true;
    private float _targetTopY = 0f;
    private float _liftStartY;
    private float _liftStartTime;
    private bool _liftActive;

    // 競合対策：元の状態
    bool _rbHad; bool _rbKinematic; bool _rbGravity;
    bool _ccHad; bool _ccEnabled;
    (Behaviour b, bool enabled)[] _snapCache;

    // ===== Debug =====
    [Header("Debug (Climb)")]
    [SerializeField] bool verboseClimbLogs = true;
    [SerializeField] bool drawLiftRay = true;
    int _climbFrame = 0;
    int _zeroYDeltaFrames = 0;
    float _lastYForStuckCheck = float.NaN;

    void V(string msg, Object ctx = null)
    {
        if (!verboseClimbLogs) return;
        if (ctx) Debug.Log($"[Climb] {msg}", ctx);
        else Debug.Log($"[Climb] {msg}");
    }
    void W(string msg) { if (verboseClimbLogs) Debug.LogWarning($"[Climb] {msg}"); }

    // ===== Move state =====
    private int _dominantAxis = 0;
    private float _noInputTimer = 0f;
    private bool _prevDash = false;
    private bool _footLoopOn = false;
    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private float _fixedY;
    private CapsuleCollider _cap;
    private int _lastSignX = 0, _lastSignY = 0;

    // 公開
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public bool IsChairClimbing => _isChairClimbing;
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();

    void Awake()
    {
        Input = new InputSystem_Actions();
        _cap = GetComponent<CapsuleCollider>();
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!cc) cc = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        Input.Player.Enable();
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;
        _fixedY = transform.position.y;

        if (disableGroundSnapAlways && groundSnapScripts != null)
        {
            foreach (var b in groundSnapScripts)
                if (b) b.enabled = false;
            V("GroundSnapScripts are PERMANENTLY disabled (by inspector).");
        }
    }
    void OnDisable() => Input.Player.Disable();

    // ===== 椅子登り API =====
    public void BeginChairClimbFromCollider(Collider col)
    {
        float targetTopY = ResolveTargetTopY(col);
        BeginChairClimb(targetTopY);
    }

    public void BeginChairClimb(float targetTopY)
    {
        if (_isChairClimbing) return;
        _isChairClimbing = true;

        _climbFrame = 0;
        _zeroYDeltaFrames = 0;
        _lastYForStuckCheck = transform.position.y;

        _lockYBeforeClimb = lockY;
        lockY = false;

        _targetTopY = targetTopY;
        _liftStartY = transform.position.y;
        _liftStartTime = Time.time;
        _liftActive = true;

        EnterClimb_DisableConflicts();

        if (animator)
        {
            animator.applyRootMotion = useRootMotionOnClimb;
            if (!string.IsNullOrEmpty(ClimbBoolName))
                animator.SetBool(ClimbBoolName, true);
        }

        ToggleFootLoop(false);
        _prevDash = false;
        IsMovingNow = IsDashingNow = IsSlowWalkingNow = false;

        V($"Begin | posY={transform.position.y:0.000} -> targetTopY={targetTopY:0.000}, applyRM={animator?.applyRootMotion}");
        if (animator)
        {
            var infos0 = animator.GetCurrentAnimatorClipInfo(0);
            if (infos0 != null && infos0.Length > 0)
            {
                var clip = infos0[0].clip;
                V($"BaseClip={clip.name}, hasRootCurves={clip.hasRootCurves}, avgSpeed={clip.averageSpeed}, apparentSpeed={clip.apparentSpeed}");
            }
            V($"hasRootMotion={animator.hasRootMotion}, culling={animator.cullingMode}, updateMode={animator.updateMode}");
        }

        // 競合候補の現状態
        if (rb) V($"RB kinematic={rb.isKinematic} gravity={rb.useGravity}");
        if (cc) V($"CC enabled={cc.enabled}");
        if (groundSnapScripts != null && groundSnapScripts.Length > 0)
        {
            for (int i = 0; i < groundSnapScripts.Length; i++)
                V($"Snap[{i}] name={groundSnapScripts[i]?.GetType().Name} enabled={groundSnapScripts[i]?.enabled}");
        }
    }

    public void EndChairClimb()
    {
        if (animator)
        {
            if (!string.IsNullOrEmpty(ClimbBoolName))
                animator.SetBool(ClimbBoolName, false);
            animator.applyRootMotion = false;
        }

        lockY = _lockYBeforeClimb;
        _fixedY = transform.position.y;

        _liftActive = false;
        _isChairClimbing = false;

        ExitClimb_RestoreConflicts();

        V($"End | finalY={_fixedY:0.000} lockY restored={lockY}");
    }

    // ===== RootMotion適用（XZはスイープ、Yは後で上書き） =====
    void OnAnimatorMove()
    {
        if (!(_isChairClimbing && animator && animator.applyRootMotion)) return;

        _climbFrame++;

        Vector3 delta = animator.deltaPosition;
        Quaternion dRot = animator.deltaRotation;

        // 参考：そのままYも一度適用（後で上書き）
        float beforeY = transform.position.y;
        transform.position += new Vector3(0f, delta.y, 0f);

        // XZはスイープ
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + new Vector3(delta.x, 0f, delta.z);
        if (!TrySweepTo(startPos, targetPos, out Vector3 stopPos, out RaycastHit hit))
        {
            transform.position = stopPos;
        }
        else
        {
            transform.position = targetPos;
        }
        transform.rotation *= dRot;

        if (_climbFrame <= 3 || _climbFrame % 15 == 0)
            V($"FM#{_climbFrame:000} | delta={delta} (appliedY {beforeY:0.000}->{transform.position.y:0.000})");
    }

    // ===== 毎フレーム：Y補間＋詳細ログ =====
    void Update()
    {
        if (_isChairClimbing && animator)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);

            float curY = transform.position.y;

            if (_liftActive)
            {
                float t01;
                float elapsed = Time.time - _liftStartTime;

                if (!liftByNormalizedTime)
                {
                    float eff = Mathf.Max(0f, elapsed - liftDelaySec);
                    t01 = Mathf.Clamp01(eff / Mathf.Max(0.0001f, liftDurationSec));
                }
                else
                {
                    float nt = st.normalizedTime;
                    float nt0 = Mathf.Min(liftStartNT, liftEndNT);
                    float nt1 = Mathf.Max(liftStartNT, liftEndNT);
                    t01 = Mathf.Clamp01(Mathf.InverseLerp(nt0, nt1, nt));
                }

                float curve = liftCurve.Evaluate(t01);
                float y = Mathf.Lerp(_liftStartY, _targetTopY, curve);

                // 位置反映
                var p = transform.position;
                transform.position = new Vector3(p.x, y, p.z);
                _hadYOverride = true;
                _yOverride = y;

                // ログ（詳細）
                if (_climbFrame % 5 == 0)
                {
                    V($"Y-Lift t={t01:0.00} curve={curve:0.00} curY(before)={curY:0.000} -> setY={y:0.000}  target={_targetTopY:0.000}  normTime={st.normalizedTime:0.00} tag={st.IsTag(ClimbTag)} RM={animator.applyRootMotion}");
                }

                // スタック検出（Yが増えない/変わらない）
                if (float.IsNaN(_lastYForStuckCheck)) _lastYForStuckCheck = y;
                float dy = Mathf.Abs(y - _lastYForStuckCheck);
                if (dy < 1e-4f) _zeroYDeltaFrames++; else { _zeroYDeltaFrames = 0; _lastYForStuckCheck = y; }

                if (_zeroYDeltaFrames >= 3)
                {
                    W($"Y not changing for {_zeroYDeltaFrames} frames (y≈{y:0.000}). Something is writing Y after Update(). Check: CC({(cc ? cc.enabled : false)}), RB(kine={(rb ? rb.isKinematic : false)},grav={(rb ? rb.useGravity : false)}), SnapCount={groundSnapScripts?.Length ?? 0}, LateUpdate保険={forceYInLateUpdate}");
                }

                if (t01 >= 1f) _liftActive = false;
            }

            if (drawLiftRay)
            {
                Debug.DrawLine(transform.position, transform.position + Vector3.up * 0.6f, Color.cyan, 0f, false);
            }

            if (rotateWhileClimb)
            {
                Vector2 moveRawTmp = Input.Player.Move.ReadValue<Vector2>();
                Vector2 moveSnapTmp = SnapOneAxis(moveRawTmp);
                if (moveSnapTmp.sqrMagnitude > 0f)
                {
                    float yawClimb = GetYaw();
                    Quaternion yawRotClimb = Quaternion.Euler(0f, yawClimb, 0f);
                    Vector3 dirClimb = (yawRotClimb * new Vector3(moveSnapTmp.x, 0f, moveSnapTmp.y)).normalized;
                    if (dirClimb.sqrMagnitude > 1e-6f)
                    {
                        Quaternion targetRotClimb = Quaternion.LookRotation(dirClimb, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotClimb, turnSpeedDeg * Time.deltaTime);
                    }
                }
            }

            // ステート終了監視
            if (st.IsTag(ClimbTag) && st.normalizedTime >= 1.0f)
            {
                V("State finished by tag/time. EndChairClimb().");
                EndChairClimb();
            }

            ToggleFootLoop(false);
            return; // 通常移動はスキップ
        }

        // ===== ここから通常移動（ログは割愛。既存のまま） =====
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

        int signX = (moveSnap.x > 0f) ? 1 : (moveSnap.x < 0f ? -1 : 0);
        int signY = (moveSnap.y > 0f) ? 1 : (moveSnap.y < 0f ? -1 : 0);
        bool flipped = (signX != 0 && _lastSignX != 0 && signX != _lastSignX)
                    || (signY != 0 && _lastSignY != 0 && signY != _lastSignY);

        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        bool isBackward = moveSnap.y < 0f;
        float speed = slowHeld ? SlowSpeed : (dashHeld && !isBackward ? DashSpeed : MoveSpeed);
        bool isDashing = (!slowHeld && dashHeld && !isBackward);

        Vector3 frameStart = transform.position;
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
                    transform.position = stopPos;

                    float traveled = (stopPos - startPos).magnitude;
                    float leftover = Mathf.Max(0f, stepLen - traveled);

                    if (disableSlideOnFlip && flipped) leftover = 0f;

                    if (leftover > 1e-5f)
                    {
                        Vector3 n = hit.normal;
                        Vector3 slideDir = Vector3.ProjectOnPlane(dir, n).normalized;
                        if (slideDir.sqrMagnitude > 1e-6f)
                        {
                            Vector3 slideTarget = stopPos + slideDir * (leftover * slideFactor);
                            if (lockY) slideTarget.y = _fixedY;

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
                    transform.position = stopPos;
                    movedThisFrame = true;
                }
            }
        }

        float frameMoved = (transform.position - frameStart).magnitude;
        if (frameMoved < microMoveEps)
        {
            transform.position = frameStart;
            movedThisFrame = false;
        }

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

        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        if (animator)
        {
            animator.SetBool("IsMoving", hasInput);
            animator.SetBool("IsDashing", hasInput && isDashing && movedThisFrame);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        IsMovingNow = movedThisFrame;
        IsDashingNow = movedThisFrame && isDashing;
        IsSlowWalkingNow = slowHeld;
        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        ToggleFootLoop(movedThisFrame);
        _lastSignX = signX; _lastSignY = signY;
    }

    // ===== フレーム末尾：Yを確定 =====
    void LateUpdate()
    {
        if (forceYInLateUpdate && _isChairClimbing && _hadYOverride)
        {
            var p = transform.position;
            transform.position = new Vector3(p.x, _yOverride, p.z);
            V($"LateFix Y => {_yOverride:0.000}");
        }
        _hadYOverride = false;
    }

    // ===== 目標高さの解決 =====
    float ResolveTargetTopY(Collider col)
    {
        float baseY;

        if (preferSeatTopChild && col != null)
        {
            var t = col.transform.Find(seatTopChildName);
            if (t != null)
            {
                baseY = t.position.y;
                return baseY + seatLiftOffsetY + extraLiftY;
            }
        }

        Bounds b = col.bounds;
        baseY = b.max.y; // 上端
        return baseY + seatLiftOffsetY + extraLiftY;
    }

    // ===== 小物ユーティリティ =====
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
            return false;
        }

        return true;
    }

    // ===== 競合対策 入退場 =====
    void EnterClimb_DisableConflicts()
    {
        if (rb)
        {
            _rbHad = true;
            _rbKinematic = rb.isKinematic;
            _rbGravity = rb.useGravity;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else _rbHad = false;

        if (cc)
        {
            _ccHad = true;
            _ccEnabled = cc.enabled;
            cc.enabled = false;
        }
        else _ccHad = false;

        if (!disableGroundSnapAlways && groundSnapScripts != null && groundSnapScripts.Length > 0)
        {
            _snapCache = new (Behaviour, bool)[groundSnapScripts.Length];
            for (int i = 0; i < groundSnapScripts.Length; i++)
            {
                var b = groundSnapScripts[i];
                if (!b) { _snapCache[i] = (null, false); continue; }
                _snapCache[i] = (b, b.enabled);
                b.enabled = false;
            }
        }
        else _snapCache = null;
    }

    void ExitClimb_RestoreConflicts()
    {
        if (_rbHad && rb)
        {
            rb.isKinematic = _rbKinematic;
            rb.useGravity = _rbGravity;
        }
        if (_ccHad && cc)
        {
            cc.enabled = _ccEnabled;
        }

        if (!disableGroundSnapAlways && _snapCache != null)
        {
            foreach (var (b, en) in _snapCache)
                if (b) b.enabled = en;
        }
        _snapCache = null;
    }
}
