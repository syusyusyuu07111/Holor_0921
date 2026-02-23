using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using CriWare; // CRI

public class TitleMenu : MonoBehaviour
{
    /*
        ============================================================
        TitleMenu がやっていること（内容は変えずに説明を足した版）
        ============================================================

        ■目的
        タイトル画面で
        ・マウスが Start / Option のどちらに近いかを判定して “光る表示” を切り替える
        ・クリックしたら
            Start：SE → フェード → シーン遷移
            Option：SE → オプションメニュー表示
        を行う。

        ■このスクリプトの重要な前提
        ・StartTarget / OptionTarget は「判定の中心」になるUIオブジェクト（RectTransformが付いているもの）
        ・StartGlow / OptionGlow は「見た目側」。色を変えてハイライト表示する対象（Imageが付いている想定）
        ・Canvas が Overlay か Camera かで WorldToScreenPoint のカメラ指定が変わるので UiCanvas 参照があると安全

        ■距離判定（ヒステリシス）
        ・EnterDistance 以内に入ったら「近い」扱い
        ・ExitDistance 以上に離れたら「遠い」扱い（Enterより大きくしてチラつき防止）
        ・両方近い場合は “より近い方” を選ぶが
          SwitchGap 以内の差だと切り替えず現状維持（チラつき防止）

        ■クリック処理
        ・active == 1 → Start がアクティブ
        ・active == 2 → Option がアクティブ
        ・左クリックされたフレームに、active の対象が近ければ実行

        ============================================================
    */

    // ================================
    // UI参照
    // ================================
    public Canvas UiCanvas;

    public GameObject StartTarget;   // 距離判定の中心（ボタン本体など）
    public GameObject OptionTarget;

    public GameObject StartGlow;     // 光らせる見た目側（Image 等）
    public GameObject OptionGlow;

    // ================================
    // 遷移/SE
    // ================================
    public string StartSceneName = "Game"; // Startクリックで遷移するシーン名
    public AudioSource ClickSfx;           // クリックSE（Unity AudioSource / 任意）

    // --- 追加: CRI 再生用（Inspector で割り当て） ---
    public CriAtomSource ClickCriSource;   // CRIで鳴らす場合のAtomSource
    public string ClickCueName = "ui_start";
    public bool CriStopIfPlaying = true;
    // -------------------------------------------------

    // ================================
    // Input
    // ================================
    public InputSystem_Actions input;

    // ================================
    // 距離判定パラメータ
    // ================================
    public float EnterDistance = 120f; // 近い扱いに入る距離
    public float ExitDistance = 150f;  // 遠い扱いに出る距離（Enterより大きく）
    public float SwitchGap = 20f;      // 両方近い時の切替差（これ未満なら現状維持）

    // ================================
    // 表示色
    // ================================
    public Color HighlightColor = new Color(1f, 1f, 1f, 1f);
    public Color NormalColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    // ================================
    // タイトル用オプションメニュー
    // ================================
    public TitleOptionMenu OptionMenu;

    // ================================
    // 内部（キャッシュ）
    // ================================
    RectTransform sRt; // StartTarget の RectTransform
    RectTransform oRt; // OptionTarget の RectTransform
    Camera uiCam;      // スクリーン座標に変換するためのカメラ

    Vector2 startPos;   // StartTarget のスクリーン座標（毎フレーム再計算）
    Vector2 optionPos;  // OptionTarget のスクリーン座標（毎フレーム再計算）

    int active = 0;     // 0=なし 1=Start 2=Option

    // ============================================================
    // ライフサイクル
    // ============================================================

    void Awake()
    {
        // Input生成（未設定なら作る）
        if (input == null) input = new InputSystem_Actions();

        // 設定のロード（プロジェクト側の実装想定）
        GameSettings.Load();
    }

    void OnEnable()
    {
        // 念のため input が null なら生成
        if (input == null) input = new InputSystem_Actions();

        // UIアクションを有効化
        input.UI.Enable();

        // 参照を取り直し（シーン再読み込みや解像度変更対策）
        ResolveRefs();

        // タイトル入場フェード（任意）
        HorrorScreenFader.FadeIn(1.2f);
    }

    void OnDisable()
    {
        // UIアクション停止
        if (input != null) input.UI.Disable();
    }

    // ============================================================
    // 参照解決（RectTransform / カメラ / 初期色）
    // ============================================================

    void ResolveRefs()
    {
        // 距離判定の中心になる RectTransform を取得
        sRt = StartTarget != null ? StartTarget.GetComponent<RectTransform>() : null;
        oRt = OptionTarget != null ? OptionTarget.GetComponent<RectTransform>() : null;

        // Canvas の描画モードでスクリーン変換に使うカメラが変わる
        // ・Overlay → カメラ不要なので null
        // ・Camera/World → Canvas.worldCamera があればそれ、なければ Camera.main
        if (UiCanvas != null && UiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            uiCam = null;
        else if (UiCanvas != null && UiCanvas.worldCamera != null)
            uiCam = UiCanvas.worldCamera;
        else
            uiCam = Camera.main;

        // 初期状態は両方通常色（光らない）
        SetColor(StartGlow, NormalColor);
        SetColor(OptionGlow, NormalColor);

        // 何も選ばれていない状態
        active = 0;
    }

    // ============================================================
    // メイン更新
    // ============================================================

    void Update()
    {
        // 必要な参照が無ければ何もしない
        if (input == null || sRt == null || oRt == null) return;

        // フェード中は入力無効（フェード演出中にクリックされたくない）
        if (HorrorScreenFader.IsBusy) return;

        // ------------------------------------------------------------
        // 1) マウス座標を取得
        // ------------------------------------------------------------
        // UIアクションの Point を優先。取れない場合は Mouse.current を使う保険。
        Vector2 mouse = input.UI.Point.ReadValue<Vector2>();
        if (mouse == Vector2.zero && Mouse.current != null)
            mouse = Mouse.current.position.ReadValue();

        // ------------------------------------------------------------
        // 2) ボタン中心をスクリーン座標に変換（毎フレーム）
        // ------------------------------------------------------------
        // 解像度変更やCanvasスケール変更でもズレないように毎フレーム計算する
        Vector3 sp1 = RectTransformUtility.WorldToScreenPoint(uiCam, sRt.position);
        Vector3 sp2 = RectTransformUtility.WorldToScreenPoint(uiCam, oRt.position);
        startPos = new Vector2(sp1.x, sp1.y);
        optionPos = new Vector2(sp2.x, sp2.y);

        // ------------------------------------------------------------
        // 3) マウスと各ボタン中心の距離を計算
        // ------------------------------------------------------------
        float dStart = Vector2.Distance(mouse, startPos);
        float dOption = Vector2.Distance(mouse, optionPos);

        // ------------------------------------------------------------
        // 4) ハイライト判定（ヒステリシス付き）
        // ------------------------------------------------------------

        // 両方とも遠い → 消灯（active=0）
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
            // 両方近い（Enter以内） → 近いほう。ただし差が小さいなら現状維持
            if (dStart <= EnterDistance && dOption <= EnterDistance)
            {
                // 今Startが点灯中で、Optionのほうが十分近くなったら切替
                if (active == 1 && dOption + SwitchGap < dStart)
                {
                    SetColor(StartGlow, NormalColor);
                    SetColor(OptionGlow, HighlightColor);
                    active = 2;
                }
                // 今Optionが点灯中で、Startのほうが十分近くなったら切替
                else if (active == 2 && dStart + SwitchGap < dOption)
                {
                    SetColor(OptionGlow, NormalColor);
                    SetColor(StartGlow, HighlightColor);
                    active = 1;
                }
                // まだ何も点灯してないなら、単純に近いほうを点灯
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
            // Startだけ近い
            else if (dStart <= EnterDistance)
            {
                if (active != 1)
                {
                    SetColor(StartGlow, HighlightColor);
                    SetColor(OptionGlow, NormalColor);
                    active = 1;
                }
            }
            // Optionだけ近い
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

        // ------------------------------------------------------------
        // 5) クリック処理（左クリックされたフレームだけ）
        // ------------------------------------------------------------
        bool leftDown = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (!leftDown) return;

        // -------------------------
        // Start クリック
        // -------------------------
        if (active == 1 && dStart <= EnterDistance)
        {
            // Unity AudioSource のSE
            if (ClickSfx != null) ClickSfx.Play();

            // CRI のSE（任意）
            if (ClickCriSource != null)
            {
                try
                {
                    // 鳴っていたら止める運用
                    if (CriStopIfPlaying && ClickCriSource.status == CriAtomSource.Status.Playing)
                        ClickCriSource.Stop();

                    // Cue名があるならそのCueを鳴らす。無ければAtomSource側設定で鳴らす
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

            // シーン遷移（ホラーフェード経由）
            if (!string.IsNullOrEmpty(StartSceneName))
            {
                HorrorScreenFader.FadeAndLoad(
                    StartSceneName,
                    fadeOut: 1.2f,
                    fadeIn: 1.0f,
                    vignettePulse: true,
                    noiseFlicker: true
                );

                // SceneManager.LoadScene(StartSceneName); ← 直ロードは使わない
            }
        }
        // -------------------------
        // Option クリック
        // -------------------------
        else if (active == 2 && dOption <= EnterDistance)
        {
            // Unity AudioSource のSE
            if (ClickSfx != null) ClickSfx.Play();

            // CRI のSE（任意）
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

            // タイトル用オプションメニューを開閉
            if (OptionMenu != null)
            {
                OptionMenu.ToggleMenu();
            }
        }
    }

    // ============================================================
    // 見た目側（Glow）の色を変える
    // ============================================================
    void SetColor(GameObject go, Color c)
    {
        if (go == null) return;

        Image img = go.GetComponent<Image>();
        if (img != null) img.color = c;
    }
}