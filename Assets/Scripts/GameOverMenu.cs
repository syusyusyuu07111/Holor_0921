using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class GameOverMenu : MonoBehaviour
{
    [Header("リトライで読み込みたいシーン名（インゲーム）")]
    public string GameplaySceneName = "SampleScene";

    [Header("タイトル画面のシーン名")]
    public string TitleSceneName = "TitleScene";

    [Header("リトライの当たり判定(クリック範囲)")]
    public RectTransform RetryHitArea;
    [Header("リトライ表示用のImage")]
    public Image RetryImage;

    [Header("タイトルの当たり判定(クリック範囲)")]
    public RectTransform TitleHitArea;
    [Header("タイトル表示用のImage")]
    public Image TitleImage;

    [Header("ホバー時カラー / 非ホバー時カラー")]
    public Color ActiveColor = Color.white;
    public Color InactiveColor = Color.white;

    private bool _retryHover = false;
    private bool _titleHover = false;

    private InputSystem_Actions _input;

    // ここ追加
    private void Start()
    {
        // ゲームオーバー画面ではマウスを見せて、自由に動かせるようにする
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f; // 念のため止まってたら戻す
    }

    private void Awake()
    {
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.UI.Enable();
        _input.Player.Enable();
        RefreshHoverColors();
    }

    private void OnDisable()
    {
        if (_input != null)
        {
            _input.UI.Disable();
            _input.Player.Disable();
        }
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        UpdateHover(mousePos);

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryClick(mousePos);
        }
    }

    private void UpdateHover(Vector2 mousePos)
    {
        bool retryNow = RectHit(RetryHitArea, mousePos);
        bool titleNow = RectHit(TitleHitArea, mousePos);

        if (retryNow != _retryHover || titleNow != _titleHover)
        {
            _retryHover = retryNow;
            _titleHover = titleNow;
            RefreshHoverColors();
        }
    }

    private void RefreshHoverColors()
    {
        if (RetryImage)
            RetryImage.color = _retryHover ? ActiveColor : InactiveColor;

        if (TitleImage)
            TitleImage.color = _titleHover ? ActiveColor : InactiveColor;
    }

    private void TryClick(Vector2 mousePos)
    {
        if (RectHit(RetryHitArea, mousePos))
        {
            LoadRetry();
            return;
        }

        if (RectHit(TitleHitArea, mousePos))
        {
            LoadTitle();
            return;
        }
    }

    private void LoadRetry()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (!string.IsNullOrEmpty(GameplaySceneName))
        {
            SceneManager.LoadScene(GameplaySceneName);
        }
        else
        {
            Debug.LogError("[GameOverMenu] GameplaySceneName が設定されていません。");
        }
    }

    private void LoadTitle()
    {
        Time.timeScale = 1f;
        // タイトルでもマウス使うならここで None/true のままでもOK
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (!string.IsNullOrEmpty(TitleSceneName))
        {
            SceneManager.LoadScene(TitleSceneName);
        }
        else
        {
            Debug.LogError("[GameOverMenu] TitleSceneName が設定されていません。");
        }
    }

    private bool RectHit(RectTransform rt, Vector2 mousePos)
    {
        if (!rt) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null);
    }
}
