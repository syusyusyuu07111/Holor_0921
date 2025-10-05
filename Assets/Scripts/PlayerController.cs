using UnityEngine;

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
                transform.position += dir * Time.deltaTime * currentSpeed; //currentSpeed を使用
                //移動する方向にキャラクターの向きを変える------------------------------------------------------------
                transform.rotation = Quaternion.LookRotation(Movedir, Vector3.up);
            }
            //後ろ向きに進むときの挙動　回転せずに後ろずさりする------------------------------------------------------
            else if (Movedir.magnitude > 0.00001f && MoveInput.y < 0)
            {
                Vector3 dir = Movedir.normalized;
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
}
