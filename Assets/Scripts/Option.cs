using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class Option : MonoBehaviour
{
    public InputSystem_Actions input;

    [Header("このシーンはインゲーム中として扱う？")]
    public bool IsGameplayScene = true;

    // ===== メニュー本体 =====
    public bool MenuOn = false;
    public GameObject MenuWindow;

    // --- ポーズ管理 ---
    private float _prevTimeScale = 1f;

    [Header("一時停止中は止めたいスクリプトたち")]
    public MonoBehaviour[] PauseTargets;

    // ===== 設定状態 =====
    public bool InvertX = false;   // 視点操作左右反転
    public bool InvertY = false;   // 視点操作上下反転

    // FullScreen == true  → UIの「ON」側が赤い（ウィンドウ表示したい）
    // FullScreen == false → UIの「OFF」側が赤い（フルスクリーンにしたい）
    public bool FullScreen = true;

    // ===== カメラ参照 =====
    public TPSCamera CameraController;

    // ===== 感度（0〜1）=====
    private float _sensitivity01 = 0f;
    public float SensitivityStep = 0.05f;

    // ===== 感度バー関連 =====
    [Header("感度バー関連（Xの範囲を数値で指定）")]
    public RectTransform SensitivityHandle;    // ハンドル

    // ハンドルの「見た目上」のX範囲（親の anchoredPosition.x）
    public float HandleMinX = -100f;
    public float HandleMaxX = 100f;

    // マウス位置からハンドルを置くときの補正
    // （「少し右にずれる」を打ち消す値。右にずれるならマイナス値）
    public float HandleVisualOffsetX = 0f;

    private RectTransform _handleParent;
    private float _handleBaseY;
    private bool _isDraggingSensitivity = false;

    [Header("Drag Settings")]
    public float SensitivityDragMaxDistanceY = 999f;

    // ===== 表示画像（赤/白切り替えする画像）=====
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

    [Header("Toggle Hit Rects (Click Areas)")]
    public RectTransform InvertX_OnHit;
    public RectTransform InvertX_OffHit;
    public RectTransform InvertY_OnHit;
    public RectTransform InvertY_OffHit;
    public RectTransform FullScreen_OnHit;
    public RectTransform FullScreen_OffHit;

    public RectTransform GameEndHit;
    private bool _gameEndHover = false;

    [Header("ESCヒント用テキスト (TMP)")]
    public TMP_Text EscHintText;

    // 0 = 左右反転 / 1 = 上下反転 / 2 = スクリーンモード / 3 = マウス感度
    private int currentIndex = 0;
    private bool _didMoveRight, _didMoveLeft, _didMoveUp, _didMoveDown;

    private Canvas _canvas;
    private Camera _uiCamera;

    private void Awake()
    {
        input = new InputSystem_Actions();

        // Canvas / UIカメラ
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

        MenuOn = false;
        if (MenuWindow)
            MenuWindow.SetActive(false);

        SyncFromCamera();
        SyncSensitivityFromCamera();

        // ハンドル親＆基準Y取得
        if (SensitivityHandle)
        {
            _handleParent = SensitivityHandle.parent as RectTransform;
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        UpdateSensitivityHandlePosition();

        RefreshColors();
        RefreshGameEndColor();
        RefreshEscHint();
        ApplyCursorStateInitial();
    }

    private void OnEnable()
    {
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

    private void Update()
    {
        // メニュー開閉
        if (input.UI.Option.WasPressedThisFrame())
        {
            ToggleMenu();
        }

        if (!MenuOn)
        {
            _isDraggingSensitivity = false;
            return;
        }

        // ===== マウス入力 =====
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();

            UpdateGameEndHover(mousePos);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryClickToggles(mousePos);
                TryBeginSensitivityDrag(mousePos);
            }

            if (Mouse.current.leftButton.isPressed && _isDraggingSensitivity)
            {
                UpdateSensitivityByPointerDrag(mousePos);
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _isDraggingSensitivity = false;
            }
        }

        // ===== ゲームパッド/キーボード =====
        Vector2 move = input.Player.Move.ReadValue<Vector2>();

        // 上
        if (move.y > 0.5f)
        {
            if (!_didMoveUp)
            {
                currentIndex = (currentIndex + 3) % 4;
                _didMoveUp = true;
            }
        }
        else _didMoveUp = false;

        // 下
        if (move.y < -0.5f)
        {
            if (!_didMoveDown)
            {
                currentIndex = (currentIndex + 1) % 4;
                _didMoveDown = true;
            }
        }
        else _didMoveDown = false;

        // 右
        if (move.x > 0.5f)
        {
            if (!_didMoveRight)
            {
                HandleRight();
                _didMoveRight = true;
            }
        }
        else _didMoveRight = false;

        // 左
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

    // カーソル初期状態
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

    // メニューオンオフ
    private void ToggleMenu()
    {
        MenuOn = !MenuOn;
        if (MenuWindow)
            MenuWindow.SetActive(MenuOn);

        ApplyPauseState(MenuOn);

        _didMoveRight = _didMoveLeft = _didMoveUp = _didMoveDown = false;
        _isDraggingSensitivity = false;

        if (MenuOn)
        {
            SyncFromCamera();
            SyncSensitivityFromCamera();
            StartCoroutine(RepositionNextFrame());
            RefreshColors();
            RefreshGameEndColor();
        }
        else
        {
            _gameEndHover = false;
            RefreshGameEndColor();
        }

        RefreshEscHint();
    }

    // ポーズ
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

    // カメラ→オプション
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

    // オプション→カメラ
    private void SyncToCamera()
    {
        if (!CameraController) return;

        CameraController.InvertX = InvertX;
        CameraController.InvertY = InvertY;
    }

    private void ApplyScreenMode()
    {
#if !UNITY_EDITOR
        Screen.fullScreen = !FullScreen;
#endif
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // GameEnd hover
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

    private void RefreshEscHint()
    {
        if (!EscHintText) return;
        EscHintText.text = MenuOn ? "ESCでオプション閉じる" : "ESCでオプション開く";
    }

    // トグルクリック
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

    // =========================================================================
    // マウス座標 → ハンドル親のローカルX に変換
    // =========================================================================
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

    // =========================================================================
    // ローカルXから感度＆ハンドル位置を更新
    // =========================================================================
    private void ApplySensitivityFromLocalX(float localX)
    {
        if (SensitivityHandle == null || HandleMaxX <= HandleMinX)
            return;

        // マウス位置にオフセットを足した「見た目のX」
        float visualX = localX + HandleVisualOffsetX;

        // 見た目のXを範囲内にクランプ（←ここがポイント）
        visualX = Mathf.Clamp(visualX, HandleMinX, HandleMaxX);

        // その見た目の位置を使って 0〜1 に正規化
        float t = Mathf.InverseLerp(HandleMinX, HandleMaxX, visualX);
        _sensitivity01 = Mathf.Clamp01(t);

        // カメラ回転速度に反映
        if (CameraController)
        {
            float newSpeed = Mathf.Lerp(
                CameraController.MinRotateSpeed,
                CameraController.MaxRotateSpeed,
                _sensitivity01
            );
            CameraController.SetRotateSpeedFromOption(newSpeed);
        }

        // ハンドルを見た目のXにセット
        SensitivityHandle.anchoredPosition = new Vector2(
            visualX,
            _handleBaseY
        );
    }

    // クリック開始：その位置にジャンプ＋ドラッグ開始
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

    // ドラッグ中：毎フレームマウスXに追従
    private void UpdateSensitivityByPointerDrag(Vector2 mousePos)
    {
        if (!_isDraggingSensitivity)
            return;

        if (TryGetHandleLocalXFromMouse(mousePos, out float localX))
        {
            ApplySensitivityFromLocalX(localX);
        }
    }

    // カメラのRotateSpeed→0〜1に変換
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

    // 0〜1の感度値からハンドル位置を決定
    private void UpdateSensitivityHandlePosition()
    {
        if (!SensitivityHandle || HandleMaxX <= HandleMinX)
            return;

        float t = Mathf.Clamp01(_sensitivity01);

        // 見た目のX（ここでは offset は使わず、純粋にバーの両端を補間）
        float visualX = Mathf.Lerp(HandleMinX, HandleMaxX, t);

        SensitivityHandle.anchoredPosition = new Vector2(
            visualX,
            _handleBaseY
        );
    }

    private IEnumerator RepositionNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        UpdateSensitivityHandlePosition();
    }

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
