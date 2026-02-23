using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/*
     このスクリプトがやること

     1) オプションメニューを「開く/閉じる」する
        ・入力：UI.Option（Esc / Tab を想定）
        ・MenuWindow の SetActive を切り替える

     2) メニューが開いている間は「ポーズ」する
        ・Time.timeScale = 0
        ・PauseTargets を enabled=false
        ・TPSCamera の操作を止める（ControlEnable=false）
        ・カーソルを表示＆ロック解除

     3) メニューが閉じたら「復帰」する
        ・Time.timeScale を元に戻す
        ・PauseTargets を enabled=true
        ・TPSCamera の操作を戻す（ControlEnable=true）
        ・ゲーム中シーンならカーソルロックに戻す

     4) メニュー内の設定項目を操作できる
        ・InvertX / InvertY：視点反転（TPSCameraへ反映）
        ・FullScreen：スクリーンモード（Screen.fullScreenへ反映）
        ・Sensitivity：感度（TPSCameraのRotateSpeedへ反映）
        ・GameEnd：ゲーム終了

     5) 操作方法が2系統ある
        ・マウス：
          - クリックで各トグルをON/OFF
          - 感度バーはクリック/ドラッグで変更
          - GameEnd はホバー色が変わる
        ・ゲームパッド/キーボード：
          - Move上下で項目選択（currentIndex）
          - Move左右で値変更
*/

public class Option : MonoBehaviour
{
    //================
    // InputSystem
    //================
    public InputSystem_Actions input;

    [Header("このシーンはインゲーム中として扱う？")]
    public bool IsGameplayScene = true;

    //================
    // Menu
    //================
    public bool MenuOn = false;                       // メニューが開いているか
    public GameObject MenuWindow;                     // 表示するメニュー本体

    //================
    // Pause
    //================
    private float _prevTimeScale = 1f;                // ポーズ前のTimeScaleを保存

    [Header("一時停止中は止めたいスクリプトたち")]
    public MonoBehaviour[] PauseTargets;              // ポーズ中に止めるコンポーネント

    //================
    // Option Values
    //================
    public bool InvertX = false;                      // 視点操作左右反転
    public bool InvertY = false;                      // 視点操作上下反転

    /*
         FullScreen の意味（UI上の意味）
         FullScreen == true  → UIの「ON」側が赤い（ウィンドウ表示したい）
         FullScreen == false → UIの「OFF」側が赤い（フルスクリーンにしたい）

         実際の Screen.fullScreen とは反転関係で扱っている
         ApplyScreenMode では Screen.fullScreen = !FullScreen を設定する
    */
    public bool FullScreen = true;

    //================
    // Camera Reference
    //================
    public TPSCamera CameraController;                // 設定を反映するカメラ

    //================
    // Sensitivity (0-1)
    //================
    private float _sensitivity01 = 0f;                // 0〜1で保持する感度値
    public float SensitivityStep = 0.05f;             // パッド操作での増減量

    //================
    // Sensitivity Bar UI
    //================
    [Header("感度バー関連（Xの範囲を数値で指定）")]
    public RectTransform SensitivityHandle;           // 感度バーのハンドル

    public float HandleMinX = -100f;                  // ハンドルの見た目上の左端
    public float HandleMaxX = 100f;                   // ハンドルの見た目上の右端
    public float HandleVisualOffsetX = 0f;            // マウス位置補正

    private RectTransform _handleParent;              // ハンドルの親（ローカル座標計算に使う）
    private float _handleBaseY;                       // ハンドルのYは固定（Xだけ動かす）
    private bool _isDraggingSensitivity = false;      // 感度バーをドラッグ中か

    [Header("Drag Settings")]
    public float SensitivityDragMaxDistanceY = 999f;  // 今は使っていないが拡張用に残している

    //================
    // Toggle Display Images (Red/White)
    //================
    [Header("Toggle Display Images")]
    public Image InvertX_OnImage;
    public Image InvertX_OffImage;
    public Image InvertY_OnImage;
    public Image InvertY_OffImage;
    public Image FullScreen_OnImage;
    public Image FullScreen_OffImage;

    public Image GameEndImage;
    public Color ActiveColor = Color.red;
    public Color InactiveColor = Color.white;

    //================
    // Click Areas
    //================
    [Header("Toggle Hit Rects (Click Areas)")]
    public RectTransform InvertX_OnHit;
    public RectTransform InvertX_OffHit;
    public RectTransform InvertY_OnHit;
    public RectTransform InvertY_OffHit;
    public RectTransform FullScreen_OnHit;
    public RectTransform FullScreen_OffHit;

    public RectTransform GameEndHit;
    private bool _gameEndHover = false;               // GameEndにマウスが乗っているか

    //================
    // ESC/Tab Hint Text
    //================
    [Header("ESCヒント用テキスト (TMP)")]
    public TMP_Text EscHintText;

    //================
    // Gamepad Navigation
    //================
    /*
         currentIndex の意味（パッド/キーボード操作）
         0 = 左右反転（InvertX）
         1 = 上下反転（InvertY）
         2 = スクリーンモード（FullScreen）
         3 = マウス感度（Sensitivity）
    */
    private int currentIndex = 0;

    // 連続入力を1回だけにするためのフラグ（押しっぱなし対策）
    private bool _didMoveRight, _didMoveLeft, _didMoveUp, _didMoveDown;

    //================
    // UI Camera
    //================
    private Canvas _canvas;
    private Camera _uiCamera;

    //================
    // Awake
    //================
    private void Awake()
    {
        input = new InputSystem_Actions();

        //================
        // Canvas / UI Camera を決める
        //================
        /*
             RectTransformUtility で ScreenPoint を判定する時に
             どのCameraを渡すかが必要になる
             ・Overlayなら camera=null
             ・それ以外なら canvas.worldCamera（なければ Camera.main）
        */
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null)
        {
            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _uiCamera = null;
            }
            else
            {
                _uiCamera = _canvas.worldCamera != null
                    ? _canvas.worldCamera
                    : Camera.main;
            }
        }
        else
        {
            _uiCamera = Camera.main;
        }

        //================
        // メニュー初期状態
        //================
        MenuOn = false;
        if (MenuWindow)
            MenuWindow.SetActive(false);

        //================
        // カメラ設定を読み込み
        //================
        SyncFromCamera();
        SyncSensitivityFromCamera();

        //================
        // 感度ハンドル初期化
        //================
        if (SensitivityHandle)
        {
            _handleParent = SensitivityHandle.parent as RectTransform;
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        UpdateSensitivityHandlePosition();

        //================
        // UI表示更新
        //================
        RefreshColors();
        RefreshGameEndColor();
        RefreshEscHint();

        //================
        // カーソル初期状態
        //================
        ApplyCursorStateInitial();
    }

    //================
    // OnEnable / OnDisable
    //================
    private void OnEnable()
    {
        if (input == null)
        {
            input = new InputSystem_Actions();
        }

        //================
        // WebGL の Escape 対策
        //================
        /*
             InputActions側で UI/Option に
             ・<Keyboard>/escape
             ・<Keyboard>/tab
             を両方バインドしている前提

             WebGLでは Escape が扱いづらい場合があるので
             Escape のバインドを無効にし Tab だけにする
        */
        var optionAction = input.UI.Option;

#if UNITY_WEBGL && !UNITY_EDITOR
        optionAction.Disable();

        for (int i = 0; i < optionAction.bindings.Count; i++)
        {
            if (optionAction.bindings[i].path == "<Keyboard>/escape")
            {
                optionAction.ApplyBindingOverride(i, "");
            }
        }

        optionAction.Enable();
#endif

        input.UI.Enable();
        input.Player.Enable();
    }

    private void OnDisable()
    {
        if (input != null)
        {
            input.UI.Disable();
            input.Player.Disable();
        }
    }

    //================
    // Update
    //================
    private void Update()
    {
        //================
        // メニュー開閉（押した瞬間だけ）
        //================
        if (input.UI.Option.WasPressedThisFrame())
        {
            ToggleMenu();
        }

        // メニューが閉じているなら、以降のUI操作はしない
        if (!MenuOn)
        {
            _isDraggingSensitivity = false;
            return;
        }

        //================
        // マウス操作
        //================
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();

            // GameEndのホバー色更新
            UpdateGameEndHover(mousePos);

            // クリック開始：トグル判定 / 感度ドラッグ開始判定
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryClickToggles(mousePos);
                TryBeginSensitivityDrag(mousePos);
            }

            // ドラッグ中：感度更新
            if (Mouse.current.leftButton.isPressed && _isDraggingSensitivity)
            {
                UpdateSensitivityByPointerDrag(mousePos);
            }

            // クリック終了：ドラッグ終了
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _isDraggingSensitivity = false;
            }
        }

        //================
        // ゲームパッド/キーボード操作
        //================
        Vector2 move = input.Player.Move.ReadValue<Vector2>();

        // 上：項目を上へ（-1相当）
        if (move.y > 0.5f)
        {
            if (!_didMoveUp)
            {
                currentIndex = (currentIndex + 3) % 4;
                _didMoveUp = true;
            }
        }
        else _didMoveUp = false;

        // 下：項目を下へ（+1相当）
        if (move.y < -0.5f)
        {
            if (!_didMoveDown)
            {
                currentIndex = (currentIndex + 1) % 4;
                _didMoveDown = true;
            }
        }
        else _didMoveDown = false;

        // 右：値を右方向へ
        if (move.x > 0.5f)
        {
            if (!_didMoveRight)
            {
                HandleRight();
                _didMoveRight = true;
            }
        }
        else _didMoveRight = false;

        // 左：値を左方向へ
        if (move.x < -0.5f)
        {
            if (!_didMoveLeft)
            {
                HandleLeft();
                _didMoveLeft = true;
            }
        }
        else _didMoveLeft = false;
    }

    //================
    // Cursor
    //================
    /*
         シーン開始時（またはメニューを閉じた時）に
         カーソル状態をゲーム側の仕様に合わせる
    */
    private void ApplyCursorStateInitial()
    {
        if (IsGameplayScene)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    //================
    // Menu Toggle
    //================
    /*
         メニューを開閉する

         ・MenuWindowの表示切替
         ・Pause状態切替（TimeScale / 対象スクリプト / カーソル）
         ・メニューを開いた瞬間はカメラから設定を読み直してUIを同期
    */
    private void ToggleMenu()
    {
        MenuOn = !MenuOn;
        if (MenuWindow)
            MenuWindow.SetActive(MenuOn);

        ApplyPauseState(MenuOn);

        // パッド入力の押しっぱなし判定をリセット
        _didMoveRight = _didMoveLeft = _didMoveUp = _didMoveDown = false;

        // 感度ドラッグも一旦切る
        _isDraggingSensitivity = false;

        if (MenuOn)
        {
            // 開いた瞬間：カメラ設定を読み直す
            SyncFromCamera();
            SyncSensitivityFromCamera();

            // 1フレーム後にUI更新（Layout更新の都合）
            StartCoroutine(RepositionNextFrame());

            RefreshColors();
            RefreshGameEndColor();
        }
        else
        {
            // 閉じた瞬間：ホバー解除
            _gameEndHover = false;
            RefreshGameEndColor();
        }

        RefreshEscHint();
    }

    //================
    // Pause / Resume
    //================
    /*
         pause=true  の時：
         ・TimeScaleを0にして停止
         ・カメラ操作を止める
         ・PauseTargetsを止める
         ・カーソルを表示する

         pause=false の時：
         ・TimeScaleを戻す
         ・カメラ操作を戻す
         ・PauseTargetsを戻す
         ・ゲーム中シーンならカーソルをロックに戻す
    */
    private void ApplyPauseState(bool pause)
    {
        if (pause)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (CameraController)
                CameraController.ControlEnable = false;

            if (PauseTargets != null)
            {
                foreach (var mb in PauseTargets)
                    if (mb) mb.enabled = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Time.timeScale = _prevTimeScale;

            if (CameraController)
                CameraController.ControlEnable = true;

            if (PauseTargets != null)
            {
                foreach (var mb in PauseTargets)
                    if (mb) mb.enabled = true;
            }

            if (IsGameplayScene)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }

    //================
    // Sync: Camera -> Option
    //================
    /*
         カメラの現在設定をオプション側へ反映する
         ・InvertX / InvertY を読み取る
         ・FullScreenは Screen.fullScreen の反転で持つ（UI仕様）
    */
    private void SyncFromCamera()
    {
        if (CameraController)
        {
            InvertX = CameraController.InvertX;
            InvertY = CameraController.InvertY;
        }

#if !UNITY_EDITOR
        FullScreen = !Screen.fullScreen;
#endif
    }

    //================
    // Sync: Option -> Camera
    //================
    /*
         オプションの InvertX / InvertY をカメラへ反映する
    */
    private void SyncToCamera()
    {
        if (!CameraController) return;

        CameraController.InvertX = InvertX;
        CameraController.InvertY = InvertY;
    }

    //================
    // Screen Mode Apply
    //================
    /*
         UI仕様の FullScreen を、実際の Screen.fullScreen に反映する
         Screen.fullScreen は FullScreen の反転でセットする
    */
    private void ApplyScreenMode()
    {
#if !UNITY_EDITOR
        Screen.fullScreen = !FullScreen;
#endif
    }

    //================
    // Quit Game
    //================
    /*
         ゲーム終了
         ・Editorなら再生停止
         ・Buildなら Application.Quit
    */
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    //================
    // GameEnd Hover
    //================
    /*
         GameEndボタンにマウスが乗ったか判定して色を変える
    */
    private void UpdateGameEndHover(Vector2 mousePos)
    {
        if (GameEndHit == null || GameEndImage == null)
        {
            _gameEndHover = false;
            return;
        }

        bool hit = RectTransformUtility.RectangleContainsScreenPoint(
            GameEndHit,
            mousePos,
            _uiCamera
        );

        if (hit != _gameEndHover)
        {
            _gameEndHover = hit;
            RefreshGameEndColor();
        }
    }

    private void RefreshGameEndColor()
    {
        if (!GameEndImage) return;
        GameEndImage.color = _gameEndHover ? ActiveColor : InactiveColor;
    }

    //================
    // ESC/Tab Hint Text
    //================
    /*
         WebGLはEscが無効化される想定なので表示もTabだけにする
    */
    private void RefreshEscHint()
    {
        if (!EscHintText) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        string keyLabel = "Tab";
#else
        string keyLabel = "Esc または Tab";
#endif

        EscHintText.text = MenuOn
            ? $"{keyLabel}でオプション閉じる"
            : $"{keyLabel}でオプション開く";
    }

    //================
    // Toggle Click
    //================
    /*
         マウスクリックで各項目を切り替える
         ・当たったHitRectに応じて bool を切り替える
         ・必要なら Camera / Screen に反映する
    */
    private void TryClickToggles(Vector2 mousePos)
    {
        bool Hit(RectTransform rt)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, _uiCamera);
        }

        if (Hit(InvertX_OnHit)) { InvertX = true; SyncToCamera(); RefreshColors(); return; }
        if (Hit(InvertX_OffHit)) { InvertX = false; SyncToCamera(); RefreshColors(); return; }

        if (Hit(InvertY_OnHit)) { InvertY = true; SyncToCamera(); RefreshColors(); return; }
        if (Hit(InvertY_OffHit)) { InvertY = false; SyncToCamera(); RefreshColors(); return; }

        if (Hit(FullScreen_OnHit)) { FullScreen = true; ApplyScreenMode(); RefreshColors(); return; }
        if (Hit(FullScreen_OffHit)) { FullScreen = false; ApplyScreenMode(); RefreshColors(); return; }

        if (Hit(GameEndHit)) { QuitGame(); return; }
    }

    //================
    // Sensitivity: Mouse -> LocalX
    //================
    /*
         Screen座標のマウス位置を、ハンドル親のローカル座標に変換する
         戻り値：
         ・true なら localX が有効
         ・false なら変換失敗（親が無い等）
    */
    private bool TryGetHandleLocalXFromMouse(Vector2 mousePos, out float localX)
    {
        localX = 0f;

        if (_handleParent == null)
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _handleParent,
            mousePos,
            _uiCamera,
            out Vector2 localPoint))
        {
            return false;
        }

        localX = localPoint.x;
        return true;
    }

    //================
    // Sensitivity: LocalX -> Sensitivity + Handle
    //================
    /*
         ローカルXから次を更新する
         ・_sensitivity01（0〜1）
         ・CameraController.RotateSpeed（Min〜MaxへLerp）
         ・SensitivityHandleの表示位置（Xだけ）
    */
    private void ApplySensitivityFromLocalX(float localX)
    {
        if (SensitivityHandle == null || HandleMaxX <= HandleMinX)
            return;

        // 見た目上のX（クリック位置＋補正）
        float visualX = localX + HandleVisualOffsetX;

        // 表示範囲に収める
        visualX = Mathf.Clamp(visualX, HandleMinX, HandleMaxX);

        // 0〜1に正規化して保存
        float t = Mathf.InverseLerp(HandleMinX, HandleMaxX, visualX);
        _sensitivity01 = Mathf.Clamp01(t);

        // カメラへ反映（回転速度）
        if (CameraController)
        {
            float newSpeed = Mathf.Lerp(
                CameraController.MinRotateSpeed,
                CameraController.MaxRotateSpeed,
                _sensitivity01
            );
            CameraController.SetRotateSpeedFromOption(newSpeed);
        }

        // ハンドル表示位置へ反映
        SensitivityHandle.anchoredPosition = new Vector2(
            visualX,
            _handleBaseY
        );
    }

    //================
    // Sensitivity: Drag Begin
    //================
    /*
         クリックした位置にハンドルをジャンプさせて、そのままドラッグ開始する
    */
    private void TryBeginSensitivityDrag(Vector2 mousePos)
    {
        if (TryGetHandleLocalXFromMouse(mousePos, out float localX))
        {
            ApplySensitivityFromLocalX(localX);
            _isDraggingSensitivity = true;
        }
        else
        {
            _isDraggingSensitivity = false;
        }
    }

    //================
    // Sensitivity: Drag Update
    //================
    /*
         ドラッグ中は毎フレームマウスXに追従させて感度を更新する
    */
    private void UpdateSensitivityByPointerDrag(Vector2 mousePos)
    {
        if (!_isDraggingSensitivity)
            return;

        if (TryGetHandleLocalXFromMouse(mousePos, out float localX))
        {
            ApplySensitivityFromLocalX(localX);
        }
    }

    //================
    // Sensitivity: Camera -> 0..1
    //================
    /*
         カメラの RotateSpeed を 0〜1 に変換して _sensitivity01 に入れる
         ・MinRotateSpeed〜MaxRotateSpeed の範囲で InverseLerp
    */
    private void SyncSensitivityFromCamera()
    {
        if (!CameraController)
        {
            _sensitivity01 = 0f;
            return;
        }

        float min = CameraController.MinRotateSpeed;
        float max = CameraController.MaxRotateSpeed;
        float cur = Mathf.Clamp(CameraController.RotateSpeed, min, max);

        if (max > min)
            _sensitivity01 = Mathf.InverseLerp(min, max, cur);
        else
            _sensitivity01 = 0f;

        _sensitivity01 = Mathf.Clamp01(_sensitivity01);
    }

    //================
    // Sensitivity: 0..1 -> Handle Position
    //================
    /*
         _sensitivity01 からハンドルXを決めて、表示位置を更新する
    */
    private void UpdateSensitivityHandlePosition()
    {
        if (!SensitivityHandle || HandleMaxX <= HandleMinX)
            return;

        float t = Mathf.Clamp01(_sensitivity01);

        float visualX = Mathf.Lerp(HandleMinX, HandleMaxX, t);

        SensitivityHandle.anchoredPosition = new Vector2(
            visualX,
            _handleBaseY
        );
    }

    //================
    // UI Layout Wait
    //================
    /*
         メニューを開いた瞬間はUIのRectが確定していないことがあるので
         1フレーム待ってからハンドル位置を更新する
    */
    private IEnumerator RepositionNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        UpdateSensitivityHandlePosition();
    }

    //================
    // Gamepad: Right / Left
    //================
    /*
         currentIndex に応じて右/左の操作内容を変える
         0/1/2 はトグル切替
         3 は感度を増減
    */
    private void HandleRight()
    {
        switch (currentIndex)
        {
            case 0: InvertX = true; SyncToCamera(); RefreshColors(); break;
            case 1: InvertY = true; SyncToCamera(); RefreshColors(); break;
            case 2: FullScreen = true; ApplyScreenMode(); RefreshColors(); break;
            case 3: ChangeSensitivity(+SensitivityStep); break;
        }
    }

    private void HandleLeft()
    {
        switch (currentIndex)
        {
            case 0: InvertX = false; SyncToCamera(); RefreshColors(); break;
            case 1: InvertY = false; SyncToCamera(); RefreshColors(); break;
            case 2: FullScreen = false; ApplyScreenMode(); RefreshColors(); break;
            case 3: ChangeSensitivity(-SensitivityStep); break;
        }
    }

    //================
    // Sensitivity: Step Change
    //================
    /*
         パッド操作で _sensitivity01 を step分だけ増減し
         カメラへ回転速度を反映して、ハンドル表示も更新する
    */
    private void ChangeSensitivity(float delta01)
    {
        if (!CameraController) return;

        _sensitivity01 = Mathf.Clamp01(_sensitivity01 + delta01);

        float newSpeed = Mathf.Lerp(
            CameraController.MinRotateSpeed,
            CameraController.MaxRotateSpeed,
            _sensitivity01
        );

        CameraController.SetRotateSpeedFromOption(newSpeed);
        UpdateSensitivityHandlePosition();
    }

    //================
    // UI: Colors
    //================
    /*
         各トグルのON/OFFに応じて色を更新する
         ・ActiveColor（赤）/ InactiveColor（白）
    */
    private void RefreshColors()
    {
        if (InvertX_OnImage)
            InvertX_OnImage.color = InvertX ? ActiveColor : InactiveColor;
        if (InvertX_OffImage)
            InvertX_OffImage.color = InvertX ? InactiveColor : ActiveColor;

        if (InvertY_OnImage)
            InvertY_OnImage.color = InvertY ? ActiveColor : InactiveColor;
        if (InvertY_OffImage)
            InvertY_OffImage.color = InvertY ? InactiveColor : ActiveColor;

        if (FullScreen_OnImage)
            FullScreen_OnImage.color = FullScreen ? ActiveColor : InactiveColor;
        if (FullScreen_OffImage)
            FullScreen_OffImage.color = FullScreen ? InactiveColor : ActiveColor;
    }
}