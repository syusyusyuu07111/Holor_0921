using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
public class TPSCamera : MonoBehaviour
{
    InputSystem_Actions input;
    //�J�������͐ݒ�==============================================================================--
    public float yaw=90;//�����Ă���p�x
    public float RotateSpeed=3.0f;
    public Transform Camera;
    public Transform Pivot;
    public float Distance=3.0f;
    public Transform cam;
    public void Awake()
    {
        input = new InputSystem_Actions();
    }
    public void OnEnable()
    {
        input.Player.Enable();
    }
    void Start()
    {
        //�J�����̉�]�̏����p�����[�^���擾���Ă���==========================================================
        if (cam != null) cam=transform;
        yaw = cam.eulerAngles.y;
    }
    void Update()
    {
        //�J�����ݒ�==========================================================================---
        Vector2 LookInput = input.Player.Look.ReadValue<Vector2>();
        yaw += LookInput.x * RotateSpeed * Time.deltaTime;
        Quaternion rot = Quaternion.Euler(0, yaw, 0);
        Vector3 CameraPos = Pivot.transform.position + rot * new Vector3(0, 0, -Distance);
        cam.position = CameraPos;
        cam.LookAt(Pivot.transform.position,Vector3.up);
    }
}
