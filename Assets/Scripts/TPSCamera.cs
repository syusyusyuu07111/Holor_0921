using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class TPSCamera : MonoBehaviour
{
    /*
        ============================================================
        TPSCamera がやっていること（内容は変えずにコメントを増やした版）
        ============================================================

        ■ざっくり役割
        ・TPS視点のカメラ制御（Yaw/Pitchで回転）
        ・Pivot（プレイヤー頭など）を中心に、一定距離後ろへ配置
        ・壁に当たるときは SphereCast でカメラを手前に寄せてめり込み防止
        ・隠れ中用の特別挙動
          - 距離を詰める（UseHiddenDistance + HiddenDistanceDelta）
          - さらに少し前に出す（PeekForward）※前方にも衝突チェック
          - カメラ位置を特定のアンカーに固定（UseHiddenAnchor）
          - 隠れ中だけYaw制限（EnableHiddenYawClamp）
          - 見る先を固定ターゲットor自由見回し（UseHiddenLookAt）
        ・必要なら、カメラのYawに合わせてプレイヤーも回転させる
        ・FOVを Aim/Normal で補間し、覗き中はNearClipも調整

        ■入力
        input.Player.Look の Vector2 を使って回転する
        InvertX / InvertY で反転
        Mouse / Gamepad で係数（MouseSense / GamepadSense）を変える

        ■外部から触るポイント
        ・Option から SetRotateSpeedFromOption() で感度を変更できる
        ・隠れ状態のときに UseHiddenDistance / UseHiddenAnchor / EnableHiddenYawClamp 等を外部でONにする想定

        ============================================================
    */

    // 入力（新InputSystemの自動生成クラス想定）
    private InputSystem_Actions input;

    // ============================================================
    // カメラ入力/基本
    // ============================================================
    [Header("基本")]
    public bool ControlEnable = true;   // false にするとカメラ更新しない（覗き演出などで使用）
    public Transform cam;              // カメラ本体（通常は this.transform）
    public Transform Pivot;            // 注視点（プレイヤー頭など）
    public float Distance = 3.0f;      // Pivot からの基本距離

    // ============================================================
    // 回転（Yaw：左右）
    // ============================================================
    [Header("回転（横/Yaw）")]
    public float yaw = 90f;            // 水平角
    public float RotateSpeed = 1.0f;   // 感度（共通）

    // ============================================================
    // 回転（Pitch：上下）
    // ============================================================
    [Header("回転（縦/Pitch）")]
    public float pitch = 0f;                   // 現在ピッチ
    public Vector2 PitchClamp = new Vector2(-40f, 70f);
    public float PitchSmoothTime = 0.06f;      // SmoothDamp の追従時間
    public float MaxPitchSpeed = 240f;         // SmoothDamp の最大角速度（deg/s）
    public float MouseYDeadZone = 0.02f;       // 微小入力カット
    public float PitchUpLimit = 30f;           // 上方向の最大角度
    public float PitchDownLimit = 10f;         // 下方向の最大角度
    [Range(0.1f, 1.0f)] public float VerticalAmount = 0.5f; // 縦入力の効き

    // Option から変更される想定の反転フラグ
    public bool InvertX = false;
    public bool InvertY = false;

    private float _pitchVel;       // SmoothDamp用速度
    private float _pitchTarget;    // 目標ピッチ（入力で動かすのはこっち）

    // ============================================================
    // デバイス別係数
    // ============================================================
    [Header("デバイス別 感度係数")]
    public float MouseSense = 1.0f;
    public float GamepadSense = 3.0f;

    // ============================================================
    // プレイヤー追従（任意）
    // ============================================================
    [Header("プレイヤー追従")]
    public Transform Player;
    public bool RotatePlayerWithCamera = false;  // trueならプレイヤーもカメラYawに追従
    public float AimSpeed = 5.0f;                // プレイヤー追従回転の速さ
    public float Deadyaw = 0.5f;                 // 微小角度は無視
    public Vector2 MoveInput;                    // 外部から歩行入力を入れる想定
    public bool RotatePlayerOnlyWhenMoving = true;

    // ============================================================
    // ショルダー/配置
    // ============================================================
    [Header("ショルダー/配置")]
    public Vector3 ShoulderOffset = new Vector3(0.4f, 0.0f, 0f); // 右肩/左肩のズラし
    public KeyCode ShoulderSwapKey = KeyCode.E;                  // 押すと左右反転
    public KeyCode QuickTurnKey = KeyCode.None;                  // 未使用

    // ============================================================
    // カメラ衝突
    // ============================================================
    [Header("カメラ衝突処理")]
    public LayerMask CollisionMask = ~0;
    public float CollisionBuffer = 0.05f;       // 壁から少し手前に止める
    public float MinCameraDistance = 0.1f;      // 最低距離（0だとめり込みやすい）
    public bool KeepFixedDistance = false;      // trueなら衝突補正をしない
    public float CameraCollisionRadius = 0.15f; // SphereCast の半径

    // ============================================================
    // 位置スムージング
    // ============================================================
    [Header("スムージング")]
    public float PositionSmoothTime = 0.08f;
    private Vector3 _camVel; // SmoothDamp用

    // ============================================================
    // FOV
    // ============================================================
    [Header("FOV")]
    public Camera UCam;
    public float FOVNormal = 60f;
    public float FOVAim = 50f;
    public float FOVLerp = 10f;
    public bool IsAiming = false;

    // ============================================================
    // UI: 感度（Slider） 任意
    // ============================================================
    [Header("UI: 感度（Slider 任意）")]
    public Slider RotateSpeedSlider;
    public float MinRotateSpeed = 0.1f;
    public float MaxRotateSpeed = 10f;
    public TMP_Text RotateSpeedLabel;

    public bool SaveSensitivity = true;
    public string SensitivityPrefsKey = "Camera.RotateSpeed";

    // ログ用：このフレームで採用した距離（衝突後の距離）
    public float CurrentDistance { get; private set; } = 0f;

    // ============================================================
    // 隠れ演出（距離寄せ：差分方式）
    // ============================================================
    [Header("隠れ演出（距離寄せ）")]
    public bool UseHiddenDistance = false;     // 隠れ中にONにする想定
    [Min(0f)] public float HiddenDistanceDelta = 1.4f; // Distance からどれだけ引くか
    public float HiddenDistanceLerp = 12f;
    private float _distanceRuntime = 0f;       // 実際に補間中の距離

    // ============================================================
    // 覗き前進（マイクロ一人称）
    // ============================================================
    [Header("覗き前進（マイクロ一人称）")]
    public bool AllowFrontWhenHidden = true;
    [Min(0f)] public float PeekForward = 0.20f;
    public float PeekForwardLerp = 12f;
    public float NearClipWhilePeek = 0.02f;
    public float NearClipRestore = 0.03f;
    private float _peekZRuntime = 0f; // 前進量の補間値

    // ============================================================
    // 隙間アンカー（隠れ中：位置固定）
    // ============================================================
    [Header("隙間アンカー（隠れ中のみ）")]
    public bool UseHiddenAnchor = false;
    public Transform HiddenAnchor;
    public Vector3 HiddenAnchorLocalOffset = new Vector3(0f, 0f, 0.2f);

    // ============================================================
    // 視界制限（隠れ中：前方180°）
    // ============================================================
    [Header("視界制限（隠れ中のみ）")]
    public bool EnableHiddenYawClamp = false;
    public float HiddenYawCenter = 0f;   // 外部でセット（隠れ開始時にカメラYaw等）
    public float HiddenYawHalfAngle = 90f;

    // ============================================================
    // ルック先（隠れ中）
    // ============================================================
    [Header("ルック先（隠れ中）")]
    public bool UseHiddenLookAt = false;
    public Transform HiddenLookAt;
    public float HiddenLookDistance = 3.0f;
    public bool InvertHiddenLookDir = false; // 未使用でもOK（互換/将来用）

    // ============================================================
    // Unity lifecycle
    // ============================================================
    public void Awake()
    {
        input = new InputSystem_Actions();

        // cam 未設定なら自分をカメラ扱いにする
        if (cam == null) cam = transform;
    }

    public void OnEnable()
    {
        // 入力有効化
        input.Player.Enable();

        // Slider 初期化（使わないなら null のままでOK）
        if (RotateSpeedSlider)
        {
            RotateSpeedSlider.minValue = MinRotateSpeed;
            RotateSpeedSlider.maxValue = MaxRotateSpeed;

            // 保存しているならPrefsから初期値取得
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

            // UI操作で感度変更されたら反映
            RotateSpeedSlider.onValueChanged.AddListener(OnRotateSpeedChanged);
        }

        RefreshSensitivityLabel();
    }

    public void OnDisable()
    {
        // 入力無効化
        input?.Player.Disable();

        // Slider リスナー解除
        if (RotateSpeedSlider)
        {
            RotateSpeedSlider.onValueChanged.RemoveListener(OnRotateSpeedChanged);
        }

        // 感度保存（OnDisable時に確定）
        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
            PlayerPrefs.Save();
        }
    }

    void Start()
    {
        // 角度初期化（現在のTransformに合わせる）
        yaw = cam.eulerAngles.y;
        _pitchTarget = pitch;

        // FOV初期
        if (UCam == null) UCam = GetComponentInChildren<Camera>();
        if (UCam != null) UCam.fieldOfView = FOVNormal;

        // 上下制限を現パラメータに同期
        PitchClamp = new Vector2(-PitchDownLimit, PitchUpLimit);

        // UI初期同期
        if (RotateSpeedSlider)
            RotateSpeedSlider.SetValueWithoutNotify(RotateSpeed);
        RefreshSensitivityLabel();

        // Runtime用の初期値
        _distanceRuntime = Distance;
        _peekZRuntime = 0f;
    }

    // ============================================================
    // カメラ更新（LateUpdate 推奨）
    // ============================================================
    void LateUpdate()
    {
        // 制御OFF / 参照不足なら何もしない
        if (!ControlEnable || cam == null || Pivot == null) return;

        // TimeScale=0などでdeltaTimeが止まっている時は暴れ防止
        if (Time.deltaTime <= Mathf.Epsilon)
        {
            _pitchVel = 0f;
            _camVel = Vector3.zero;
            return;
        }

        // ----------------------------
        // Look入力取得
        // ----------------------------
        Vector2 look = input.Player.Look.ReadValue<Vector2>();

        // デバイス判定（直近フレームで更新されたならゲームパッド扱い）
        bool usingGamepad =
            (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame);
        float deviceSense = usingGamepad ? GamepadSense : MouseSense;

        // ショルダー入れ替え（左右オフセット反転）
        if (ShoulderSwapKey != KeyCode.None && Input.GetKeyDown(ShoulderSwapKey))
            ShoulderOffset.x *= -1f;

        // ----------------------------
        // PitchClamp を毎フレーム更新（可変調整のため）
        // ----------------------------
        PitchUpLimit = Mathf.Clamp(PitchUpLimit, 0f, 80f);
        PitchDownLimit = Mathf.Clamp(PitchDownLimit, 0f, 80f);
        PitchClamp.x = -PitchDownLimit;
        PitchClamp.y = PitchUpLimit;

        // ----------------------------
        // 回転更新（Yaw/Pitch）
        // ----------------------------
        float dx = look.x;
        if (InvertX) dx = -dx;

        float ly = look.y;
        if (InvertY) ly = -ly;

        // 縦の微小入力は捨てる（ノイズ対策）
        if (Mathf.Abs(ly) < MouseYDeadZone) ly = 0f;

        // yaw は積算
        yaw += dx * RotateSpeed * deviceSense;

        // 隠れ中のYaw制限（中心±半角にクランプ）
        if (EnableHiddenYawClamp)
        {
            float minYaw = HiddenYawCenter - Mathf.Abs(HiddenYawHalfAngle);
            float maxYaw = HiddenYawCenter + Mathf.Abs(HiddenYawHalfAngle);
            yaw = ClampAngleWrapping(yaw, minYaw, maxYaw);
        }

        // pitch はターゲットを作って SmoothDamp で追従
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

        // 今フレームの回転
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        // ============================================================
        // 距離補正（隠れ中の距離寄せ + 壁衝突）
        // ============================================================
        bool useCollision = !KeepFixedDistance;

        // (1) 隠れ中なら Distance から HiddenDistanceDelta だけ引いた距離へ
        float baseDist = Distance;
        if (UseHiddenDistance)
        {
            // MinCameraDistance を割らないように安全にクランプ
            float maxDelta = Mathf.Max(0f, Distance - MinCameraDistance);
            float clamped = Mathf.Clamp(HiddenDistanceDelta, 0f, maxDelta);
            baseDist = Mathf.Max(MinCameraDistance, Distance - clamped);
        }

        // (2) 距離は補間して急に変わらないようにする
        float targetDist = baseDist;
        _distanceRuntime = Mathf.Lerp(_distanceRuntime, targetDist, HiddenDistanceLerp * Time.deltaTime);

        // (3) 衝突補正：Pivotから後ろ方向へ SphereCast → 壁があれば手前に寄せる
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

        // ============================================================
        // 覗き前進（前方向へ少し進む）※前方も衝突チェックする
        // ============================================================
        float targetPeek = 0f;
        if (UseHiddenDistance && AllowFrontWhenHidden)
        {
            // 前に出られる上限：今の距離 d から MinCameraDistance まで
            targetPeek = Mathf.Min(PeekForward, Mathf.Max(0f, d - MinCameraDistance));

            // 前方にも壁があるなら、そこまでに制限
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

        // 前進量も補間して滑らかに
        _peekZRuntime = Mathf.Lerp(_peekZRuntime, targetPeek, PeekForwardLerp * Time.deltaTime);

        // このフレームで採用した距離を公開（デバッグ用）
        CurrentDistance = d;

        // ============================================================
        // 目標位置決定（通常TPS or 隠れアンカー固定）
        // ============================================================
        Vector3 desiredPos;

        if (UseHiddenAnchor && HiddenAnchor != null)
        {
            // 隠れ中：アンカーのローカルオフセット位置へ固定
            desiredPos = HiddenAnchor.TransformPoint(HiddenAnchorLocalOffset);
        }
        else
        {
            // 通常：Pivotから回転方向の後ろに配置（ショルダー分だけX/Yずらす）
            desiredPos =
                Pivot.position + rot * new Vector3(
                    ShoulderOffset.x,
                    ShoulderOffset.y,
                    -(d - _peekZRuntime) // 前進するぶんだけ距離を縮める
                );
        }

        // ============================================================
        // 位置スムーズ追従
        // ============================================================
        cam.position = Vector3.SmoothDamp(
            cam.position,
            desiredPos,
            ref _camVel,
            Mathf.Max(0f, PositionSmoothTime)
        );

        // ============================================================
        // ルック先（通常Pivot / 隠れ中は固定or自由見回し）
        // ============================================================
        Vector3 lookTarget = Pivot.position;

        // 隠れ中だけルック先を切り替える（アンカーを使っている＝隠れモード扱い）
        if (UseHiddenAnchor && HiddenAnchor != null)
        {
            if (UseHiddenLookAt && HiddenLookAt != null)
            {
                // 固定ターゲットを見る
                lookTarget = HiddenLookAt.position;
            }
            else
            {
                // 自由見回し：今の yaw/pitch の前方にある点を見る
                lookTarget = cam.position
                           + (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward)
                           * Mathf.Max(0.01f, HiddenLookDistance);
            }
        }

        // 実際に視線を合わせる
        cam.LookAt(lookTarget, Vector3.up);

        // ============================================================
        // プレイヤー向きも回す（任意）
        // ============================================================
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

        // ============================================================
        // FOV & NearClip
        // ============================================================
        if (UCam != null)
        {
            // Aim/Normal を補間
            float targetFov = IsAiming ? FOVAim : FOVNormal;
            UCam.fieldOfView = Mathf.Lerp(UCam.fieldOfView, targetFov, FOVLerp * Time.deltaTime);

            // 覗き前進中は near clip を下げて、手前の見切れを抑える
            float targetNear = (UseHiddenDistance && AllowFrontWhenHidden && _peekZRuntime > 0.0001f)
                ? NearClipWhilePeek
                : NearClipRestore;

            UCam.nearClipPlane = Mathf.Lerp(UCam.nearClipPlane, targetNear, 12f * Time.deltaTime);
        }
    }

    // ============================================================
    // 角度のラッピングClamp（yawが360をまたいでも破綻しないようにクランプ）
    // ============================================================
    private static float ClampAngleWrapping(float angle, float min, float max)
    {
        // Mathf.DeltaAngle(0, angle) は -180〜180 に正規化した角度を返す
        angle = Mathf.DeltaAngle(0f, angle);
        min = Mathf.DeltaAngle(0f, min);
        max = Mathf.DeltaAngle(0f, max);

        // min <= max の区間なら単純Clamp
        if (min <= max)
        {
            return Mathf.Clamp(angle, min, max);
        }
        else
        {
            // 0°を跨ぐ区間（例: 170〜-170）に対応するため、どちらに近いかで選ぶ
            float a = Mathf.Clamp(angle, min, 180f);
            float b = Mathf.Clamp(angle, -180f, max);
            return Mathf.Abs(Mathf.DeltaAngle(angle, a)) < Mathf.Abs(Mathf.DeltaAngle(angle, b)) ? a : b;
        }
    }

    // ============================================================
    // Option 側から感度をセット（外部向け）
    // ============================================================
    public void SetRotateSpeedFromOption(float v)
    {
        RotateSpeed = Mathf.Clamp(v, MinRotateSpeed, MaxRotateSpeed);
        RefreshSensitivityLabel();

        // ここでは Save() しない（OnDisableでまとめてSaveする設計）
        if (SaveSensitivity)
        {
            PlayerPrefs.SetFloat(SensitivityPrefsKey, RotateSpeed);
        }
    }

    // Slider用コールバック
    private void OnRotateSpeedChanged(float v)
    {
        SetRotateSpeedFromOption(v);
    }

    // 感度のラベル更新（任意UI）
    private void RefreshSensitivityLabel()
    {
        if (RotateSpeedLabel)
        {
            RotateSpeedLabel.text = $"Sensitivity : {RotateSpeed:0.00}";
        }
    }
}