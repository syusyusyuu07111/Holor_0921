using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    InputSystem_Actions Input;

    [SerializeField] Transform Camera;        // カメラの Transform
    [SerializeField] float MoveSpeed = 5.0f;
    [SerializeField] float DashSpeed = 7.0f;
    [SerializeField] float SlowSpeed = 2.0f;
    [SerializeField] Animator animator;

    [Header("アナログ入力しきい値")]
    [SerializeField, Range(0f, 1f)] float analogPressPoint = 0.5f; // トリガー誤動作防止

    [Header("入力しきい値")]
    [SerializeField] float deadZone = 0.12f;   // 入力マグニチュードしきい値
    [SerializeField] float stopGrace = 0.08f;  // 離してからIdleに落とす遅延

    float noInputTimer = 0f;

    // 公開状態 & イベント
    [System.Serializable] public class DashEvent : UnityEngine.Events.UnityEvent { }
    public bool IsMovingNow { get; private set; }
    public bool IsDashingNow { get; private set; }
    public bool IsSlowWalkingNow { get; private set; }
    public DashEvent OnDashStart = new DashEvent();
    public DashEvent OnDashEnd = new DashEvent();
    bool _prevDash = false;

    // 入力なし時のロック
    Vector3 _lockedPos;
    Quaternion _lockedRot;

    // ===== 足音用 =====
    [Header("サウンド")]
    [SerializeField] private AudioManager audioManager; // 足音などを鳴らす
    bool _footstepActiveNow = false;                    // 直前フレームで足音が鳴いてたか

    // ===== デバッグ（Sキー後退の健全性チェック）=====
    [Header("Debug")]
    [SerializeField] TPSCamera tpsCamera;   // ← シーン上の TPSCamera をアサイン
    [SerializeField] bool EnableBackLog = true;
    [SerializeField] int LogEveryNFrames = 1;  // 1=毎フレーム出す

    void Awake()
    {
        Input = new InputSystem_Actions();
    }

    void OnEnable()
    {
        Input.Player.Enable();
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;
    }

    void Update()
    {
        // 入力読み取り
        Vector2 move = Input.Player.Move.ReadValue<Vector2>();
        bool isSlowWalking = Input.Player.SlowWalk.ReadValue<float>() >= analogPressPoint;

        // deadZone 判定
        bool hasInput = (move.sqrMagnitude >= deadZone * deadZone);

        // 入力なし：ロック
        if (!hasInput)
        {
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);

            noInputTimer += Time.deltaTime;
            if (noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
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

            // 足音停止（完全に止まってる扱い）
            HandleFootstepAudio(false);

            // S押下チェック用ログ（入力なし時は出さない）
            return;
        }

        // 入力あり
        noInputTimer = 0f;

        // =========== カメラの水平前方・右方向を求める（ピッチ影響を完全に排除する）===========
        Vector3 forward;
        Vector3 right;
        if (Camera)
        {
            float yaw = Camera.eulerAngles.y;
            Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
            forward = yawOnly * Vector3.forward;
            right = yawOnly * Vector3.right;
        }
        else
        {
            forward = Vector3.forward;
            right = Vector3.right;
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

        if (right.sqrMagnitude < 0.0001f)
        {
            right = new Vector3(forward.z, 0f, -forward.x);
        }
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(Vector3.up, forward);
        }
        right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;

        // 速度決定（ダッシュは前進時のみ）
        float currentSpeed = MoveSpeed;
        bool isDashing = Input.Player.Dash.ReadValue<float>() >= analogPressPoint && move.y >= 0f;
        if (isSlowWalking) { currentSpeed = SlowSpeed; isDashing = false; }
        else if (isDashing) { currentSpeed = DashSpeed; }

        // 進行方向
        Vector3 moveDir = (forward * move.y + right * move.x);
        if (moveDir.sqrMagnitude > 0.0001f) moveDir.Normalize();

        // --- 後退判定---
        bool isBackward = move.y < -deadZone;

        if (moveDir.sqrMagnitude > 0f)
        {
            // 移動は常に入力通り
            transform.position += moveDir * currentSpeed * Time.deltaTime;

            // 回転は「後退中はしない」。前進/横移動のときだけ向きを合わせる
            if (!isBackward)
            {
                transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
            }
            // isBackward のときは rotation を触らない＝向き維持で後退
        }

        // このフレームの結果をロック
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;

        // アニメ更新
        if (animator)
        {
            if (!animator.GetBool("IsMoving")) animator.SetBool("IsMoving", true);
            animator.SetBool("IsDashing", isDashing);
            animator.SetBool("IsSlowWalking", isSlowWalking);
        }

        // 公開状態＆イベント
        bool nowMoving = true;          // hasInput が true なので移動中扱い
        bool nowDashing = isDashing;
        bool nowSlow = isSlowWalking;

        if (nowDashing && !_prevDash) OnDashStart.Invoke();
        if (!nowDashing && _prevDash) OnDashEnd.Invoke();
        _prevDash = nowDashing;

        IsMovingNow = nowMoving;
        IsDashingNow = nowDashing;
        IsSlowWalkingNow = nowSlow;

        // 足音ループの制御
        HandleFootstepAudio(true);

        // ====== ここで S 押下時の健全性ログを出す ======
        if (EnableBackLog && Keyboard.current != null && Keyboard.current.sKey.isPressed)
        {
            LogBackCheck(move, moveDir, hasInput);
        }
    }

    // 足音をオンオフする処理をまとめておく
    void HandleFootstepAudio(bool shouldPlay)
    {
        if (audioManager == null) return;

        if (shouldPlay)
        {
            if (!_footstepActiveNow)
            {
                audioManager.StartFootstepLoop();
                _footstepActiveNow = true;
            }
        }
        else
        {
            if (_footstepActiveNow)
            {
                audioManager.StopFootstepLoop();
                _footstepActiveNow = false;
            }
        }
    }

    // ===== Sキー後退の健全性ログ =====
    void LogBackCheck(Vector2 move, Vector3 moveDir, bool hasInput)
    {
        if (Time.frameCount % Mathf.Max(1, LogEveryNFrames) != 0) return;

        // =========== ログ用のカメラ水平ベクトル再計算（プレイヤー移動と同じ処理）===========
        Vector3 camFh;
        Vector3 camRh;
        if (Camera)
        {
            float yaw = Camera.eulerAngles.y;
            Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
            camFh = yawOnly * Vector3.forward;
            camRh = yawOnly * Vector3.right;
        }
        else
        {
            camFh = Vector3.forward;
            camRh = Vector3.right;
        }

        if (camFh.sqrMagnitude < 0.0001f) camFh = Vector3.forward;
        camFh.Normalize();

        if (camRh.sqrMagnitude < 0.0001f) camRh = new Vector3(camFh.z, 0f, -camFh.x);
        camRh.Normalize();

        // S（後退）で期待する方向は「-camFh」
        Vector3 expected = -camFh;

        float ang = moveDir.sqrMagnitude > 0f ? Vector3.Angle(moveDir, expected) : 999f;
        float dot = moveDir.sqrMagnitude > 0f ? Vector3.Dot(moveDir, expected) : -1f;
        bool okMove = hasInput && move.y < 0f && ang <= 12f && dot > 0.98f; // だいたい真逆向き

        // カメラ距離の健全性（衝突補正後の“実使用距離”と実測が近いか）
        string camStat = "Unknown";
        float distNow = -1f, distUsed = -1f, yaw = -999f, pitch = -999f;
        if (tpsCamera != null && tpsCamera.Pivot != null && Camera != null)
        {
            distNow = Vector3.Distance(Camera.position, tpsCamera.Pivot.position);
            distUsed = tpsCamera.CurrentDistance;
            yaw = tpsCamera.yaw;
            pitch = tpsCamera.pitch;

            bool okCam = Mathf.Abs(distNow - distUsed) <= 0.15f; // 誤差15cm以内をOK
            camStat = okCam ? "OK" : "NG";
        }

        Debug.Log(
            $"[BackCheck] S-press " +
            $"| hasInput={hasInput} move=({move.x:0.00},{move.y:0.00}) " +
            $"| camYaw={yaw:0.0} camPitch={pitch:0.0} " +
            $"| camFh={Vxz(camFh)} camRh={Vxz(camRh)} " +
            $"| moveDir={Vxz(moveDir)} " +
            $"| angle(moveDir,-camF)={ang:0.0} dot={dot:0.000} => MoveOK={(okMove ? "OK" : "NG")} " +
            $"| camDistNow={distNow:0.00} used~{distUsed:0.00} => CamOK={camStat}"
        );
    }

    // 見やすいように XZ 成分だけを短く表記
    static string Vxz(Vector3 v) => $"({v.x:0.000},{v.z:0.000})";
}
