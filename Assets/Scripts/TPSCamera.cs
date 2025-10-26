using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class TPSCamera : MonoBehaviour
{
    private InputSystem_Actions input;

    // ===== カメラ入力/基本 =====
    [Header("基本")]
    public bool ControlEnable = true;
    public Transform cam;
    public Transform Pivot;
    public float Distance = 3.0f;

    [Header("回転（横/Yaw）")]
    public float yaw = 90f;
    public float RotateSpeed = 1.0f;

    [Header("回転（縦/Pitch）")]
    public float pitch = 0f;
    public Vector2 PitchClamp = new Vector2(-40f, 70f);
    public float PitchSmoothTime = 0.06f;
    public float MaxPitchSpeed = 240f;
    public float MouseYDeadZone = 0.02f;
    public float PitchUpLimit = 30f;
    public float PitchDownLimit = 10f;
    [Range(0.1f, 1.0f)] public float VerticalAmount = 0.5f;
    public bool InvertY = false;

    private float _pitchVel;
    private float _pitchTarget;

    [Header("デバイス別 感度係数")]
    public float MouseSense = 1.0f;
    public float GamepadSense = 3.0f;

    [Header("プレイヤー追従")]
    public Transform Player;
    public bool RotatePlayerWithCamera = false;
    public float AimSpeed = 5.0f;
    public float Deadyaw = 0.5f;

    [Tooltip("移動入力（移動側から代入想定）")]
    public Vector2 MoveInput;
    public bool RotatePlayerOnlyWhenMoving = true;

    [Header("ショルダー/配置")]
    public Vector3 ShoulderOffset = new Vector3(0.4f, 0.0f, 0f);
    public KeyCode ShoulderSwapKey = KeyCode.E;
    public KeyCode QuickTurnKey = KeyCode.None;

    [Header("カメラ衝突処理")]
    public LayerMask CollisionMask = ~0;
    public float CollisionBuffer = 0.05f;
    public float MinCameraDistance = 0.1f;
    public bool KeepFixedDistance = false;
    public float CameraCollisionRadius = 0.15f;

    [Header("スムージング")]
    public float PositionSmoothTime = 0.08f;
    private Vector3 _camVel;

    [Header("FOV")]
    public Camera UCam;
    public float FOVNormal = 60f;
    public float FOVAim = 50f;
    public float FOVLerp = 10f;
    public bool IsAiming = false;

    // ===== 感度保存/表示 =====
    [Header("感度パラメータ")]
    public float MinRotateSpeed = 0.1f;
    public float MaxRotateSpeed = 10f;

    [Tooltip("数値を出したいときだけ割り当て(任意)")]
    public TMP_Text RotateSpeedLabel;

    [Tooltip("感度をPlayerPrefsへ保存するか")]
    public bool SaveSensitivity = true;
    public string SensitivityPrefsKey = "Camera.RotateSpeed";

    public void Awake()
    {
        input = new InputSystem_Actions();
        if (!cam) cam = transform;

        if (SaveSensitivity && PlayerPrefs.HasKey(SensitivityPrefsKey))
        {
            float saved = PlayerPrefs.GetFloat(SensitivityPrefsKey);
            RotateSpeed = Mathf.Clamp(saved, MinRotateSpeed, MaxRotateSpeed);
        }

        RefreshSensitivityLabel();
    }

    public void OnEnable()
    {
        input.Player.Enable();

        yaw = cam.eulerAngles.y;
        _pitchTarget = pitch;

        if (!UCam) UCam = GetComponentInChildren<Camera>();
        if (UCam) UCam.fieldOfView = FOVNormal;

        PitchClamp = new Vector2(-PitchDownLimit, PitchUpLimit);
    }

    public void OnDisable()
    {
        input?.Player.Disable();

        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
            PlayerPrefs.Save();
        }
    }

    private void LateUpdate()
    {
        if (!ControlEnable || !cam || !Pivot) return;

        if (Time.deltaTime <= Mathf.Epsilon)
        {
            _pitchVel = 0f;
            _camVel = Vector3.zero;
            return;
        }

        Vector2 look = input.Player.Look.ReadValue<Vector2>();
        bool usingGamepad = (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame);
        float deviceSense = usingGamepad ? GamepadSense : MouseSense;

        if (ShoulderSwapKey != KeyCode.None && Input.GetKeyDown(ShoulderSwapKey))
            ShoulderOffset.x *= -1f;

        PitchUpLimit = Mathf.Clamp(PitchUpLimit, 0f, 80f);
        PitchDownLimit = Mathf.Clamp(PitchDownLimit, 0f, 80f);
        PitchClamp.x = -PitchDownLimit;
        PitchClamp.y = PitchUpLimit;

        float dx = look.x;
        float ly = InvertY ? -look.y : look.y;
        if (Mathf.Abs(ly) < MouseYDeadZone) ly = 0f;

        yaw += dx * RotateSpeed * deviceSense;

        _pitchTarget = Mathf.Clamp(
            _pitchTarget - ly * RotateSpeed * deviceSense * Mathf.Clamp01(VerticalAmount),
            PitchClamp.x, PitchClamp.y
        );
        pitch = Mathf.SmoothDamp(pitch, _pitchTarget, ref _pitchVel, PitchSmoothTime, MaxPitchSpeed);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        float d = Distance;
        if (!KeepFixedDistance)
        {
            Vector3 backDir = rot * Vector3.back;
            if (Physics.SphereCast(Pivot.position, CameraCollisionRadius, backDir,
                                   out RaycastHit hit, Distance, CollisionMask, QueryTriggerInteraction.Ignore))
            {
                d = Mathf.Max(MinCameraDistance, hit.distance - CollisionBuffer);
            }
        }

        Vector3 desiredPos = Pivot.position + rot * new Vector3(ShoulderOffset.x, ShoulderOffset.y, -d);

        cam.position = Vector3.SmoothDamp(cam.position, desiredPos, ref _camVel, Mathf.Max(0f, PositionSmoothTime));
        cam.LookAt(Pivot.position, Vector3.up);

        if (RotatePlayerWithCamera && Player)
        {
            bool moving = MoveInput.sqrMagnitude > 0.0001f;
            if (!RotatePlayerOnlyWhenMoving || moving)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(Player.eulerAngles.y, cam.eulerAngles.y));
                if (diff > Mathf.Max(Deadyaw, 3f))
                {
                    Quaternion t = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
                    Player.rotation = Quaternion.Slerp(Player.rotation, t, AimSpeed * Time.deltaTime);
                }
            }
        }

        if (UCam)
        {
            float targetFov = IsAiming ? FOVAim : FOVNormal;
            UCam.fieldOfView = Mathf.Lerp(UCam.fieldOfView, targetFov, FOVLerp * Time.deltaTime);
        }
    }

    // ===== Option から呼ぶ：感度を安全に反映 =====
    public void SetRotateSpeedFromOption(float newSpeed)
    {
        RotateSpeed = Mathf.Clamp(newSpeed, MinRotateSpeed, MaxRotateSpeed);
        RefreshSensitivityLabel();

        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
            PlayerPrefs.Save();
        }
    }

    private void RefreshSensitivityLabel()
    {
        if (RotateSpeedLabel != null)
            RotateSpeedLabel.text = $"Sensitivity : {RotateSpeed:0.00}";
    }
}
