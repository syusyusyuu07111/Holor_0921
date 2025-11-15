using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using CriWare; // CRI

public class TitleMenu : MonoBehaviour
{
    public Canvas UiCanvas;

    public GameObject StartTarget;   // 判定の中心にするオブジェクト（ボタン本体）
    public GameObject OptionTarget;

    public GameObject StartGlow;     // 光らせる見た目側（Image 等）
    public GameObject OptionGlow;

    public string StartSceneName = "Game"; // クリックで遷移するシーン名
    public AudioSource ClickSfx;           // クリックSE（任意）

    // --- 追加: CRI 再生用（Inspector で割り当て） ---
    public CriAtomSource ClickCriSource;
    public string ClickCueName = "ui_start";
    public bool CriStopIfPlaying = true;
    // -------------------------------------------------

    public InputSystem_Actions input;
    public float EnterDistance = 120f; // 入る距離
    public float ExitDistance = 150f;  // 抜ける距離（ヒステリシス）
    public float SwitchGap = 20f;      // 切替のための差

    public Color HighlightColor = new Color(1f, 1f, 1f, 1f);
    public Color NormalColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    // タイトル用オプションメニュー
    public TitleOptionMenu OptionMenu;

    // 内部 ----------------------------------------------------------------------
    RectTransform sRt;
    RectTransform oRt;
    Camera uiCam;

    Vector2 startPos;   // 毎フレーム再計算
    Vector2 optionPos;

    int active = 0;     // 0=なし 1=Start 2=Option

    // 入力 ----------------------------------------------------------------------
    void Awake()
    {
        if (input == null) input = new InputSystem_Actions();
        GameSettings.Load();
    }

    void OnEnable()
    {
        if (input == null) input = new InputSystem_Actions();
        input.UI.Enable();
        ResolveRefs();

        // タイトル入場フェード（任意）
        HorrorScreenFader.FadeIn(1.2f);
    }

    void OnDisable()
    {
        if (input != null) input.UI.Disable();
    }

    void ResolveRefs()
    {
        sRt = StartTarget != null ? StartTarget.GetComponent<RectTransform>() : null;
        oRt = OptionTarget != null ? OptionTarget.GetComponent<RectTransform>() : null;

        if (UiCanvas != null && UiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            uiCam = null;
        else if (UiCanvas != null && UiCanvas.worldCamera != null)
            uiCam = UiCanvas.worldCamera;
        else
            uiCam = Camera.main;

        // 初期色
        SetColor(StartGlow, NormalColor);
        SetColor(OptionGlow, NormalColor);
        active = 0;
    }

    // 更新 ----------------------------------------------------------------------
    void Update()
    {
        if (input == null || sRt == null || oRt == null) return;

        // フェード中は入力無効（任意）
        if (HorrorScreenFader.IsBusy) return;

        // マウス：新InputSystem（UIActionでもMouseでも可）
        Vector2 mouse = input.UI.Point.ReadValue<Vector2>();
        if (mouse == Vector2.zero && Mouse.current != null)
            mouse = Mouse.current.position.ReadValue();

        // ボタンの中心をスクリーン座標で毎フレーム取得（解像度変更対策）
        Vector3 sp1 = RectTransformUtility.WorldToScreenPoint(uiCam, sRt.position);
        Vector3 sp2 = RectTransformUtility.WorldToScreenPoint(uiCam, oRt.position);
        startPos = new Vector2(sp1.x, sp1.y);
        optionPos = new Vector2(sp2.x, sp2.y);

        float dStart = Vector2.Distance(mouse, startPos);
        float dOption = Vector2.Distance(mouse, optionPos);

        // どちらも遠い → 消灯
        if (dStart >= ExitDistance && dOption >= ExitDistance)
        {
            if (active != 0)
            {
                SetColor(StartGlow, NormalColor);
                SetColor(OptionGlow, NormalColor);
                active = 0;
            }
        }
        else
        {
            // 両方近い → 近いほう（差が小さい時は現状維持）
            if (dStart <= EnterDistance && dOption <= EnterDistance)
            {
                if (active == 1 && dOption + SwitchGap < dStart)
                {
                    SetColor(StartGlow, NormalColor);
                    SetColor(OptionGlow, HighlightColor);
                    active = 2;
                }
                else if (active == 2 && dStart + SwitchGap < dOption)
                {
                    SetColor(OptionGlow, NormalColor);
                    SetColor(StartGlow, HighlightColor);
                    active = 1;
                }
                else if (active == 0)
                {
                    if (dStart <= dOption)
                    {
                        SetColor(StartGlow, HighlightColor);
                        SetColor(OptionGlow, NormalColor);
                        active = 1;
                    }
                    else
                    {
                        SetColor(OptionGlow, HighlightColor);
                        SetColor(StartGlow, NormalColor);
                        active = 2;
                    }
                }
            }
            // 片方だけ近い
            else if (dStart <= EnterDistance)
            {
                if (active != 1)
                {
                    SetColor(StartGlow, HighlightColor);
                    SetColor(OptionGlow, NormalColor);
                    active = 1;
                }
            }
            else if (dOption <= EnterDistance)
            {
                if (active != 2)
                {
                    SetColor(OptionGlow, HighlightColor);
                    SetColor(StartGlow, NormalColor);
                    active = 2;
                }
            }
        }

        // クリック処理
        bool leftDown = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (leftDown)
        {
            // Start クリック → SE → フェード経由でシーン遷移
            if (active == 1 && dStart <= EnterDistance)
            {
                if (ClickSfx != null) ClickSfx.Play();

                // --- CRI 再生（Start） ---
                if (ClickCriSource != null)
                {
                    try
                    {
                        if (CriStopIfPlaying && ClickCriSource.status == CriAtomSource.Status.Playing)
                            ClickCriSource.Stop();

                        if (!string.IsNullOrEmpty(ClickCueName))
                            ClickCriSource.Play(ClickCueName);
                        else
                            ClickCriSource.Play();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[TitleMenu] CRI Start 再生失敗: {e.Message}", this);
                    }
                }
                // --------------------------

                if (!string.IsNullOrEmpty(StartSceneName))
                {
                    // 直接ロードの代わりにホラーフェードでロード
                    HorrorScreenFader.FadeAndLoad(StartSceneName, fadeOut: 1.2f, fadeIn: 1.0f, vignettePulse: true, noiseFlicker: true);
                    // SceneManager.LoadScene(StartSceneName); ← 使わない
                }
            }
            // Option クリック → メニュー表示
            else if (active == 2 && dOption <= EnterDistance)
            {
                if (ClickSfx != null) ClickSfx.Play();

                // --- CRI 再生（Option） ---
                if (ClickCriSource != null)
                {
                    try
                    {
                        if (CriStopIfPlaying && ClickCriSource.status == CriAtomSource.Status.Playing)
                            ClickCriSource.Stop();

                        if (!string.IsNullOrEmpty(ClickCueName))
                            ClickCriSource.Play(ClickCueName);
                        else
                            ClickCriSource.Play();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[TitleMenu] CRI Option 再生失敗: {e.Message}", this);
                    }
                }
                // --------------------------

                if (OptionMenu != null)
                {
                    OptionMenu.ToggleMenu();
                }
            }
        }
    }

    // -------------------------------------------------------------
    void SetColor(GameObject go, Color c)
    {
        if (go == null) return;
        Image img = go.GetComponent<Image>();
        if (img != null) img.color = c;
    }
}
