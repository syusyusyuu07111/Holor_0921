using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
public class TPSCamera : MonoBehaviour
{
    InputSystem_Actions input;
    //カメラ入力設定==============================================================================--
    public float yaw=90;//向いている角度
    public float RotateSpeed=3.0f;
    public Transform Camera;
    public Transform Pivot;
    public float Distance=3.0f;
    public Transform cam;
    [Header("キャラクター（プレイヤー）")]
    public Transform Player;
    public float AimSpeed=5.0f;
    public float YawPlayer;
    public float prevplayerrow;//前フレームのプレイヤーの向いている角度
    public float Deadyaw = 0.5f;//無視する角度　少しの角度は回転に考慮しない
    [Range(0f,1f)]float RowAmount=1.0f;//キャラクターとカメラをどのくらい追従させるか
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
        //カメラの回転の初期パラメータを取得しておく==========================================================
        if (cam != null) cam=transform;
        yaw = cam.eulerAngles.y;
        prevplayerrow = Player.eulerAngles.y;
    }
    void Update()
    {
        //カメラ設定==========================================================================---
        Vector2 LookInput = input.Player.Look.ReadValue<Vector2>();
        yaw += LookInput.x * RotateSpeed * Time.deltaTime;
        Quaternion rot = Quaternion.Euler(0, yaw, 0);
        Vector3 CameraPos = Pivot.transform.position + rot * new Vector3(0, 0, -Distance);
        cam.position = CameraPos;
        cam.LookAt(Pivot.transform.position,Vector3.up);

        //カメラを回転させたらキャラも回転させる--------------------------------------------------
        if(Player!=null)
        {
            quaternion target = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
            Player.rotation = Quaternion.Slerp(Player.rotation,target, AimSpeed*Time.deltaTime);
        }
    }
}
