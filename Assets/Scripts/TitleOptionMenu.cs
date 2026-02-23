using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TitleOptionMenu : MonoBehaviour
{
    /*
        ============================================================
        TitleOptionMenu がやっていること
        ============================================================

        ■目的
        タイトル画面の「オプションメニュー」を開閉し、
        ・反転設定（InvertX / InvertY）
        ・画面モード（FullScreen：UI上は“ウィンドウ/フルスク”切替のため反転表現）
        ・感度（0〜1：スライダーをドラッグして変更）
        ・ゲーム終了
        をマウス操作で行えるようにする。

        ■データの保存先
        変更した値は GameSettings に Set〜 して Save() する。
        （= メニューを閉じても設定が保持される）

        ■UIの仕組み
        ・ON/OFF のクリック判定は RectTransform（Hit）で行う
        ・表示色は Image を赤/白で塗り替えて状態が分かるようにする
        ・感度は “ハンドル(ポチ)” を左右に動かすことで 0〜1 を表現する
          - LeftAnchor / RightAnchor の anchoredPosition.x の間を lerp する

        ■ドラッグ判定
        ・バーの中心Y付近をクリックしたらドラッグ開始
        ・ドラッグ中はマウスX位置→0〜1に変換して感度更新

        ============================================================
    */

    // ================================
    // メニュー本体
    // ================================
    [Header("メニュー本体")]
    public GameObject MenuWindow;      // 表示/非表示するウィンドウ
    public bool MenuOn = false;        // 開いているかどうか

    // ================================
    // 設定状態（UI表示用）
    // ================================
    [Header("設定値（画面上の状態）")]
    public bool InvertX = false;
    public bool InvertY = false;

    // true  = ウィンドウ表示したい（UI ON が赤）
    // false = フルスクリーンにしたい（UI OFF が赤）
    public bool FullScreen = true;

    // 感度（0〜1）
    private float _sensitivity01 = 0.5f;
    public float SensitivityStep = 0.05f; // ※現状このスクリプト内では未使用（将来用）

    // ================================
    // 感度バー関連
    // ================================
    [Header("感度バー")]
    public RectTransform SensitivityBarArea;       // バー当たり判定用（なくても可）
    public RectTransform SensitivityHandle;        // ポチ（動かす）
    public RectTransform SensitivityLeftAnchor;    // 左端基準
    public RectTransform SensitivityRightAnchor;   // 右端基準

    private float _handleBaseY;                    // ハンドルのY固定値
    private bool _isDraggingSensitivity = false;   // ドラッグ中か

    [Header("Drag Hit Settings")]
    public float SensitivityDragMaxDistanceY = 15f; // バー中心Yからの許容距離
    public float SensitivityDragExtraX = 10f;       // X方向の当たり判定の余白

    // ================================
    // 表示画像（赤/白切り替え）
    // ================================
    [Header("Toggle Display Images")]
    public Image InvertX_OnImage;
    public Image InvertX_OffImage;
    public Image InvertY_OnImage;
    public Image InvertY_OffImage;
    public Image FullScreen_OnImage;   // ON（ウィンドウ）
    public Image FullScreen_OffImage;  // OFF（フルスク）

    // ================================
    // ゲーム終了（任意）
    // ================================
    [Header("ゲーム終了（任意）")]
    public RectTransform GameEndHit;   // クリック判定
    public Image GameEndImage;         // 色変更

    public Color ActiveColor = Color.red;
    public Color InactiveColor = Color.white;

    private bool _gameEndHover = false; // hover中か

    // ================================
    // クリック判定用Rect
    // ================================
    [Header("Toggle Hit Rects (Click Areas)")]
    public RectTransform InvertX_OnHit;
    public RectTransform InvertX_OffHit;
    public RectTransform InvertY_OnHit;
    public RectTransform InvertY_OffHit;
    public RectTransform FullScreen_OnHit;
    public RectTransform FullScreen_OffHit;

    // ================================
    // 閉じるボタン
    // ================================
    [Header("Close Button Hit (Click Area)")]
    public RectTransform CloseHit;     // ★タイトルオプションを閉じるボタン用

    // ============================================================
    // 初期化
    // ============================================================
    private void Awake()
    {
        // 最初は閉じておく
        MenuOn = false;
        if (MenuWindow) MenuWindow.SetActive(false);

        // 設定読み込み → UI用変数へ反映
        GameSettings.Load();
        SyncFromSettings();

        // ハンドルの基準Y（Xだけ動かしてYは固定にしたい）
        if (SensitivityHandle)
        {
            _handleBaseY = SensitivityHandle.anchoredPosition.y;
        }

        // 現在の感度値に合わせてハンドル位置更新
        UpdateSensitivityHandlePosition();

        // 表示色更新
        RefreshColors();
        RefreshGameEndColor();
    }

    // ============================================================
    // メニューが開いている間だけマウス操作を処理
    // ============================================================
    private void Update()
    {
        // 閉じてるなら何もしない
        if (!MenuOn) return;

        // マウスが取れない環境なら何もしない
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        // ゲーム終了の hover 表示更新
        UpdateGameEndHover(mousePos);

        // クリック開始（押した瞬間）
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // 先に ON/OFF / 閉じる / 終了 などを判定
            TryClickToggles(mousePos);

            // 次に感度ドラッグを開始できるか判定
            TryBeginSensitivityDrag(mousePos);
        }

        // ドラッグ中（押しっぱなし）
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

        // 最新設定を読み直して表示に反映
        GameSettings.Load();
        SyncFromSettings();

        // レイアウト確定後にハンドル位置を揃える（次フレーム）
        StartCoroutine(RepositionNextFrame());

        RefreshColors();
        RefreshGameEndColor();
    }

    public void CloseMenu()
    {
        if (!MenuOn) return;

        MenuOn = false;
        if (MenuWindow) MenuWindow.SetActive(false);

        // ドラッグ/hover をリセット
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
        // RectTransform の範囲内にマウスがあるか？
        bool Hit(RectTransform rt)
        {
            if (rt == null) return false;

            // タイトル画面の想定：ScreenSpaceOverlay なので camera は null
            return RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null);
        }

        // -------------------------
        // 視点操作左右反転
        // -------------------------
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

        // -------------------------
        // 視点操作上下反転
        // -------------------------
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

        // -------------------------
        // スクリーンモード
        // -------------------------
        if (Hit(FullScreen_OnHit))
        {
            // ON → ウィンドウ表示したい
            FullScreen = true;
            GameSettings.SetFullScreenOption(FullScreen);
            GameSettings.Save();

#if !UNITY_EDITOR
            // Screen.fullScreen は「フルスクか？」なので、UIの FullScreen(=ウィンドウ希望) と反転している
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

        // -------------------------
        // 閉じるボタン
        // -------------------------
        if (Hit(CloseHit))
        {
            CloseMenu();
            return;
        }

        // -------------------------
        // ゲーム終了
        // -------------------------
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

        // 状態が変わったときだけ色更新
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

    // 感度バーを掴む判定（クリックした瞬間だけ）
    private void TryBeginSensitivityDrag(Vector2 mousePos)
    {
        // 必要な参照が無いならドラッグ不可
        if (!SensitivityHandle || !SensitivityLeftAnchor || !SensitivityRightAnchor)
        {
            _isDraggingSensitivity = false;
            return;
        }

        // ハンドルの親（同じRectTransform空間）を基準にローカル座標を取る
        RectTransform parent = SensitivityHandle.parent as RectTransform;
        if (!parent)
        {
            _isDraggingSensitivity = false;
            return;
        }

        // スクリーン座標 → parent ローカル座標
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent,
            mousePos,
            null,
            out Vector2 localPoint))
        {
            _isDraggingSensitivity = false;
            return;
        }

        // Xの当たり範囲（左右アンカーの間＋余白）
        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;
        float minX = Mathf.Min(leftX, rightX) - SensitivityDragExtraX;
        float maxX = Mathf.Max(leftX, rightX) + SensitivityDragExtraX;

        // Yの当たり範囲（バー中心Y付近）
        float barY = GetBarCenterYInParent(parent);
        float diffY = Mathf.Abs(localPoint.y - barY);

        bool withinX = (localPoint.x >= minX && localPoint.x <= maxX);
        bool withinY = (diffY <= SensitivityDragMaxDistanceY);

        // この条件を満たしたら “ドラッグ開始” として扱う
        _isDraggingSensitivity = (withinX && withinY);
    }

    // ドラッグ中：感度更新（毎フレーム）
    private void UpdateSensitivityByPointerDrag(Vector2 mousePos)
    {
        if (!SensitivityHandle || !SensitivityLeftAnchor || !SensitivityRightAnchor) return;

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

        // 左右アンカーのXの間を 0〜1 にする
        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;

        float t = 0.5f;
        if (Mathf.Abs(rightX - leftX) > Mathf.Epsilon)
        {
            t = Mathf.InverseLerp(leftX, rightX, localPoint.x);
        }

        _sensitivity01 = Mathf.Clamp01(t);

        // 設定へ保存
        GameSettings.SetSensitivity01(_sensitivity01);
        GameSettings.Save();

        // 見た目（ハンドル位置）更新
        UpdateSensitivityHandlePosition();
    }

    // 親ローカル座標でバーの中心Yを取る
    private float GetBarCenterYInParent(RectTransform parent)
    {
        // BarArea が無い場合は「ハンドルY」をバー中心として扱う（保険）
        if (parent == null || SensitivityBarArea == null)
        {
            return _handleBaseY;
        }

        // BarArea の中心を “親のローカル” に変換してYを使う
        Vector3 barWorldPos = SensitivityBarArea.TransformPoint(SensitivityBarArea.rect.center);
        Vector3 barLocalInParent = parent.InverseTransformPoint(barWorldPos);
        return barLocalInParent.y;
    }

    // ハンドルの見た目更新（Xだけ動かしてYは固定）
    private void UpdateSensitivityHandlePosition()
    {
        if (!SensitivityHandle || !SensitivityLeftAnchor || !SensitivityRightAnchor) return;

        float leftX = SensitivityLeftAnchor.anchoredPosition.x;
        float rightX = SensitivityRightAnchor.anchoredPosition.x;

        // _sensitivity01 (0〜1) を左右アンカーの間のXに変換
        float newX = Mathf.Lerp(leftX, rightX, _sensitivity01);

        SensitivityHandle.anchoredPosition = new Vector2(newX, _handleBaseY);
    }

    // レイアウトが確定してからハンドルを揃える（1フレーム待つ）
    private IEnumerator RepositionNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        UpdateSensitivityHandlePosition();
    }

    // =============================================================================
    // UI色（赤/白）の更新
    // =============================================================================
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