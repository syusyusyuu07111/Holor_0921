using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class TPSCamera : MonoBehaviour
{
    private InputSystem_Actions input;

    // ===== カメラ入力/基本 =====
    [Header("基本")]
    public bool ControlEnable = true;
    public Transform cam;                 // カメラ本体（通常は this.transform）
    public Transform Pivot;               // 注視点（プレイヤー頭等）
    public float Distance = 3.0f;         // Pivot からの距離

    [Header("回転（横/Yaw）")]
    public float yaw = 90f;               // 向いている角度（水平）
    public float RotateSpeed = 1.0f;      // 感度（共通）

    [Header("回転（縦/Pitch）")]
    public float pitch = 0f;              // 現在のピッチ
    public Vector2 PitchClamp = new Vector2(-40f, 70f);
    public float PitchSmoothTime = 0.06f; // スムーズ追従
    public float MaxPitchSpeed = 240f;    // 最大角速度（deg/s）
    public float MouseYDeadZone = 0.02f;  // 縦の微小入力カット
    public float PitchUpLimit = 30f;      // 上方向リミット
    public float PitchDownLimit = 10f;    // 下方向リミット
    [Range(0.1f, 1.0f)] public float VerticalAmount = 0.5f;

    // 反転フラグ（Optionからいじる）
    public bool InvertX = false;          // 左右反転 ← 新しく追加
    public bool InvertY = false;          // 上下反転（元からあったものそのまま）

    private float _pitchVel;              // SmoothDamp用
    private float _pitchTarget;           // 目標ピッチ

    [Header("デバイス別 感度係数")]
    public float MouseSense = 1.0f;
    public float GamepadSense = 3.0f;

    [Header("プレイヤー追従")]
    public Transform Player;
    public bool RotatePlayerWithCamera = false;
    public float AimSpeed = 5.0f;         // プレイヤー回転追従の速さ
    public float Deadyaw = 0.5f;          // 微小角の無視
    public Vector2 MoveInput;             // 外から歩行入力もらう想定
    public bool RotatePlayerOnlyWhenMoving = true;

    [Header("ショルダー/配置")]
    public Vector3 ShoulderOffset = new Vector3(0.4f, 0.0f, 0f);
    public KeyCode ShoulderSwapKey = KeyCode.E;
    public KeyCode QuickTurnKey = KeyCode.None; // 未使用

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

    // ===== UI: 感度（Slider）※今はOptionから制御してるので任意 =====
    [Header("UI: 感度（Slider 任意）")]
    public Slider RotateSpeedSlider;            // 使わないなら未割り当てでOK
    public float MinRotateSpeed = 0.1f;         // 感度の下限
    public float MaxRotateSpeed = 10f;          // 感度の上限
    public TMP_Text RotateSpeedLabel;           // 任意表示

    public bool SaveSensitivity = true;
    public string SensitivityPrefsKey = "Camera.RotateSpeed";

    public void Awake()
    {
        input = new InputSystem_Actions();
        if (cam == null) cam = transform;
    }

    public void OnEnable()
    {
        input.Player.Enable();

        // Slider初期化 (使ってないならnullのままでOK)
        if (RotateSpeedSlider)
        {
            RotateSpeedSlider.minValue = MinRotateSpeed;
            RotateSpeedSlider.maxValue = MaxRotateSpeed;

            float init = RotateSpeed;
            if (SaveSensitivity && PlayerPrefs.HasKey(SensitivityPrefsKey))
            {
                init = Mathf.Clamp(
                    PlayerPrefs.GetFloat(SensitivityPrefsKey),
                    MinRotateSpeed,
                    MaxRotateSpeed
                );
            }

            RotateSpeed = init;
            RotateSpeedSlider.SetValueWithoutNotify(init);
            RotateSpeedSlider.onValueChanged.AddListener(OnRotateSpeedChanged);
        }

        RefreshSensitivityLabel();
    }

    public void OnDisable()
    {
        input?.Player.Disable();

        if (RotateSpeedSlider)
        {
            RotateSpeedSlider.onValueChanged.RemoveListener(OnRotateSpeedChanged);
        }

        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
            PlayerPrefs.Save();
        }
    }

    void Start()
    {
        // 角度初期化
        yaw = cam.eulerAngles.y;
        _pitchTarget = pitch;

        // FOV初期
        if (UCam == null) UCam = GetComponentInChildren<Camera>();
        if (UCam != null) UCam.fieldOfView = FOVNormal;

        // 上下制限初期同期
        PitchClamp = new Vector2(-PitchDownLimit, PitchUpLimit);

        // UIの初期同期
        if (RotateSpeedSlider)
            RotateSpeedSlider.SetValueWithoutNotify(RotateSpeed);
        RefreshSensitivityLabel();
    }

    void LateUpdate()
    {
        if (!ControlEnable || cam == null || Pivot == null) return;

        // deltaTimeが止まってるときは安全に抜ける
        if (Time.deltaTime <= Mathf.Epsilon)
        {
            _pitchVel = 0f;
            _camVel = Vector3.zero;
            return;
        }

        // Look入力
        Vector2 look = input.Player.Look.ReadValue<Vector2>();

        // どのデバイスっぽいかで係数
        bool usingGamepad =
            (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame);
        float deviceSense = usingGamepad ? GamepadSense : MouseSense;

        // ショルダー入れ替え（お好み）
        if (ShoulderSwapKey != KeyCode.None && Input.GetKeyDown(ShoulderSwapKey))
            ShoulderOffset.x *= -1f;

        // 上下角のClamp更新
        PitchUpLimit = Mathf.Clamp(PitchUpLimit, 0f, 80f);
        PitchDownLimit = Mathf.Clamp(PitchDownLimit, 0f, 80f);
        PitchClamp.x = -PitchDownLimit;
        PitchClamp.y = PitchUpLimit;

        // ===== 回転更新 =====
        float dx = look.x;
        if (InvertX) dx = -dx; // ← 左右反転ここで反映

        float ly = look.y;
        if (InvertY) ly = -ly; // ← 上下反転ここで反映

        if (Mathf.Abs(ly) < MouseYDeadZone) ly = 0f;

        yaw += dx * RotateSpeed * deviceSense;

        _pitchTarget = Mathf.Clamp(
            _pitchTarget - ly * RotateSpeed * deviceSense * Mathf.Clamp01(VerticalAmount),
            PitchClamp.x, PitchClamp.y
        );
        pitch = Mathf.SmoothDamp(
            pitch,
            _pitchTarget,
            ref _pitchVel,
            PitchSmoothTime,
            MaxPitchSpeed
        );

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        // ===== カメラの距離補正（衝突）=====
        float d = Distance;
        if (!KeepFixedDistance)
        {
            Vector3 backDir = rot * Vector3.back;
            if (Physics.SphereCast(
                    Pivot.position,
                    CameraCollisionRadius,
                    backDir,
                    out RaycastHit hit,
                    Distance,
                    CollisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                d = Mathf.Max(MinCameraDistance, hit.distance - CollisionBuffer);
            }
        }

        // 目標位置
        Vector3 desiredPos =
            Pivot.position + rot * new Vector3(ShoulderOffset.x, ShoulderOffset.y, -d);

        // 位置スムーズ
        cam.position = Vector3.SmoothDamp(
            cam.position,
            desiredPos,
            ref _camVel,
            Mathf.Max(0f, PositionSmoothTime)
        );
        cam.LookAt(Pivot.position, Vector3.up);

        // プレイヤー向きも回すかどうか
        if (RotatePlayerWithCamera && Player != null)
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

        // FOV補間
        if (UCam != null)
        {
            float targetFov = IsAiming ? FOVAim : FOVNormal;
            UCam.fieldOfView = Mathf.Lerp(UCam.fieldOfView, targetFov, FOVLerp * Time.deltaTime);
        }
    }

    // ====== Option 側から感度をセットするための関数 ======
    public void SetRotateSpeedFromOption(float v)
    {
        RotateSpeed = Mathf.Clamp(v, MinRotateSpeed, MaxRotateSpeed);
        RefreshSensitivityLabel();

        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
        }
    }

    // ===== Slider用コールバック（未使用でもOK）=====
    private void OnRotateSpeedChanged(float v)
    {
        SetRotateSpeedFromOption(v);
    }

    private void RefreshSensitivityLabel()
    {
        if (RotateSpeedLabel)
        {
            RotateSpeedLabel.text = $"Sensitivity : {RotateSpeed:0.00}";
        }
    }
}
