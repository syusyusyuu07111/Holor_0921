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
    public bool InvertX = false;          // 左右反転
    public bool InvertY = false;          // 上下反転

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

    // ===== UI: 感度（Slider） 任意 =====
    [Header("UI: 感度（Slider 任意）")]
    public Slider RotateSpeedSlider;            // 使わないなら未割り当てでOK
    public float MinRotateSpeed = 0.1f;         // 感度の下限
    public float MaxRotateSpeed = 10f;          // 感度の上限
    public TMP_Text RotateSpeedLabel;           // 任意表示

    public bool SaveSensitivity = true;
    public string SensitivityPrefsKey = "Camera.RotateSpeed";

    // ===== ログ用に“そのフレームで採用した距離”を公開 =====
    public float CurrentDistance { get; private set; } = 0f;

    // ===== 隠れ演出（距離寄せ：差分方式） =====
    [Header("隠れ演出（距離寄せ）")]
    public bool UseHiddenDistance = false;
    [Min(0f)] public float HiddenDistanceDelta = 1.4f;
    public float HiddenDistanceLerp = 12f;
    private float _distanceRuntime = 0f;

    // ===== 覗き前進（マイクロ一人称） =====
    [Header("覗き前進（マイクロ一人称）")]
    public bool AllowFrontWhenHidden = true;
    [Min(0f)] public float PeekForward = 0.20f;
    public float PeekForwardLerp = 12f;
    public float NearClipWhilePeek = 0.02f;
    public float NearClipRestore = 0.03f;
    private float _peekZRuntime = 0f;

    // ===== 隙間アンカー（隠れ中：位置を固定） =====
    [Header("隙間アンカー（隠れ中のみ）")]
    public bool UseHiddenAnchor = false;            // 隠れ中だけ true
    public Transform HiddenAnchor;                  // 通常は Door/Player
    public Vector3 HiddenAnchorLocalOffset = new Vector3(0f, 0f, 0.2f); // ローカル：X右/Z前

    // ===== 視界制限（隠れ中：前方180°）=====
    [Header("視界制限（隠れ中のみ）")]
    public bool EnableHiddenYawClamp = false;
    public float HiddenYawCenter = 0f;              // 外部から設定
    public float HiddenYawHalfAngle = 90f;          // 半角（90で180°）

    // ===== ルック先：固定/自由 =====
    [Header("ルック先（隠れ中）")]
    public bool UseHiddenLookAt = false;            // 固定ターゲットを見るか
    public Transform HiddenLookAt;                  // 固定ターゲット
    public float HiddenLookDistance = 3.0f;         // 自由見回し時：前方の距離
    public bool InvertHiddenLookDir = false;        // （固定LookAt用）未使用でもOK

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

        _distanceRuntime = Distance;
        _peekZRuntime = 0f;
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

        // デバイス判定で係数
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
        if (InvertX) dx = -dx;

        float ly = look.y;
        if (InvertY) ly = -ly;

        if (Mathf.Abs(ly) < MouseYDeadZone) ly = 0f;

        yaw += dx * RotateSpeed * deviceSense;

        // ★ 隠れ中の視界制限（±HiddenYawHalfAngle）
        if (EnableHiddenYawClamp)
        {
            float minYaw = HiddenYawCenter - Mathf.Abs(HiddenYawHalfAngle);
            float maxYaw = HiddenYawCenter + Mathf.Abs(HiddenYawHalfAngle);
            yaw = ClampAngleWrapping(yaw, minYaw, maxYaw);
        }

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
        bool useCollision = !KeepFixedDistance;

        // 距離寄せ（差分）
        float baseDist = Distance;
        if (UseHiddenDistance)
        {
            float maxDelta = Mathf.Max(0f, Distance - MinCameraDistance);
            float clamped = Mathf.Clamp(HiddenDistanceDelta, 0f, maxDelta);
            baseDist = Mathf.Max(MinCameraDistance, Distance - clamped);
        }
        float targetDist = baseDist;
        _distanceRuntime = Mathf.Lerp(_distanceRuntime, targetDist, HiddenDistanceLerp * Time.deltaTime);

        float d = _distanceRuntime;
        if (useCollision)
        {
            Vector3 backDir = rot * Vector3.back;
            if (Physics.SphereCast(
                    Pivot.position,
                    CameraCollisionRadius,
                    backDir,
                    out RaycastHit hit,
                    _distanceRuntime,
                    CollisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                d = Mathf.Max(MinCameraDistance, hit.distance - CollisionBuffer);
            }
        }

        // 覗き前進（前方衝突）
        float targetPeek = 0f;
        if (UseHiddenDistance && AllowFrontWhenHidden)
        {
            targetPeek = Mathf.Min(PeekForward, Mathf.Max(0f, d - MinCameraDistance));
            if (useCollision && targetPeek > 0f && Physics.SphereCast(
                    Pivot.position,
                    CameraCollisionRadius,
                    rot * Vector3.forward,
                    out RaycastHit fhit,
                    targetPeek,
                    CollisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                targetPeek = Mathf.Max(0f, fhit.distance - CollisionBuffer);
            }
        }
        _peekZRuntime = Mathf.Lerp(_peekZRuntime, targetPeek, PeekForwardLerp * Time.deltaTime);

        // ★ログ用：このフレームで実際に使った距離を公開
        CurrentDistance = d;

        // 目標位置（隠れ中はアンカー位置に固定）
        Vector3 desiredPos;
        if (UseHiddenAnchor && HiddenAnchor != null)
        {
            desiredPos = HiddenAnchor.TransformPoint(HiddenAnchorLocalOffset);
        }
        else
        {
            desiredPos =
                Pivot.position + rot * new Vector3(ShoulderOffset.x, ShoulderOffset.y, -(d - _peekZRuntime));
        }

        // 位置スムーズ
        cam.position = Vector3.SmoothDamp(
            cam.position,
            desiredPos,
            ref _camVel,
            Mathf.Max(0f, PositionSmoothTime)
        );

        // ★ ルック先（自由見回し or 固定）
        Vector3 lookTarget = Pivot.position; // デフォ：TPS
        if (UseHiddenAnchor && HiddenAnchor != null)
        {
            if (UseHiddenLookAt && HiddenLookAt != null)
            {
                lookTarget = HiddenLookAt.position; // 固定ターゲット
            }
            else
            {
                // 自由見回し：現在の yaw/pitch の前方を見る
                lookTarget = cam.position + (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward)
                                        * Mathf.Max(0.01f, HiddenLookDistance);
            }
        }
        cam.LookAt(lookTarget, Vector3.up);

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

        // FOV補間 + NearClip（覗き時）
        if (UCam != null)
        {
            float targetFov = IsAiming ? FOVAim : FOVNormal;
            UCam.fieldOfView = Mathf.Lerp(UCam.fieldOfView, targetFov, FOVLerp * Time.deltaTime);

            float targetNear = (UseHiddenDistance && AllowFrontWhenHidden && _peekZRuntime > 0.0001f)
                ? NearClipWhilePeek : NearClipRestore;
            UCam.nearClipPlane = Mathf.Lerp(UCam.nearClipPlane, targetNear, 12f * Time.deltaTime);
        }
    }

    // 角度のラッピングClamp（-∞〜∞のyawをmin..maxの区間に収める）
    private static float ClampAngleWrapping(float angle, float min, float max)
    {
        angle = Mathf.DeltaAngle(0f, angle);
        min = Mathf.DeltaAngle(0f, min);
        max = Mathf.DeltaAngle(0f, max);

        if (min <= max)
        {
            return Mathf.Clamp(angle, min, max);
        }
        else
        {
            float a = Mathf.Clamp(angle, min, 180f);
            float b = Mathf.Clamp(angle, -180f, max);
            return Mathf.Abs(Mathf.DeltaAngle(angle, a)) < Mathf.Abs(Mathf.DeltaAngle(angle, b)) ? a : b;
        }
    }

    // ====== Option 側から感度をセット ======
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
