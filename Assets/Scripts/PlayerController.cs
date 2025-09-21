using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerController : MonoBehaviour
{
    InputSystem_Actions Input;
    [SerializeField] Transform Camera;
    [SerializeField] float MoveSpeed = 5.0f;


    private void Awake()
    {
        Input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        Input.Player.Enable();
    }
    void Start()
    {

    }

    // Update is called once per frame
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
            Vector3 Movedir = Forward * MoveInput.y + Right * MoveInput.x;
            if (Movedir.sqrMagnitude > 0.0001f)
                transform.position += Movedir * Time.deltaTime * MoveSpeed;
        }
    }
}
