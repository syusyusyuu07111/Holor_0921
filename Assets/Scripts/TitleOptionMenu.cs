using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TitleOptionMenu : MonoBehaviour
{
    [Header("メニュー本体")]
    public GameObject MenuWindow;
    public bool MenuOn = false;

    // ===== 設定状態（UI表示用）=====
    [Header("設定値（画面上の状態）")]
    public bool InvertX = false;
    public bool InvertY = false;

    // true  = ウィンドウ表示したい（UI ON が赤）
    // false = フルスクリーンにしたい（UI OFF が赤）
    public bool FullScreen = true;

    // 0〜1 感度
    private float _sensitivity01 = 0.5f;
    public float SensitivityStep = 0.05f;

    // ===== 感度バー関連 =====
    [Header("感度バー")]
    public RectTransform SensitivityBarArea;       // バーの当たり判定用Rect（なくても可）
    public RectTransform SensitivityHandle;        // ポチ
    public RectTransform SensitivityLeftAnchor;    // 左端
    public RectTransform SensitivityRightAnchor;   // 右端

    private float _handleBaseY;
    private bool _isDraggingSensitivity = false;

    [Header("Drag Hit Settings")]
    public float SensitivityDragMaxDistanceY = 15f;
    public float SensitivityDragExtraX = 10f;

    // ===== 表示画像（赤/白切り替えする画像）=====
    [Header("Toggle Display Images")]
    public Image InvertX_OnImage;        // On
    public Image InvertX_OffImage;       // Off
    public Image InvertY_OnImage;        // On
    public Image InvertY_OffImage;       // Off
    public Image FullScreen_OnImage;     // ON（ウィンドウ）
    public Image FullScreen_OffImage;    // OFF（フルスク）

    [Header("ゲーム終了（任意）")]
    public RectTransform GameEndHit;
    public Image GameEndImage;

    public Color ActiveColor = Color.red;
    public Color InactiveColor = Color.white;

    private bool _gameEndHover = false;

    [Header("Toggle Hit Rects (Click Areas)")]
    public RectTransform InvertX_OnHit;
    public RectTransform InvertX_OffHit;
    public RectTransform InvertY_OnHit;
    public RectTransform InvertY_OffHit;
    public RectTransform FullScreen_OnHit;
    public RectTransform FullScreen_OffHit;

    [Header("Close Button Hit (Click Area)")]
    public RectTransform CloseHit;   // ★タイトルオプションを閉じるボタン用

    private void Awake()
    {
        // 最初は閉じておく
        MenuOn = false;
        if (MenuWindow) MenuWindow.SetActive(false);

        // 設定読み込み
        GameSettings.Load();
        SyncFromSettings();

        // ハンドルの基準Y
        if (SensitivityHandle)
        {
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        UpdateSensitivityHandlePosition();
        RefreshColors();
        RefreshGameEndColor();
    }

    private void Update()
    {
        if (!MenuOn) return;
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        // hover処理
        UpdateGameEndHover(mousePos);

        // クリック開始
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryClickToggles(mousePos);
            TryBeginSensitivityDrag(mousePos);
        }

        // ドラッグ中
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

    // =============================================================================
    // 外部から呼ぶ：開く / 閉じる / トグル
    // =============================================================================
    public void OpenMenu()
    {
        if (MenuOn) return;
        MenuOn = true;
        if (MenuWindow) MenuWindow.SetActive(true);

        // 最新設定を反映してから表示
        GameSettings.Load();
        SyncFromSettings();
        StartCoroutine(RepositionNextFrame());
        RefreshColors();
        RefreshGameEndColor();
    }

    public void CloseMenu()
    {
        if (!MenuOn) return;
        MenuOn = false;
        if (MenuWindow) MenuWindow.SetActive(false);

        _isDraggingSensitivity = false;
        _gameEndHover = false;
        RefreshGameEndColor();
    }

    public void ToggleMenu()
    {
        if (MenuOn) CloseMenu();
        else OpenMenu();
    }

    // =============================================================================
    // GameSettings → UI用フィールドへ反映
    // =============================================================================
    private void SyncFromSettings()
    {
        InvertX = GameSettings.InvertX;
        InvertY = GameSettings.InvertY;
        FullScreen = GameSettings.FullScreenOption;
        _sensitivity01 = GameSettings.Sensitivity01;
    }

    // =============================================================================
    // ON/OFFクリック / フルスクリーン / 閉じる / ゲーム終了クリック
    // =============================================================================
    private void TryClickToggles(Vector2 mousePos)
    {
        bool Hit(RectTransform rt)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null);
        }

        // --- 視点操作左右反転
        if (Hit(InvertX_OnHit))
        {
            InvertX = true;
            GameSettings.SetInvertX(InvertX);
            GameSettings.Save();
            RefreshColors();
            return;
        }
        if (Hit(InvertX_OffHit))
        {
            InvertX = false;
            GameSettings.SetInvertX(InvertX);
            GameSettings.Save();
            RefreshColors();
            return;
        }

        // --- 視点操作上下反転
        if (Hit(InvertY_OnHit))
        {
            InvertY = true;
            GameSettings.SetInvertY(InvertY);
            GameSettings.Save();
            RefreshColors();
            return;
        }
        if (Hit(InvertY_OffHit))
        {
            InvertY = false;
            GameSettings.SetInvertY(InvertY);
            GameSettings.Save();
            RefreshColors();
            return;
        }

        // --- スクリーンモード
        if (Hit(FullScreen_OnHit))
        {
            // ON → ウィンドウ表示したい
            FullScreen = true;
            GameSettings.SetFullScreenOption(FullScreen);
            GameSettings.Save();
#if !UNITY_EDITOR
            Screen.fullScreen = !FullScreen;
#endif
            RefreshColors();
            return;
        }
        if (Hit(FullScreen_OffHit))
        {
            // OFF → フルスクリーンにしたい
            FullScreen = false;
            GameSettings.SetFullScreenOption(FullScreen);
            GameSettings.Save();
#if !UNITY_EDITOR
            Screen.fullScreen = !FullScreen;
#endif
            RefreshColors();
            return;
        }

        // --- 閉じるボタン
        if (Hit(CloseHit))
        {
            CloseMenu();
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

        bool hit = RectTransformUtility.RectangleContainsScreenPoint(GameEndHit, mousePos, null);

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
    // 感度バーまわり
    // =============================================================================

    // 感度バーを掴む判定
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
            null,
            out Vector2 localPoint))
        {
            _isDraggingSensitivity = false;
            return;
        }

        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;
        float minX = Mathf.Min(leftX, rightX) - SensitivityDragExtraX;
        float maxX = Mathf.Max(leftX, rightX) + SensitivityDragExtraX;

        float barY = GetBarCenterYInParent(parent);
        float diffY = Mathf.Abs(localPoint.y - barY);

        bool withinX = (localPoint.x >= minX && localPoint.x <= maxX);
        bool withinY = (diffY <= SensitivityDragMaxDistanceY);

        _isDraggingSensitivity = (withinX && withinY);
    }

    // ドラッグ中：感度更新
    private void UpdateSensitivityByPointerDrag(Vector2 mousePos)
    {
        if (!SensitivityHandle ||
            !SensitivityLeftAnchor ||
            !SensitivityRightAnchor)
        {
            return;
        }

        RectTransform parent = SensitivityHandle.parent as RectTransform;
        if (!parent) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent,
            mousePos,
            null,
            out Vector2 localPoint))
        {
            return;
        }

        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;

        float t = 0.5f;
        if (Mathf.Abs(rightX - leftX) > Mathf.Epsilon)
        {
            t = Mathf.InverseLerp(leftX, rightX, localPoint.x);
        }
        _sensitivity01 = Mathf.Clamp01(t);

        GameSettings.SetSensitivity01(_sensitivity01);
        GameSettings.Save();

        UpdateSensitivityHandlePosition();
    }

    // 親ローカル座標でバーの中心Yを取る
    private float GetBarCenterYInParent(RectTransform parent)
    {
        if (parent == null || SensitivityBarArea == null)
        {
            return _handleBaseY;
        }

        Vector3 barWorldPos = SensitivityBarArea.TransformPoint(SensitivityBarArea.rect.center);
        Vector3 barLocalInParent = parent.InverseTransformPoint(barWorldPos);
        return barLocalInParent.y;
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
