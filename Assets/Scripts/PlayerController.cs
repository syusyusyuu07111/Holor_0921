using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    InputSystem_Actions Input;
    [SerializeField] Transform Camera;
    [SerializeField] float MoveSpeed = 5.0f;
    [SerializeField] float DashSpeed = 7.0f;
    [SerializeField] float SlowSpeed = 2.0f;
    [SerializeField] Animator animator;//アニメーション
                                       // しきい値
    [SerializeField] float deadZone = 0.12f;   // スティックの遊び
    [SerializeField] float stopGrace = 0.08f;  // 離してからIdleに落とす遅延（ビビり防止）

    float noInputTimer = 0f;

    // 登り用のパラメータ
    [SerializeField] float stepHeight = 0.4f;          // 上りたい最大段差
    [SerializeField] float stepCheckDistance = 0.3f;   // 前方チェック距離
    [SerializeField] float stepSnapUpSpeed = 4.0f;     // 持ち上げ速度（1フレーム上限）
    [SerializeField] LayerMask stepMask = ~0;          // 当たり判定レイヤー（地形など）
    [SerializeField] CapsuleCollider col;              // カプセル。当たりから実寸を取る（任意）

    private void Awake()
    {
        Input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        Input.Player.Enable();
    }
    void Update()
    {
        //カメラ座標から移動する方向を取得する=============================================================
        Vector2 MoveInput = Input.Player.Move.ReadValue<Vector2>();
        //  そーっと歩くボタン（押している間だけ有効）
        bool isSlowWalking = Input.Player.SlowWalk.IsPressed();

        //カメラ座標から前の向きを取得---------------------------------------------------------------------
        Vector3 Forward = Camera.transform.forward;
        Forward.y = 0;
        Forward.Normalize();
        //カメラ座標から横の向きを取得する-----------------------------------------------------------------
        Vector3 Right = Camera.transform.right;
        Right.y = 0;
        Right.Normalize();

        //  このフレームで使う実速度（MoveSpeedは上書きしない）
        float currentSpeed = MoveSpeed;
        // ダッシュは「押してる間」かつ後退以外のときのみ有効
        bool isDashing = Input.Player.Dash.IsPressed() && MoveInput.y >= 0f;
        if (isDashing)
        {
            currentSpeed = DashSpeed;
        }

        // そーっと歩きはダッシュより優先（押されていれば常にSlowSpeed）
        if (isSlowWalking)
        {
            currentSpeed = SlowSpeed;
            isDashing = false;
        }

        //プレイヤーの移動==================================================================================
        {
            //移動する方向を決めて移動する--------------------------------------------------------------------------
            //前向きに進むときの挙動--------------------------------------------------------------------------------
            Vector3 Movedir = Forward * MoveInput.y + Right * MoveInput.x;
            if (Movedir.sqrMagnitude > 0.0001f && MoveInput.y >= 0)
            {
                Vector3 dir = Movedir.normalized; // 斜めの暴走防止

                // 前進時：段差登りを試行）
                TryStepUp(dir, Time.deltaTime);

                transform.position += dir * Time.deltaTime * currentSpeed; //currentSpeed を使用
                //移動する方向にキャラクターの向きを変える------------------------------------------------------------
                transform.rotation = Quaternion.LookRotation(Movedir, Vector3.up);
            }
            //後ろ向きに進むときの挙動　回転せずに後ろずさりする------------------------------------------------------
            else if (Movedir.magnitude > 0.00001f && MoveInput.y < 0)
            {
                Vector3 dir = Movedir.normalized;

                // （後退時：段差登りを試行）
                TryStepUp(dir, Time.deltaTime);

                //  後退時も「そーっと歩く」ならSlowSpeed、そうでなければ元の通常速度
                if (isSlowWalking)
                {
                    transform.position += dir * Time.deltaTime * SlowSpeed;
                }
                else
                {
                    transform.position += dir * Time.deltaTime * MoveSpeed; // ダッシュ無効（通常速度）
                }
                isDashing = false; //  後退中は常にダッシュOFF

                //段差も上れる機能を追加--------------------------------------------------------------------------------

            }

            //アニメーション更新=========================================================================================
            // --- Boolでアニメ切り替え ---
            bool hasInput = MoveInput != Vector2.zero;    //

            if (hasInput)
            {
                noInputTimer = 0f;
                if (animator && !animator.GetBool("IsMoving"))
                    animator.SetBool("IsMoving", true);

                //  ダッシュ中フラグ
                if (animator) animator.SetBool("IsDashing", isDashing); // ← ダッシュ中だけ true

                // そーっと歩きフラグ（必要ならAnimatorにBool「IsSlowWalking」を用意）
                if (animator) animator.SetBool("IsSlowWalking", isSlowWalking);
            }
            else
            {
                noInputTimer += Time.deltaTime;
                if (noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                    animator.SetBool("IsMoving", false);

                // 入力が無いときは必ずダッシュOFF
                if (animator) animator.SetBool("IsDashing", false);

                // 入力なし時はそーっと歩きもOFF
                if (animator) animator.SetBool("IsSlowWalking", false);
            }
        }
    }

    // （段差登り本体。transform移動のまま“少し持ち上げてから進む”）
    void TryStepUp(Vector3 moveDir, float dt)
    {
        if (moveDir.sqrMagnitude < 0.0001f) return;

        // カプセルの実寸を取得（col が未設定なら緊急値で動作）
        float radius = 0.3f;
        float halfHeight = 0.9f;
        Vector3 center = transform.position;

        if (col)
        {
            radius = col.radius * Mathf.Max(transform.localScale.x, transform.localScale.z);
            halfHeight = Mathf.Max(col.height * 0.5f * transform.localScale.y, radius + 0.01f);
            center = col.bounds.center;
        }

        // カプセル上下端（縦カプセル想定）
        Vector3 pTop = center + Vector3.up * (halfHeight - radius);
        Vector3 pBot = center - Vector3.up * (halfHeight - radius);

        Vector3 dir = moveDir.normalized;
        float checkDist = Mathf.Max(stepCheckDistance, radius + 0.05f);

        // 足元側でヒット、段差高さぶん上ではヒットなし → 段差と判定
        bool lowHit = Physics.CapsuleCast(
            pTop, pBot, radius, dir, out _, checkDist, stepMask, QueryTriggerInteraction.Ignore);

        if (!lowHit) return;

        // このフレーム分だけ上に持ち上げ（スナップではなく連続）
        float lift = Mathf.Min(stepSnapUpSpeed * dt, stepHeight);
        if (lift <= 0f) return;

        Vector3 up = Vector3.up * lift;

        bool upBlocked = Physics.CapsuleCast(
            pTop + up, pBot + up, radius, dir, out _, checkDist, stepMask, QueryTriggerInteraction.Ignore);

        if (upBlocked) return;

        // 頭上クリア確認（天井や庇に当たらないか）
        bool ceiling = Physics.CheckCapsule(
            pTop + up, pBot + up, radius, stepMask, QueryTriggerInteraction.Ignore);

        if (ceiling) return;

        // 実際に少し持ち上げる
        transform.position += up;
    }

    // 可視化
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (!Camera) return;

        // 前方ベクトル（水平化）
        Vector3 forward = Camera.transform.forward;
        forward.y = 0; forward.Normalize();

        // カプセル寸法（実行時の最新値）
        float radius = 0.3f;
        float halfHeight = 0.9f;
        Vector3 center = transform.position;

        if (col)
        {
            radius = col.radius * Mathf.Max(transform.localScale.x, transform.localScale.z);
            halfHeight = Mathf.Max(col.height * 0.5f * transform.localScale.y, radius + 0.01f);
            center = col.bounds.center;
        }

        Vector3 pTop = center + Vector3.up * (halfHeight - radius);
        Vector3 pBot = center - Vector3.up * (halfHeight - radius);

        float d = Mathf.Max(stepCheckDistance, radius + 0.05f);

        Gizmos.color = Color.yellow; Gizmos.DrawLine(pTop, pBot);
        Gizmos.color = Color.red; Gizmos.DrawLine(pBot, pBot + forward * d);
        Gizmos.color = Color.green; Gizmos.DrawLine(pBot + Vector3.up * stepHeight, pBot + Vector3.up * stepHeight + forward * d);
    }
}
