using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Option : MonoBehaviour
{
    public InputSystem_Actions input;

    // ===== メニュー本体 =====
    public bool MenuOn = false;
    public GameObject MenuWindow;

    // ===== 設定状態（UI表示用）=====
    public bool InvertX = false;   // 視点操作左右反転
    public bool InvertY = false;   // 視点操作上下反転
    public bool FullScreen = true; // スクリーンモード (true=ON側)

    // ===== カメラと感度 =====
    public TPSCamera CameraController;   // TPSCamera を割り当て
    public float SensitivityStep = 0.05f; // ← / → 入力でどれくらい動かすか（0〜1スケール）

    // 0〜1で保持する現在の感度（Min〜Maxを正規化した値）
    private float _sensitivity01 = 0f;

    // ハンドルとアンカー
    // Panelの子として
    //   SensitivityHandle      ← 白いポチ
    //   SensitivityLeftAnchor  ← バーの左端
    //   SensitivityRightAnchor ← バーの右端
    public RectTransform SensitivityHandle;
    public RectTransform SensitivityLeftAnchor;
    public RectTransform SensitivityRightAnchor;

    // ハンドルのY座標は固定したいので最初に記録する
    private float _handleBaseY;

    // ===== ON / OFF の画像（Image.colorで赤白切り替え）=====
    public Image InvertX_OnImage;
    public Image InvertX_OffImage;
    public Image InvertY_OnImage;
    public Image InvertY_OffImage;
    public Image FullScreen_OnImage;
    public Image FullScreen_OffImage;

    public Color ActiveColor = Color.red;
    public Color InactiveColor = Color.white;

    // ===== どの行をいじっているか =====
    // 0 = 左右反転
    // 1 = 上下反転
    // 2 = スクリーンモード
    // 3 = マウス感度
    private int currentIndex = 0;

    // ===== 入力の押しっぱなし対策 =====
    private bool _didMoveRight, _didMoveLeft, _didMoveUp, _didMoveDown;

    private void Awake()
    {
        input = new InputSystem_Actions();

        // メニューは開始時は閉じておく
        MenuOn = false;
        if (MenuWindow != null)
        {
            MenuWindow.SetActive(false);
        }

        // ON/OFFの色を今のboolに合わせる
        RefreshColors();

        // カメラの現在の感度を読み取って、_sensitivity01 に変換
        SyncSensitivityFromCamera();

        // ハンドルの基準Y（今の高さ）を覚えておく
        if (SensitivityHandle != null)
        {
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        // ハンドル位置を初期反映
        UpdateSensitivityHandlePosition();
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
        // =====================
        // メニューの開閉
        // =====================
        if (input.UI.Option.WasPressedThisFrame())
        {
            MenuOn = !MenuOn;
            if (MenuWindow != null)
            {
                MenuWindow.SetActive(MenuOn);
            }

            // スティック押しっぱなしフラグをリセット
            _didMoveRight = _didMoveLeft = _didMoveUp = _didMoveDown = false;

            if (MenuOn)
            {
                // メニューを開いた瞬間に最新の感度と位置を同期
                SyncSensitivityFromCamera();
                StartCoroutine(RepositionNextFrame());
            }
        }

        // メニュー閉じてるときはここで終わり
        if (!MenuOn)
        {
            return;
        }

        // =====================
        // マウスドラッグで感度バーを動かす
        // =====================
        // 条件: メニュー開いてる ＋ マウス左ボタン押されてる
        //      (長押し中はずっと追従するイメージ)
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            UpdateSensitivityByPointerDrag();
        }

        // =====================
        // パッド / キーボードのスティック入力での操作
        // =====================
        Vector2 move = input.Player.Move.ReadValue<Vector2>();

        // ---- 上入力：行を上に移動 ----
        if (move.y > 0.5f)
        {
            if (!_didMoveUp)
            {
                currentIndex = (currentIndex + 3) % 4; // (currentIndex - 1 + 4) % 4
                _didMoveUp = true;
            }
        }
        else
        {
            _didMoveUp = false;
        }

        // ---- 下入力：行を下に移動 ----
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

        // ---- 右入力（スティック・キー）----
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

        // ---- 左入力（スティック・キー）----
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

    // ==========================
    // → 入力のときの処理
    // ==========================
    private void HandleRight()
    {
        switch (currentIndex)
        {
            case 0: // 視点操作左右反転をON側に寄せる
                InvertX = true;
                RefreshColors();
                break;

            case 1: // 視点操作上下反転をON側に寄せる
                InvertY = true;
                RefreshColors();
                break;

            case 2: // スクリーンモードをON側
                FullScreen = true;
                RefreshColors();
                break;

            case 3: // マウス感度UP
                ChangeSensitivity(+SensitivityStep);
                break;
        }
    }

    // ==========================
    // ← 入力のときの処理
    // ==========================
    private void HandleLeft()
    {
        switch (currentIndex)
        {
            case 0:
                InvertX = false;
                RefreshColors();
                break;

            case 1:
                InvertY = false;
                RefreshColors();
                break;

            case 2:
                FullScreen = false;
                RefreshColors();
                break;

            case 3:
                ChangeSensitivity(-SensitivityStep);
                break;
        }
    }

    // ==========================
    // 感度を増減する（←→入力用）
    // _sensitivity01 (0〜1) を更新して
    // → 実スピードに変換
    // → カメラに渡す
    // → ハンドルXを更新
    // ==========================
    private void ChangeSensitivity(float delta01)
    {
        if (CameraController == null) return;

        _sensitivity01 = Mathf.Clamp01(_sensitivity01 + delta01);

        float newSpeed = Mathf.Lerp(
            CameraController.MinRotateSpeed,
            CameraController.MaxRotateSpeed,
            _sensitivity01
        );

        CameraController.SetRotateSpeedFromOption(newSpeed);

        UpdateSensitivityHandlePosition();
    }

    // ==========================
    // マウスでドラッグ中の位置から感度を更新する
    // 左クリック長押ししたままマウスを左右に動かすとバーの上をスライドする
    // ==========================
    private void UpdateSensitivityByPointerDrag()
    {
        // 必要なオブジェクト無いなら何もしない
        if (SensitivityHandle == null ||
            SensitivityLeftAnchor == null ||
            SensitivityRightAnchor == null ||
            CameraController == null)
        {
            return;
        }

        // 親RectTransformを取得
        RectTransform parent = SensitivityHandle.parent as RectTransform;
        if (parent == null) return;

        // マウスのスクリーン座標を、親のローカル座標に変換
        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent,
            mouseScreenPos,
            null, // Screen Space Overlay CanvasならnullでOK
            out localPoint))
        {
            return;
        }

        // アンカーのXを取得（親基準のanchoredPosition）
        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;

        // localPoint.x が左端〜右端のどこにいるかを0〜1に正規化
        float t = 0.5f;
        if (Mathf.Abs(rightX - leftX) > Mathf.Epsilon)
        {
            t = Mathf.InverseLerp(leftX, rightX, localPoint.x);
        }
        t = Mathf.Clamp01(t);

        // 0〜1の感度に反映
        _sensitivity01 = t;

        // 実感度（RotateSpeed）に変換してカメラへ渡す
        float newSpeed = Mathf.Lerp(
            CameraController.MinRotateSpeed,
            CameraController.MaxRotateSpeed,
            _sensitivity01
        );
        CameraController.SetRotateSpeedFromOption(newSpeed);

        // ハンドルの見た目を更新
        UpdateSensitivityHandlePosition();
    }

    // ==========================
    // カメラの現在のRotateSpeedから
    // _sensitivity01を逆算して同期する
    // ==========================
    private void SyncSensitivityFromCamera()
    {
        if (CameraController == null)
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

    // ==========================
    // ハンドル（白いポチ）をバー上で動かす
    // Xだけ補間して動かす
    // Yは初期値(_handleBaseY)のまま固定
    // ==========================
    private void UpdateSensitivityHandlePosition()
    {
        if (SensitivityHandle == null ||
            SensitivityLeftAnchor == null ||
            SensitivityRightAnchor == null)
        {
            return;
        }

        // アンカーのX（anchoredPosition.x）を線形補間
        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;
        float newX = Mathf.Lerp(leftX, rightX, _sensitivity01);

        // Yは固定
        SensitivityHandle.anchoredPosition = new Vector2(
            newX,
            _handleBaseY
        );
    }

    // ==========================
    // レイアウトが落ち着いた1フレーム後に
    // ハンドル位置を合わせ直す
    // （メニュー開いた瞬間のズレ対策）
    // ==========================
    private IEnumerator RepositionNextFrame()
    {
        yield return null; // 1フレーム待つ
        Canvas.ForceUpdateCanvases();
        UpdateSensitivityHandlePosition();
    }

    // ==========================
    // ON/OFFの文字画像の色を更新する
    // ==========================
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
