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
        //�J�������W����ړ�����������擾����=============================================================
        Vector2 MoveInput = Input.Player.Move.ReadValue<Vector2>();
        //�J�������W����O�̌������擾---------------------------------------------------------------------
        Vector3 Forward = Camera.transform.forward;
        Forward.y = 0;
        Forward.Normalize();
        //�J�������W���牡�̌������擾����-----------------------------------------------------------------
        Vector3 Right = Camera.transform.right;
        Right.y = 0;
        Right.Normalize();

        //�v���C���[�̈ړ�==================================================================================
        {
            //�ړ�������������߂Ĉړ�����--------------------------------------------------------------------------
            Vector3 Movedir = Forward * MoveInput.y + Right * MoveInput.x;
            if (Movedir.sqrMagnitude > 0.0001f)
                transform.position += Movedir * Time.deltaTime * MoveSpeed;
        }
    }
}
