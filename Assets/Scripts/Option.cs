using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class Option : MonoBehaviour
{
    public InputSystem_Actions input;

    // ===== このシーンはインゲーム扱い？ =====
    [Header("このシーンはインゲーム中として扱う？")]
    public bool IsGameplayScene = true; // ★ここ重要

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
    public RectTransform SensitivityBarArea;       // バーの当たり判定用Rect
    public RectTransform SensitivityHandle;        // ポチ
    public RectTransform SensitivityLeftAnchor;    // 左端
    public RectTransform SensitivityRightAnchor;   // 右端

    private float _handleBaseY;
    private bool _isDraggingSensitivity = false;

    [Header("Drag Hit Settings")]
    public float SensitivityDragMaxDistanceY = 15f; // ※今は未使用（Y無視仕様）
    public float SensitivityDragExtraX = 10f;

    // ===== 表示画像（赤/白切り替えする画像）=====
    [Header("Toggle Display Images")]
    public Image InvertX_OnImage;        // On
    public Image InvertX_OffImage;       // off1
    public Image InvertY_OnImage;        // On 2
    public Image InvertY_OffImage;       // off2
    public Image FullScreen_OnImage;     // On3
    public Image FullScreen_OffImage;    // off3

    // 「ゲーム終了」のテキスト画像
    public Image GameEndImage;

    public Color ActiveColor = Color.red;
    public Color InactiveColor = Color.white;

    // ===== クリック当たり判定に使うRectTransform =====
    [Header("Toggle Hit Rects (Click Areas)")]
    public RectTransform InvertX_OnHit;
    public RectTransform InvertX_OffHit;
    public RectTransform InvertY_OnHit;
    public RectTransform InvertY_OffHit;
    public RectTransform FullScreen_OnHit;
    public RectTransform FullScreen_OffHit;

    // 「ゲーム終了」用のヒット領域
    public RectTransform GameEndHit;

    // hover状態
    private bool _gameEndHover = false;

    // ===== ESCヒント表示用 =====
    [Header("ESCヒント用テキスト (TMP)")]
    public TMP_Text EscHintText;

    // ===== フォーカス行（ゲームパッド/キーボード用）=====
    // 0 = 左右反転
    // 1 = 上下反転
    // 2 = スクリーンモード
    // 3 = マウス感度
    private int currentIndex = 0;

    private bool _didMoveRight, _didMoveLeft, _didMoveUp, _didMoveDown;

    // ===== UI用 Canvas / Camera =====
    private Canvas _canvas;
    private Camera _uiCamera;

    private void Awake()
    {
        input = new InputSystem_Actions();

        // ===== Canvas / UIカメラ取得 =====
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null)
        {
            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _uiCamera = null; // Overlay のときは null でOK
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

        // 最初は閉じておく
        MenuOn = false;
        if (MenuWindow)
        {
            MenuWindow.SetActive(false);
        }

        // カメラ・画面モードの状態を取り込む
        SyncFromCamera();

        // 感度をカメラから読み取る
        SyncSensitivityFromCamera();

        // ハンドルの基準Yを覚える
        if (SensitivityHandle)
        {
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        // ハンドル位置反映
        UpdateSensitivityHandlePosition();

        // 色更新
        RefreshColors();
        RefreshGameEndColor();

        // ESCヒント初期化
        RefreshEscHint();

        // 起動時のマウス表示状態
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
        // ===== メニュー開閉（ESC含む）=====
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

            // hover処理
            UpdateGameEndHover(mousePos);

            // 左クリック押した瞬間
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // ON/OFF / スクリーンモード / ゲーム終了
                TryClickToggles(mousePos);

                // 感度バー（クリック位置に即ジャンプ＋ドラッグ開始）
                TryBeginSensitivityDrag(mousePos);
            }

            // 感度ドラッグ中
            if (Mouse.current.leftButton.isPressed && _isDraggingSensitivity)
            {
                UpdateSensitivityByPointerDrag(mousePos);
            }

            // 離したらドラッグ終了
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _isDraggingSensitivity = false;
            }
        }

        // ===== ゲームパッド/キーボード入力 =====
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
        else
        {
            _didMoveUp = false;
        }

        // 下
        if (move.y < -0.5f)
        {
            if (!_didMoveDown)
            {
                currentIndex = (currentIndex + 1) % 4;
                _didMoveDown = true;
            }
        }
        else
        {
            _didMoveDown = false;
        }

        // 右
        if (move.x > 0.5f)
        {
            if (!_didMoveRight)
            {
                HandleRight();
                _didMoveRight = true;
            }
        }
        else
        {
            _didMoveRight = false;
        }

        // 左
        if (move.x < -0.5f)
        {
            if (!_didMoveLeft)
            {
                HandleLeft();
                _didMoveLeft = true;
            }
        }
        else
        {
            _didMoveLeft = false;
        }
    }

    // =============================================================================
    // 起動時にカーソル状態を整える
    // =============================================================================
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

    // =============================================================================
    // メニューのオン/オフ
    // =============================================================================
    private void ToggleMenu()
    {
        MenuOn = !MenuOn;

        if (MenuWindow)
            MenuWindow.SetActive(MenuOn);

        // ゲーム停止/再開
        ApplyPauseState(MenuOn);

        // メニュー開いた瞬間の初期化
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

        // ESCヒント更新
        RefreshEscHint();
    }

    // =============================================================================
    // ゲームの時間と操作を止める/戻す
    // =============================================================================
    private void ApplyPauseState(bool pause)
    {
        if (pause)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (CameraController)
            {
                CameraController.ControlEnable = false;
            }

            if (PauseTargets != null)
            {
                foreach (var mb in PauseTargets)
                {
                    if (mb) mb.enabled = false;
                }
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Time.timeScale = _prevTimeScale;

            if (CameraController)
            {
                CameraController.ControlEnable = true;
            }

            if (PauseTargets != null)
            {
                foreach (var mb in PauseTargets)
                {
                    if (mb) mb.enabled = true;
                }
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

    // =============================================================================
    // 今の状態をOption側にコピー（開いたとき）
    // =============================================================================
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

    // =============================================================================
    // Optionの反転状態をカメラに反映
    // =============================================================================
    private void SyncToCamera()
    {
        if (!CameraController) return;

        CameraController.InvertX = InvertX;
        CameraController.InvertY = InvertY;
    }

    // =============================================================================
    // Screen.fullScreen を切り替え（ビルド後のみ）
    // =============================================================================
    private void ApplyScreenMode()
    {
#if !UNITY_EDITOR
        Screen.fullScreen = !FullScreen;
#endif
    }

    // =============================================================================
    // ゲーム終了
    // =============================================================================
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =============================================================================
    // 「ゲーム終了」hover処理
    // =============================================================================
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

    // 「ゲーム終了」の色だけ更新
    private void RefreshGameEndColor()
    {
        if (!GameEndImage) return;

        GameEndImage.color = _gameEndHover ? ActiveColor : InactiveColor;
    }

    // =============================================================================
    // ESCヒントの文言を更新
    // =============================================================================
    private void RefreshEscHint()
    {
        if (!EscHintText) return;

        if (MenuOn)
        {
            EscHintText.text = "ESCでオプション閉じる";
        }
        else
        {
            EscHintText.text = "ESCでオプション開く";
        }
    }

    // =============================================================================
    // ON/OFFクリック / フルスクリーン / ゲーム終了クリック
    // =============================================================================>
    private void TryClickToggles(Vector2 mousePos)
    {
        bool Hit(RectTransform rt)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, _uiCamera);
        }

        // --- 視点操作左右反転
        if (Hit(InvertX_OnHit))
        {
            InvertX = true;
            SyncToCamera();
            RefreshColors();
            return;
        }
        if (Hit(InvertX_OffHit))
        {
            InvertX = false;
            SyncToCamera();
            RefreshColors();
            return;
        }

        // --- 視点操作上下反転
        if (Hit(InvertY_OnHit))
        {
            InvertY = true;
            SyncToCamera();
            RefreshColors();
            return;
        }
        if (Hit(InvertY_OffHit))
        {
            InvertY = false;
            SyncToCamera();
            RefreshColors();
            return;
        }

        // --- スクリーンモード
        if (Hit(FullScreen_OnHit))
        {
            FullScreen = true; // ウィンドウ表示
            ApplyScreenMode();
            RefreshColors();
            return;
        }
        if (Hit(FullScreen_OffHit))
        {
            FullScreen = false; // フルスクリーン
            ApplyScreenMode();
            RefreshColors();
            return;
        }

        // --- ゲーム終了
        if (Hit(GameEndHit))
        {
            QuitGame();
            return;
        }
    }

    // =============================================================================
    // X 座標（親ローカル）から感度とハンドル位置を更新する共通処理
    // =============================================================================
    private void ApplySensitivityFromLocalX(float localX)
    {
        if (!CameraController ||
            !SensitivityLeftAnchor ||
            !SensitivityRightAnchor)
        {
            return;
        }

        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;

        float t = 0.5f;
        if (Mathf.Abs(rightX - leftX) > Mathf.Epsilon)
        {
            t = Mathf.InverseLerp(leftX, rightX, localX);
        }

        _sensitivity01 = Mathf.Clamp01(t);

        float newSpeed = Mathf.Lerp(
            CameraController.MinRotateSpeed,
            CameraController.MaxRotateSpeed,
            _sensitivity01
        );

        CameraController.SetRotateSpeedFromOption(newSpeed);
        UpdateSensitivityHandlePosition();
    }

    // =============================================================================
    // 感度バーを掴む判定 ＋ クリック位置に即ジャンプ（Yは無視）
    // =============================================================================
    private void TryBeginSensitivityDrag(Vector2 mousePos)
    {
        if (!SensitivityHandle ||
            !SensitivityLeftAnchor ||
            !SensitivityRightAnchor)
        {
            _isDraggingSensitivity = false;
            return;
        }

        RectTransform parent = SensitivityHandle.parent as RectTransform;
        if (!parent)
        {
            _isDraggingSensitivity = false;
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent,
            mousePos,
            _uiCamera,
            out Vector2 localPoint))
        {
            _isDraggingSensitivity = false;
            return;
        }

        // クリックした瞬間に X に合わせる（Y 完全無視）
        ApplySensitivityFromLocalX(localPoint.x);

        // X 方向だけでクリック範囲チェック
        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;
        float minX = Mathf.Min(leftX, rightX) - SensitivityDragExtraX;
        float maxX = Mathf.Max(leftX, rightX) + SensitivityDragExtraX;

        _isDraggingSensitivity = (localPoint.x >= minX && localPoint.x <= maxX);
    }

    // =============================================================================
    // ドラッグ中：感度更新（Xのみ見る）
    // =============================================================================
    private void UpdateSensitivityByPointerDrag(Vector2 mousePos)
    {
        if (!SensitivityHandle ||
            !SensitivityLeftAnchor ||
            !SensitivityRightAnchor ||
            !CameraController)
        {
            return;
        }

        RectTransform parent = SensitivityHandle.parent as RectTransform;
        if (!parent) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent,
            mousePos,
            _uiCamera,
            out Vector2 localPoint))
        {
            return;
        }

        // X 座標から感度とハンドル位置を更新（Y 完全無視）
        ApplySensitivityFromLocalX(localPoint.x);
    }

    // =============================================================================
    // キー/パッド：右
    // =============================================================================
    private void HandleRight()
    {
        switch (currentIndex)
        {
            case 0: // 左右反転
                InvertX = true;
                SyncToCamera();
                RefreshColors();
                break;

            case 1: // 上下反転
                InvertY = true;
                SyncToCamera();
                RefreshColors();
                break;

            case 2: // スクリーンモード → 「ON」側（ウィンドウ表示）
                FullScreen = true;
                ApplyScreenMode();
                RefreshColors();
                break;

            case 3: // 感度アップ
                ChangeSensitivity(+SensitivityStep);
                break;
        }
    }

    // =============================================================================
    // キー/パッド：左
    // =============================================================================
    private void HandleLeft()
    {
        switch (currentIndex)
        {
            case 0:
                InvertX = false;
                SyncToCamera();
                RefreshColors();
                break;

            case 1:
                InvertY = false;
                SyncToCamera();
                RefreshColors();
                break;

            case 2: // スクリーンモード → 「OFF」側（フルスクリーン）
                FullScreen = false;
                ApplyScreenMode();
                RefreshColors();
                break;

            case 3:
                ChangeSensitivity(-SensitivityStep);
                break;
        }
    }

    // =============================================================================
    // 感度を+-する（キー/パッド用）
    // =============================================================================
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

    // カメラの現在RotateSpeedを読んで _sensitivity01 に変換
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
        {
            _sensitivity01 = Mathf.InverseLerp(min, max, cur);
        }
        else
        {
            _sensitivity01 = 0f;
        }

        _sensitivity01 = Mathf.Clamp01(_sensitivity01);
    }

    // ハンドルの見た目更新（Xだけ動かしてYは固定）
    private void UpdateSensitivityHandlePosition()
    {
        if (!SensitivityHandle ||
            !SensitivityLeftAnchor ||
            !SensitivityRightAnchor)
        {
            return;
        }

        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;
        float newX = Mathf.Lerp(leftX, rightX, _sensitivity01);

        SensitivityHandle.anchoredPosition = new Vector2(
            newX,
            _handleBaseY
        );
    }

    // レイアウト安定後に位置揃える
    private IEnumerator RepositionNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        UpdateSensitivityHandlePosition();
    }

    // 赤/白の切り替え（反転とスクリーンモード用）
    private void RefreshColors()
    {
        // 左右反転
        if (InvertX_OnImage)
            InvertX_OnImage.color = InvertX ? ActiveColor : InactiveColor;
        if (InvertX_OffImage)
            InvertX_OffImage.color = InvertX ? InactiveColor : ActiveColor;

        // 上下反転
        if (InvertY_OnImage)
            InvertY_OnImage.color = InvertY ? ActiveColor : InactiveColor;
        if (InvertY_OffImage)
            InvertY_OffImage.color = InvertY ? InactiveColor : ActiveColor;

        // スクリーンモード
        if (FullScreen_OnImage)
            FullScreen_OnImage.color = FullScreen ? ActiveColor : InactiveColor;
        if (FullScreen_OffImage)
            FullScreen_OffImage.color = FullScreen ? InactiveColor : ActiveColor;
    }
}
