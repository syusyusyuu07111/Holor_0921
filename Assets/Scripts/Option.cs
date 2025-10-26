using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Option : MonoBehaviour
{
    public InputSystem_Actions input;

    public bool MenuOn = false;
    public GameObject MenuWindow;

    // 設定状態
    public bool InvertX = false;      // 視点操作左右反転
    public bool InvertY = false;      // 視点操作上下反転
    public bool FullScreen = true;    // スクリーンモード (true = ON側)

    // ====== 各行の ON / OFF の画像（1枚ずつ） ======
    // 視点操作左右反転
    public Image InvertX_OnImage;
    public Image InvertX_OffImage;

    // 視点操作上下反転
    public Image InvertY_OnImage;
    public Image InvertY_OffImage;

    // スクリーンモード
    public Image FullScreen_OnImage;
    public Image FullScreen_OffImage;

    // 色（インスペクターで設定する）
    public Color ActiveColor = Color.red;    // 今選ばれてるほう
    public Color InactiveColor = Color.white; // 選ばれてないほう

    // 連打防止用（スティック倒しっぱなし対策）
    private bool _didMoveRight = false;
    private bool _didMoveLeft = false;

    private void Awake()
    {
        input = new InputSystem_Actions();

        // メニューは最初閉じる
        MenuOn = false;
        if (MenuWindow != null)
        {
            MenuWindow.SetActive(false);
        }

        RefreshColors();
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

    void Update()
    {
        // 1) メニューの開閉
        if (input.UI.Option.WasPressedThisFrame())
        {
            MenuOn = !MenuOn;
            MenuWindow.SetActive(MenuOn);

            // 入力状態リセットしておく
            _didMoveRight = false;
            _didMoveLeft = false;
        }

        // メニューが閉じてる間はオプション操作しない
        if (MenuOn == false)
        {
            return;
        }

        // 2) メニュー開いてるときの操作
        // ここでは例として 左右反転(InvertX)だけを左右入力で変える例をまず見せる
        // 後で同じノリで上下反転、フルスクリーンにも広げられる

        Vector2 move = input.Player.Move.ReadValue<Vector2>();

        // → 右に倒したら ON
        if (move.x > 0.5f)
        {
            if (_didMoveRight == false)
            {
                InvertX = true;
                RefreshColors();
                _didMoveRight = true;
            }
        }
        else
        {
            _didMoveRight = false;
        }

        // ← 左に倒したら OFF
        if (move.x < -0.5f)
        {
            if (_didMoveLeft == false)
            {
                InvertX = false;
                RefreshColors();
                _didMoveLeft = true;
            }
        }
        else
        {
            _didMoveLeft = false;
        }

        // ↑ここでは InvertX だけいじってるけど、
        // 本当は「今どの項目を選んでるか（カーソル位置）」を持って、
        // その項目に応じて InvertY / FullScreen をいじるようにしていくイメージ。
    }

    // 今の設定状態に合わせて、画像の色を塗り分ける
    void RefreshColors()
    {
        // --- 左右反転 (InvertX) ---
        // InvertX = true のときは「ONが赤 / OFFが白」
        // InvertX = false のときは「OFFが赤 / ONが白」
        if (InvertX_OnImage != null)
        {
            InvertX_OnImage.color = InvertX ? ActiveColor : InactiveColor;
        }
        if (InvertX_OffImage != null)
        {
            InvertX_OffImage.color = InvertX ? InactiveColor : ActiveColor;
        }

        // --- 上下反転 (InvertY) ---
        if (InvertY_OnImage != null)
        {
            InvertY_OnImage.color = InvertY ? ActiveColor : InactiveColor;
        }
        if (InvertY_OffImage != null)
        {
            InvertY_OffImage.color = InvertY ? InactiveColor : ActiveColor;
        }

        // --- スクリーンモード (FullScreen) ---
        if (FullScreen_OnImage != null)
        {
            FullScreen_OnImage.color = FullScreen ? ActiveColor : InactiveColor;
        }
        if (FullScreen_OffImage != null)
        {
            FullScreen_OffImage.color = FullScreen ? InactiveColor : ActiveColor;
        }
    }
}
