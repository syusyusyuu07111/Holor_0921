using CriWare;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    /*
        =========================
        このスクリプトがやってること
        =========================

        ■目的
        ・プレイヤーを「4方向（上/下/左/右）」に移動させる（斜め入力はしない）
        ・家具レイヤーにだけ当たり判定を行い、壁にめり込まないように止める/滑らせる
        ・入力に応じてアニメ(歩き/走り/スロー)を切り替える
        ・CRI Atom で「歩きループ」「走りループ」の足音を、開始/終了のタイミングだけ再生/停止する

        ■大きな流れ（Update）
        1) 入力を読む（Move / SlowWalk / Dash）
        2) 入力を「上下左右のどれか1軸」にスナップする（SnapOneAxis）
        3) 入力が無いなら：位置をロック位置へ戻し、アニメ停止、足音停止
        4) 入力があるなら：
           - カメラの向き（Yaw）に合わせて移動方向を作る
           - 速度を決める（通常 / ダッシュ / スロー）
           - 移動量を小分け（サブステップ）にして、1回ずつ CapsuleCast で衝突チェック
           - 当たったら「止め位置」まで進め、残り距離は壁に沿ってスライド
           - ただし「方向反転したフレーム」はスライドしない（disableSlideOnFlip）
           - 微小移動（ピクッ）を消す（microMoveEps）
           - 回頭はRotateTowardsでスムーズに
        5) 状態（IsMovingNow 等）更新、Dashイベント更新
        6) 足音ループ制御（歩き/走りの開始・終了だけPlay/Stop）

        ※「IsMovingNow」は“実際に動けたか”なので、壁押しっぱなしで動けない時は false になる。
          ただし animator の IsMoving は「入力がある限り true」の運用になっている（Idleに戻りにくくする意図）。
    */

    // ===== Input =====
    private InputSystem_Actions Input;

    // ===== Refs =====
    [Header("Refs")]
    [SerializeField] Transform Cam;
    [SerializeField] Animator animator;
    [SerializeField] TPSCamera tpsCamera;

    [Header("Footstep (CRI Atom)")]
    [SerializeField] CriAtomSource walkLoopSource; // 歩き用ループSE
    [SerializeField] CriAtomSource runLoopSource;  // 走り用ループSE

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
    private Vector3 _lockedPos;
    private Quaternion _lockedRot;
    private float _fixedY;
    private CapsuleCollider _cap;

    // 入力方向の符号記録（反転検出用）
    private int _lastSignX = 0, _lastSignY = 0;

    // 足音ループ状態（実際に今 Self 管理しているか）
    private bool _walkLoopOn = false;
    private bool _runLoopOn = false;

    // ★追加：前フレームの「論理状態」（歩き/走り中か）
    private bool _wasWalking = false;
    private bool _wasRunning = false;

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
        // InputActions生成
        Input = new InputSystem_Actions();

        // CapsuleColliderが付いていれば採用（無ければfallback設定を使う）
        _cap = GetComponent<CapsuleCollider>();
    }

    void OnEnable()
    {
        // 入力を有効化
        Input.Player.Enable();

        // 入力が無いフレームで「位置を戻す」ためのロックを初期化
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        // Y固定用（2Dっぽい運用）
        _fixedY = transform.position.y;
    }

    void OnDisable() => Input.Player.Disable();

    void Update()
    {
        // =========================================================
        // 1) 入力を読む
        // =========================================================
        Vector2 moveRaw = Input.Player.Move.ReadValue<Vector2>();

        // アナログ入力でも一定値以上で「押した」扱いにする
        bool slowHeld = Input.Player.SlowWalk.ReadValue<float>() >= analogPressPoint;
        bool dashHeld = Input.Player.Dash.ReadValue<float>() >= analogPressPoint;

        // =========================================================
        // 2) 入力を上下左右のどれか1軸にスナップ（斜め禁止）
        // =========================================================
        Vector2 moveSnap = SnapOneAxis(moveRaw);
        bool hasInput = (moveSnap.x != 0f) || (moveSnap.y != 0f);

        // =========================================================
        // 3) 入力なし：位置固定 / アニメ止め / 足音止め
        // =========================================================
        if (!hasInput)
        {
            // 前フレームの「最後に正しく動けた位置」に戻す（微振動を抑える意図）
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);

            // Y固定
            if (lockY) transform.position = new Vector3(transform.position.x, _fixedY, transform.position.z);

            // StopGrace：入力が無い時間が少し続いたら IsMoving を false にする（瞬間の入力抜け対策）
            _noInputTimer += Time.deltaTime;
            if (_noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                animator.SetBool("IsMoving", false);

            // ダッシュ/スローは即false
            if (animator)
            {
                animator.SetBool("IsDashing", false);
                animator.SetBool("IsSlowWalking", false);
            }

            // 公開状態も停止
            IsMovingNow = IsDashingNow = IsSlowWalkingNow = false;

            // Dashイベント終端
            if (_prevDash)
            {
                OnDashEnd.Invoke();
                _prevDash = false;
            }

            // 足音停止（歩き/走りとも false）
            UpdateFootstepLoop(false, false);

            // 次回の優勢軸や符号をリセット
            _dominantAxis = 0;
            _lastSignX = 0; _lastSignY = 0;
            return;
        }

        // 入力がある → stop grace リセット
        _noInputTimer = 0f;

        // =========================================================
        // 4) 「方向反転」フレームか？（スライド無効化に使う）
        // =========================================================
        int signX = (moveSnap.x > 0f) ? 1 : (moveSnap.x < 0f ? -1 : 0);
        int signY = (moveSnap.y > 0f) ? 1 : (moveSnap.y < 0f ? -1 : 0);

        bool flipped = (signX != 0 && _lastSignX != 0 && signX != _lastSignX)
                    || (signY != 0 && _lastSignY != 0 && signY != _lastSignY);

        // =========================================================
        // 5) カメラの向きに合わせて移動方向を作る（Yawのみ）
        // =========================================================
        float yaw = GetYaw();
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 moveDir = (yawRot * new Vector3(moveSnap.x, 0f, moveSnap.y)).normalized;

        // =========================================================
        // 6) 速度決定（後ろ入力はダッシュ禁止）
        // =========================================================
        bool isBackward = moveSnap.y < 0f;
        float speed = slowHeld ? SlowSpeed : (dashHeld && !isBackward ? DashSpeed : MoveSpeed);
        bool isDashing = (!slowHeld && dashHeld && !isBackward);

        // =========================================================
        // 7) サブステップ移動（CapsuleCastで止める/滑らせる）
        // =========================================================
        Vector3 frameStart = transform.position; // 微小移動キャンセル用
        Vector3 desired = moveDir * speed * Time.deltaTime;
        float remain = desired.magnitude;
        bool movedThisFrame = false;

        if (remain > 0f)
        {
            Vector3 dir = desired / remain;

            // 大きい移動量を小分けにする（壁への引っ掛かりを減らす）
            int steps = Mathf.Max(1, Mathf.CeilToInt(remain / maxStep));
            float stepLen = remain / steps;

            for (int i = 0; i < steps; i++)
            {
                Vector3 startPos = transform.position;
                Vector3 targetPos = startPos + dir * stepLen;
                if (lockY) targetPos.y = _fixedY;

                // 1回目：目標へスイープ（当たるか？）
                if (!TrySweepTo(startPos, targetPos, out Vector3 stopPos, out RaycastHit hit))
                {
                    // 任意ログ：何に当たって止められたか
                    if (logBlockObjects && Time.frameCount % blockLogEveryNFrames == 0)
                    {
                        Vector3 moveDirLog = (targetPos - startPos).normalized;
                        LogBlock(stopPos, 1, hit.collider, moveDirLog);
                    }

                    // 当たったので、止め位置まで進める
                    transform.position = stopPos;

                    // 残り距離（このステップで進めなかった分）
                    float traveled = (stopPos - startPos).magnitude;
                    float leftover = Mathf.Max(0f, stepLen - traveled);

                    // 方向反転フレームはスライド禁止
                    if (disableSlideOnFlip && flipped) leftover = 0f;

                    // 2回目：壁面へ投影した方向にスライドしてみる
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
                    // 当たらず到達
                    transform.position = stopPos;
                    movedThisFrame = true;
                }
            }
        }

        // =========================================================
        // 8) 微小移動をキャンセル（壁押しでピクピクするのを抑える）
        // =========================================================
        float frameMoved = (transform.position - frameStart).magnitude;
        if (frameMoved < microMoveEps)
        {
            transform.position = frameStart;
            movedThisFrame = false;
        }

        // =========================================================
        // 9) 回頭（後ろ入力は回頭しない。動けなくても回頭したい場合は rotateEvenIfBlocked）
        // =========================================================
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

        // 入力無し時に戻すためのロック更新
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        // =========================================================
        // 10) アニメ更新
        //     ※「入力があるなら IsMoving=true」なので、壁で進めなくても Idleになりにくい
        // =========================================================
        if (animator)
        {
            animator.SetBool("IsMoving", hasInput);
            animator.SetBool("IsDashing", hasInput && isDashing && movedThisFrame);
            animator.SetBool("IsSlowWalking", slowHeld);
        }

        // =========================================================
        // 11) 公開状態更新（他スクリプトが見る用）
        // =========================================================
        IsMovingNow = movedThisFrame;
        IsDashingNow = movedThisFrame && isDashing;
        IsSlowWalkingNow = slowHeld;

        // Dashイベント（開始/終了）
        if (IsDashingNow && !_prevDash) OnDashStart.Invoke();
        if (!IsDashingNow && _prevDash) OnDashEnd.Invoke();
        _prevDash = IsDashingNow;

        // =========================================================
        // 12) 足音ループ制御（状態変化があったときだけ Play/Stop）
        // =========================================================
        bool isRunningNow = IsDashingNow;
        bool isWalkingNow = IsMovingNow && !IsDashingNow;

        UpdateFootstepLoop(isWalkingNow, isRunningNow);

        // 次フレーム用：符号を保存
        _lastSignX = signX; _lastSignY = signY;
    }

    // ------------------------------------------------------------
    // SnapOneAxis
    // ・入力(raw)を「左右」か「上下」のどちらか一方に固定する
    // ・同じくらいの強さなら、前フレームの優勢軸(_dominantAxis)を優先してブレを減らす
    // ------------------------------------------------------------
    Vector2 SnapOneAxis(Vector2 raw)
    {
        float ax = Mathf.Abs(raw.x), ay = Mathf.Abs(raw.y);

        // deadZone未満は0にする
        float x = (ax >= deadZone) ? Mathf.Sign(raw.x) : 0f;
        float y = (ay >= deadZone) ? Mathf.Sign(raw.y) : 0f;

        // 両方0なら入力なし
        if (x == 0f && y == 0f) { _dominantAxis = 0; return Vector2.zero; }

        // どちらが明確に強いかで優勢軸を決める
        if (ax > ay + tieEpsilon) _dominantAxis = 1;          // 左右が強い
        else if (ay > ax + tieEpsilon) _dominantAxis = 2;     // 上下が強い
        else if (_dominantAxis == 0) _dominantAxis = (ay >= ax) ? 2 : 1; // 初回だけ適当に決める

        // 優勢軸だけ通す（もう片方は0）
        return (_dominantAxis == 1) ? new Vector2(x, 0f) : new Vector2(0f, y);
    }

    // ------------------------------------------------------------
    // GetYaw
    // ・移動方向をカメラの向きに合わせたいので、Yaw（Y回転）だけ取得
    // ------------------------------------------------------------
    float GetYaw()
    {
        Transform src = Cam ? Cam : (Camera.main ? Camera.main.transform : transform);
        return src.eulerAngles.y;
    }

    // ------------------------------------------------------------
    // UpdateFootstepLoop
    // ・歩き/走り の「開始/終了」を検出してループSEをPlay/Stopする
    // ・毎フレーム Play を呼ぶとループが不安定になるので、変化した時だけ
    // ------------------------------------------------------------
    void UpdateFootstepLoop(bool isWalkingNow, bool isRunningNow)
    {
        // どちらのソースも無ければ何もしない
        if (walkLoopSource == null && runLoopSource == null) return;

        // ===== 歩き：開始 =====
        if (isWalkingNow && !_wasWalking)
        {
            // 走りが鳴ってたら止めてから歩きへ切替
            if (_runLoopOn && runLoopSource != null)
            {
                Debug.Log("Footstep: Stop RUN (because WALK started)");
                runLoopSource.Stop();
                _runLoopOn = false;
            }

            if (walkLoopSource != null && !_walkLoopOn)
            {
                Debug.Log("Footstep: Play WALK");
                walkLoopSource.loop = true;
                walkLoopSource.Play();
                _walkLoopOn = true;
            }
        }
        // ===== 歩き：終了 =====
        else if (!isWalkingNow && _wasWalking)
        {
            if (_walkLoopOn && walkLoopSource != null)
            {
                Debug.Log("Footstep: Stop WALK");
                walkLoopSource.Stop();
                _walkLoopOn = false;
            }
        }

        // ===== 走り：開始 =====
        if (isRunningNow && !_wasRunning)
        {
            // 歩きが鳴ってたら止めてから走りへ切替
            if (_walkLoopOn && walkLoopSource != null)
            {
                Debug.Log("Footstep: Stop WALK (because RUN started)");
                walkLoopSource.Stop();
                _walkLoopOn = false;
            }

            if (runLoopSource != null && !_runLoopOn)
            {
                Debug.Log("Footstep: Play RUN");
                runLoopSource.loop = true;
                runLoopSource.Play();
                _runLoopOn = true;
            }
        }
        // ===== 走り：終了 =====
        else if (!isRunningNow && _wasRunning)
        {
            if (_runLoopOn && runLoopSource != null)
            {
                Debug.Log("Footstep: Stop RUN");
                runLoopSource.Stop();
                _runLoopOn = false;
            }
        }

        // 次フレーム用に記録
        _wasWalking = isWalkingNow;
        _wasRunning = isRunningNow;
    }

    // ------------------------------------------------------------
    // LogBlock
    // ・どのColliderに当たって止められたかログを出す（任意）
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // ApproxPenetration
    // ・ComputePenetration を使って「どれくらいめり込んでるか」を概算する（ログ用）
    // ------------------------------------------------------------
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
                return distance;
            }
        }

        Vector3 p = other.ClosestPoint(nextPos);
        return Mathf.Max(0f, (nextPos - p).magnitude);
    }

    // ------------------------------------------------------------
    // GetCapsuleWorld
    // ・CapsuleCast 用に「ワールド座標のカプセル端点(p1,p2)」と「半径(r)」を計算する
    // ・CapsuleColliderがあればそれを優先、無ければ fallback を使う
    // ・topTrim / bottomTrim で上下を少し削り誤反応を減らす
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // TrySweepTo
    // ・fromPos → toPos へ移動できるか CapsuleCast で判定する
    // ・当たったら hitStopPos を「壁手前(skin)で止まる位置」にして false を返す
    // ・当たらなければ toPos を返して true
    // ------------------------------------------------------------
    public bool TrySweepTo(Vector3 fromPos, Vector3 toPos, out Vector3 hitStopPos, out RaycastHit hitInfo)
    {
        hitInfo = default;
        hitStopPos = toPos;

        GetCapsuleWorld(out Vector3 p1, out Vector3 p2, out float r);

        Vector3 dir = toPos - fromPos;
        float dist = dir.magnitude;
        if (dist <= 0f) return true;

        dir /= dist;

        // Overlap判定を少し細くする（めり込み/誤反応低減）
        float rAdj = Mathf.Max(0.001f, r - Mathf.Max(0f, overlapShrink));

        if (Physics.CapsuleCast(p1, p2, rAdj, dir, out RaycastHit hit, dist, furnitureMask, QueryTriggerInteraction.Ignore))
        {
            // hit.distance は「当たるまでの距離」なので skin 分だけ手前で止める
            float stopDist = Mathf.Max(0f, hit.distance - skin);
            hitStopPos = fromPos + dir * stopDist;
            hitInfo = hit;
            return false;
        }

        return true;
    }

    // ================================
    // ExternalMoveByDelta
    // ・外部から「押す」などの理由で worldDelta だけ動かしたい時に使う
    // ・同じ TrySweepTo を通すので、家具にめり込まない
    // ================================
    public void ExternalMoveByDelta(Vector3 worldDelta)
    {
        if (worldDelta.sqrMagnitude <= 0f) return;

        Vector3 fromPos = transform.position;
        Vector3 toPos = fromPos + worldDelta;

        if (!TrySweepTo(fromPos, toPos, out Vector3 stopPos, out RaycastHit _))
        {
            transform.position = stopPos;
        }
        else
        {
            transform.position = stopPos;
        }

        if (lockY)
        {
            transform.position = new Vector3(transform.position.x, _fixedY, transform.position.z);
        }

        // 押された結果をロック位置にも反映したいなら以下を使う
        // _lockedPos = transform.position;
        // _lockedRot = transform.rotation;
    }
}