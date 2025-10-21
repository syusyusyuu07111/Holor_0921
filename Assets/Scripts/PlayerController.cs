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

    // ★ ロック用（最後に“自分で動かした”座標/回転）
    Vector3 _lockedPos;
    Quaternion _lockedRot;

    void Awake()
    {
        Input = new InputSystem_Actions();
    }

    void OnEnable()
    {
        Input.Player.Enable();

        // 起動時点をロック基準に
        _lockedPos = transform.position;
        _lockedRot = transform.rotation;
    }

    void Update()
    {
        // 入力読み取り
        Vector2 move = Input.Player.Move.ReadValue<Vector2>();
        bool isSlowWalking = Input.Player.SlowWalk.IsPressed();

        // deadZone 判定（スティック/キーボード両対応）
        bool hasInput = (move.sqrMagnitude >= deadZone * deadZone);

        // 入力なし：位置/回転をロック値に固定して終了
        if (!hasInput)
        {
            transform.SetPositionAndRotation(_lockedPos, _lockedRot);

            // アニメ処理（停止）
            noInputTimer += Time.deltaTime;
            if (noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                animator.SetBool("IsMoving", false);
            if (animator)
            {
                animator.SetBool("IsDashing", false);
                animator.SetBool("IsSlowWalking", false);
            }

            // 公開状態
            IsMovingNow = false;
            IsDashingNow = false;
            IsSlowWalkingNow = false;
            if (_prevDash) { OnDashEnd.Invoke(); _prevDash = false; }

            return;
        }

        // ここから“入力あり”処理
        noInputTimer = 0f;

        // カメラ基準の平面向き
        Vector3 forward = Camera ? Camera.forward : Vector3.forward;
        forward.y = 0f; forward.Normalize();
        Vector3 right = Camera ? Camera.right : Vector3.right;
        right.y = 0f; right.Normalize();

        // スピード決定（ダッシュは前進時のみ）
        float currentSpeed = MoveSpeed;
        bool isDashing = Input.Player.Dash.IsPressed() && move.y >= 0f;
        if (isSlowWalking) { currentSpeed = SlowSpeed; isDashing = false; }
        else if (isDashing) { currentSpeed = DashSpeed; }

        // 進行方向
        Vector3 moveDir = (forward * move.y + right * move.x);
        if (moveDir.sqrMagnitude > 0.0001f) moveDir.Normalize();

        // 前進/後退どちらでも押されている入力に従って移動
        if (moveDir.sqrMagnitude > 0f)
        {
            transform.position += moveDir * currentSpeed * Time.deltaTime;

            // 前進時のみ向きを合わせたいなら条件を付ける。ここでは常に向ける：
            transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
        }

        // ★ このフレームの結果を“ロック値”として記録
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
