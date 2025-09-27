using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
public class PlayerController : MonoBehaviour
{
    InputSystem_Actions Input;
    [SerializeField] Transform Camera;
    [SerializeField] float MoveSpeed = 5.0f;
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
        //カメラ座標から前の向きを取得---------------------------------------------------------------------
        Vector3 Forward = Camera.transform.forward;
        Forward.y = 0;
        Forward.Normalize();
        //カメラ座標から横の向きを取得する-----------------------------------------------------------------
        Vector3 Right = Camera.transform.right;
        Right.y = 0;
        Right.Normalize();

        //プレイヤーの移動==================================================================================
        {
            //移動する方向を決めて移動する--------------------------------------------------------------------------
            //前向きに進むときの挙動--------------------------------------------------------------------------------
            Vector3 Movedir = Forward * MoveInput.y + Right * MoveInput.x;
            if (Movedir.sqrMagnitude > 0.0001f&&MoveInput.y>=0)
            {
                transform.position += Movedir * Time.deltaTime * MoveSpeed;
                //移動する方向にキャラクターの向きを変える------------------------------------------------------------
                transform.rotation = Quaternion.LookRotation(Movedir, Vector3.up);
            }
            //後ろ向きに進むときの挙動　回転せずに後ろずさりする------------------------------------------------------
            else if(Movedir.magnitude>0.00001f&&MoveInput.y<0)
            {
                transform.position += Movedir * Time.deltaTime * MoveSpeed;
            }
            //アニメーション更新=========================================================================================
            // --- Boolでアニメ切り替え ---
            bool hasInput = MoveInput != Vector2.zero;    // ★存在しない 'move' ではなく 'MoveInput' を使う

            if (hasInput)
            {
                noInputTimer = 0f;
                if (animator && !animator.GetBool("IsMoving"))
                    animator.SetBool("IsMoving", true);
            }
            else
            {
                noInputTimer += Time.deltaTime;
                if (noInputTimer >= stopGrace && animator && animator.GetBool("IsMoving"))
                    animator.SetBool("IsMoving", false);
            }
        }
    }
}
