using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    InputSystem_Actions Input;

    [SerializeField] Transform Camera;
    [SerializeField] float MoveSpeed = 5.0f;
    [SerializeField] float DashSpeed = 7.0f;
    [SerializeField] float SlowSpeed = 2.0f;
    [SerializeField] Animator animator;

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
        bool isSlowWalking = Input.Player.SlowWalk.IsPressed();

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
            return;
        }

        // 入力あり
        noInputTimer = 0f;

        // カメラ基準の平面向き
        Vector3 forward = Camera ? Vector3.ProjectOnPlane(Camera.forward, Vector3.up) : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
        {
            // カメラが真上・真下を向いている場合は、自身の forward を基準にする
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

        Vector3 right = Camera ? Vector3.ProjectOnPlane(Camera.right, Vector3.up) : Vector3.right;
        if (right.sqrMagnitude < 0.0001f)
        {
            right = new Vector3(forward.z, 0f, -forward.x); // forward と直交する水平ベクトル
        }
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(Vector3.up, forward);
        }
        right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;

        // 速度決定（ダッシュは前進時のみ）
        float currentSpeed = MoveSpeed;
        bool isDashing = Input.Player.Dash.IsPressed() && move.y >= 0f;
        if (isSlowWalking) { currentSpeed = SlowSpeed; isDashing = false; }
        else if (isDashing) { currentSpeed = DashSpeed; }

        // 進行方向
        Vector3 moveDir = (forward * move.y + right * move.x);
        if (moveDir.sqrMagnitude > 0.0001f) moveDir.Normalize();

        // --- 後退判定（ここがポイント）---
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
        bool nowMoving = true;
        bool nowDashing = isDashing;
        bool nowSlow = isSlowWalking;

        if (nowDashing && !_prevDash) OnDashStart.Invoke();
        if (!nowDashing && _prevDash) OnDashEnd.Invoke();
        _prevDash = nowDashing;

        IsMovingNow = nowMoving;
        IsDashingNow = nowDashing;
        IsSlowWalkingNow = nowSlow;
    }
}
