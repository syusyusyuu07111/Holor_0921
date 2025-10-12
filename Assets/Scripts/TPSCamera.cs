using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
public class TPSCamera : MonoBehaviour
{
    InputSystem_Actions input;
    //カメラ入力設定==============================================================================--
    public float yaw = 90;//向いている角度
    public float RotateSpeed = 1.0f;
    public Transform Camera;
    public Transform Pivot;
    public float Distance = 3.0f;
    public Transform cam;
    [Header("キャラクター（プレイヤー）")]
    public Transform Player;
    public float AimSpeed = 5.0f;
    public float YawPlayer;
    public float prevplayerrow;//前フレームのプレイヤーの向いている角度
    public float Deadyaw = 0.5f;//無視する角度　少しの角度は回転に考慮しない
    [Range(0f, 1f)] float RowAmount = 1.0f;//キャラクターとカメラをどのくらい追従させるか

    //このカメラ制御機能のon off切り替え
    public bool ControlEnable = true;

    // ===== ここから追記（TPS向けの追加パラメータ）=====
    [Header("視点（縦回転/pitch）")]
    public float pitch = 0f;
    public Vector2 PitchClamp = new Vector2(-40f, 70f);

    // 縦揺れ対策（ターゲット＋平滑化＋デッドゾーン＋最大角速度）
    public float PitchSmoothTime = 0.06f;   // 縦の追従の滑らかさ（小さいほどキビキビ）
    public float MaxPitchSpeed = 360f;    // 縦回転の最大角速度（度/秒）
    public float MouseYDeadZone = 0.02f;   // 微小入力を無視（0〜0.05 くらい）
    float _pitchVel;                        // SmoothDamp用
    float _pitchTarget;                     // 目標角（生入力はここに反映）

    // 縦の可動域（真上/真下に行きすぎないための上限・下限）＋ 縦だけ感度控えめ
    public float PitchUpLimit = 35f;      // 上の上限（真上NG）
    public float PitchDownLimit = 10f;      // 下の上限（見下ろし控えめ）
    public float VerticalAmount = 0.5f;     // 縦だけ感度を抑える係数（0.2〜0.7推奨）

    [Header("感度（デバイス別）")]
    public float MouseSense = 1.0f;      // マウス用の係数
    public float GamepadSense = 3.0f;    // ゲームパッド用の係数
    public bool InvertY = false;         // 縦反転

    [Header("ショルダー/カメラ配置")]
    public Vector3 ShoulderOffset = new Vector3(0.4f, 0.0f, 0f); // 右肩
    public KeyCode ShoulderSwapKey = KeyCode.E;                   // 左右切替（任意）
    public KeyCode QuickTurnKey = KeyCode.Q;                      // クイックターン（任意）

    [Header("カメラ衝突処理")]
    public LayerMask CollisionMask = ~0;        // ぶつかり判定の対象
    public float CollisionBuffer = 0.05f;       // 壁から少し離す
    public float MinCameraDistance = 0.1f;      // 最短距離

    [Header("スムージング")]
    public float PositionSmoothTime = 0.08f;    // カメラ位置スムーズ
    Vector3 _camVel;                             // SmoothDamp用

    [Header("FOV")]
    public Camera UCam;
    public float FOVNormal = 60f;
    public float FOVAim = 50f;
    public float FOVLerp = 10f;
    public bool IsAiming = false;                // あなたのInputに合わせて切り替え

    // ===== ここから追記（UI表示用）=====
    [Header("UI")]
    public bool ShowBar = true;
    public Vector2 UIPos = new Vector2(20f, 40f);
    public Vector2 UISize = new Vector2(240f, 20f);
    public float MinRotateSpeed = 0.1f;
    public float MaxRotateSpeed = 10f;
    public int UIFontSize = 14;
    public float SliderHeight = 28f;   // バーの高さ
    public float ThumbWidth = 22f;     // つまみの幅
    public float ThumbHeight = 28f;    // つまみの高さ
    // ===== 追記ここまで =====

    public void Awake()
    {
        input = new InputSystem_Actions();
    }
    public void OnEnable()
    {
        input.Player.Enable();
    }
    public void OnDisable()
    {
        input?.Player.Disable();
    }
    void Start()
    {
        //カメラの回転の初期パラメータを取得しておく==========================================================
        if (cam == null) cam = transform; // ← null のときだけ代入に修正（コメントは既存のまま）
        yaw = cam.eulerAngles.y;
        prevplayerrow = Player.eulerAngles.y;

        // 初期FOV
        if (UCam == null)
        {
            UCam = GetComponentInChildren<Camera>();
        }
        if (UCam != null) UCam.fieldOfView = FOVNormal;

        // 縦揺れ対策の初期化
        _pitchTarget = pitch;

        // 上下制限の初期同期
        PitchClamp = new Vector2(-PitchDownLimit, PitchUpLimit);
    }
    void Update()
    {
        if (!ControlEnable || cam == null || Pivot == null) return;//カメラ制御をオフにする

        // 入力読み取り
        Vector2 LookInput = input.Player.Look.ReadValue<Vector2>();
        // デバイス別係数（パッド優先検出）
        bool usingGamepad = (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame);
        float deviceSense = usingGamepad ? GamepadSense : MouseSense;

        // クイックターン/ショルダー切替（任意：キーで制御）
        if (QuickTurnKey != KeyCode.None && Input.GetKeyDown(QuickTurnKey)) yaw += 180f;
        if (ShoulderSwapKey != KeyCode.None && Input.GetKeyDown(ShoulderSwapKey)) ShoulderOffset.x *= -1f;

        // 視点回転==========================================================================---
        // ※ マウスのLookは既にフレーム積分されているため deltaTime は掛けない
        float ly = InvertY ? -LookInput.y : LookInput.y;
        if (Mathf.Abs(ly) < MouseYDeadZone) ly = 0f; // 微小入力カット

        // 毎フレーム、上限/下限を同期（逆転しないようにクリップ）
        PitchUpLimit = Mathf.Clamp(PitchUpLimit, 0f, 80f);
        PitchDownLimit = Mathf.Clamp(PitchDownLimit, 0f, 80f);
        PitchClamp.x = -PitchDownLimit;
        PitchClamp.y = PitchUpLimit;

        yaw += LookInput.x * RotateSpeed * deviceSense;

        // 縦は抑えめ（VerticalAmount）で目標角に反映 → 目標をClamp
        _pitchTarget = Mathf.Clamp(
            _pitchTarget - ly * RotateSpeed * deviceSense * Mathf.Clamp01(VerticalAmount),
            PitchClamp.x, PitchClamp.y
        );
        // 実角はスムーズに追従（最大角速度で揺れ止め）
        pitch = Mathf.SmoothDamp(pitch, _pitchTarget, ref _pitchVel, PitchSmoothTime, MaxPitchSpeed);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);

        // 距離・衝突補正
        float d = Distance;
        Vector3 backDir = rot * Vector3.back; // rot * (0,0,-1)
        if (Physics.Raycast(Pivot.position, backDir, out RaycastHit hit, Distance, CollisionMask, QueryTriggerInteraction.Ignore))
        {
            d = Mathf.Max(MinCameraDistance, hit.distance - CollisionBuffer);
        }

        // 目標カメラ位置（ショルダーオフセット込み）
        Vector3 desiredPos = Pivot.transform.position + rot * new Vector3(0, 0, -d) + Pivot.TransformVector(ShoulderOffset);

        // 位置反映（スムージング）
        cam.position = Vector3.SmoothDamp(cam.position, desiredPos, ref _camVel, Mathf.Max(0f, PositionSmoothTime));
        cam.LookAt(Pivot.transform.position, Vector3.up);

        //カメラを回転させたらキャラも回転させる--------------------------------------------------
        if (Player != null)
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(Player.eulerAngles.y, cam.eulerAngles.y));
            if (diff > Mathf.Max(Deadyaw, 3f)) // 微小揺れを抑える
            {
                Quaternion target = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
                Player.rotation = Quaternion.Slerp(Player.rotation, target, AimSpeed * Time.deltaTime);
            }
        }

        // FOV（エイム時に絞る）----------------------------------------------------------------
        if (UCam != null)
        {
            float targetFov = IsAiming ? FOVAim : FOVNormal;
            UCam.fieldOfView = Mathf.Lerp(UCam.fieldOfView, targetFov, FOVLerp * Time.deltaTime);
        }
    }

    // ===== ここから追記（IMGUIでバー表示）=====
    void OnGUI()
    {
        if (!ShowBar) return;

        Rect r = new Rect(UIPos.x, UIPos.y, UISize.x, UISize.y);

        float labelH = UIFontSize + 6f;
        float pad = 4f;

        Rect labelRect = new Rect(r.x, r.y, r.width, labelH);
        Rect sliderRect = new Rect(r.x, r.y + labelH + pad, r.width, Mathf.Max(UISize.y, SliderHeight));

        if (_labelStyle == null || _labelStyle.fontSize != UIFontSize)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = UIFontSize;
        }
        GUI.Label(labelRect, $"Sensitivity : {RotateSpeed:0.00}", _labelStyle);

        // ===== ここから一時的にサイズを上書き =====
        float prevH = GUI.skin.horizontalSlider.fixedHeight;
        float prevTW = GUI.skin.horizontalSliderThumb.fixedWidth;
        float prevTH = GUI.skin.horizontalSliderThumb.fixedHeight;
        bool prevSW = GUI.skin.horizontalSlider.stretchWidth;
        bool prevTsw = GUI.skin.horizontalSliderThumb.stretchWidth;

        GUI.skin.horizontalSlider.fixedHeight = SliderHeight;
        GUI.skin.horizontalSlider.stretchWidth = true;   // 横は伸縮OK
        GUI.skin.horizontalSliderThumb.fixedWidth = ThumbWidth;
        GUI.skin.horizontalSliderThumb.fixedHeight = ThumbHeight;
        GUI.skin.horizontalSliderThumb.stretchWidth = false;  // 幅は固定
                                                              // ===== 上書きここまで =====

        // 感度スライダー
        float v = GUI.HorizontalSlider(sliderRect, RotateSpeed, MinRotateSpeed, MaxRotateSpeed);
        if (Mathf.Abs(v - RotateSpeed) > 0.0001f) RotateSpeed = v;

        // ===== 復元 =====
        GUI.skin.horizontalSlider.fixedHeight = prevH;
        GUI.skin.horizontalSlider.stretchWidth = prevSW;
        GUI.skin.horizontalSliderThumb.fixedWidth = prevTW;
        GUI.skin.horizontalSliderThumb.fixedHeight = prevTH;
        GUI.skin.horizontalSliderThumb.stretchWidth = prevTsw;

        // ---- ここから（上下可動域の調整UI・任意。実行中に上限/下限をいじれる）----
        float y = sliderRect.yMax + 10f;

        Rect upLbl = new Rect(r.x, y, 140f, 20f);
        Rect upSld = new Rect(r.x + 140f, y, r.width - 140f, 20f);
        GUI.Label(upLbl, "Pitch UpLimit", _labelStyle);
        PitchUpLimit = GUI.HorizontalSlider(upSld, PitchUpLimit, 0f, 80f);

        y += 24f;
        Rect dnLbl = new Rect(r.x, y, 140f, 20f);
        Rect dnSld = new Rect(r.x + 140f, y, r.width - 140f, 20f);
        GUI.Label(dnLbl, "Pitch DownLimit", _labelStyle);
        PitchDownLimit = GUI.HorizontalSlider(dnSld, PitchDownLimit, 0f, 80f);

        y += 24f;
        Rect vaLbl = new Rect(r.x, y, 140f, 20f);
        Rect vaSld = new Rect(r.x + 140f, y, r.width - 140f, 20f);
        GUI.Label(vaLbl, "Vertical Amount", _labelStyle);
        VerticalAmount = GUI.HorizontalSlider(vaSld, VerticalAmount, 0.1f, 1.0f);
        // ---- 調整UIここまで ----
    }
    GUIStyle _labelStyle; // 既にあるなら不要



    // ===== 追記ここまで =====
}
