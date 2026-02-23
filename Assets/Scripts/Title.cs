// TitleTextReveal.cs
// ロゴ(画像)を「左→右」に露出 → 一息 → ボタンをフェードイン

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TitleTextReveal : MonoBehaviour
{
    /*
        ============================================================
        TitleTextReveal がやっていること
        ============================================================

        ■全体の流れ

        ① 少し待つ（StartDelay）
        ② ロゴを左→右へ 0% → 99% まで表示（溜め演出）
        ③ 一呼吸止める（HoldTime）
        ④ 99% → 100% にスッと表示（SnapTime）
        ⑤ 効果音再生（任意）
        ⑥ スタート / オプションボタンをフェードイン表示

        ------------------------------------------------------------
        ■ポイント

        ・ロゴは Image の "Filled / Horizontal" を使って
          fillAmount を 0 → 1 に変化させることで
          「左から現れる」演出をしている。

        ・ボタンは CanvasGroup を使って
          alpha を 0 → 1 にしてフェードインさせている。

        ============================================================
    */

    // ================================
    // ロゴ / ボタン参照
    // ================================
    public Image TitleLogo;            // タイトルロゴ（UI Image）
    public GameObject StartButton;     // スタートボタン（任意）
    public GameObject OptionButton;    // オプションボタン（任意）
    public AudioSource FinalSfx;       // ロゴ完成時に鳴らすSE（任意）

    // ================================
    // 時間設定
    // ================================
    public float StartDelay = 0.35f;   // 演出開始までの待機時間
    public float RevealTime = 1.60f;   // 0 → 99% までの時間
    public float HoldTime = 0.25f;     // 99%で止める時間（溜め）
    public float SnapTime = 0.15f;     // 99 → 100% までの時間
    public float ButtonsDelay = 0.60f; // ロゴ完成後 → ボタン表示までの待機
    public float ButtonsFade = 0.35f;  // ボタンのフェード時間

    // ================================
    // 内部用
    // ================================
    CanvasGroup startCg;
    CanvasGroup optCg;

    // ------------------------------------------------------------
    // 初期化
    // ------------------------------------------------------------
    void Awake()
    {
        if (TitleLogo == null) return;

        // ロゴを「横方向のフィル」に設定
        // これで fillAmount を変えると左→右に表示される
        TitleLogo.type = Image.Type.Filled;
        TitleLogo.fillMethod = Image.FillMethod.Horizontal;
        TitleLogo.fillOrigin = 0;      // 左から
        TitleLogo.fillAmount = 0f;     // 最初は完全に非表示

        // スタートボタンのCanvasGroupを準備
        if (StartButton != null)
        {
            startCg = EnsureCanvasGroup(StartButton);

            // 最初は非表示＆クリック不可
            startCg.alpha = 0f;
            startCg.interactable = false;
            startCg.blocksRaycasts = false;
        }

        // オプションボタンも同様
        if (OptionButton != null)
        {
            optCg = EnsureCanvasGroup(OptionButton);

            optCg.alpha = 0f;
            optCg.interactable = false;
            optCg.blocksRaycasts = false;
        }
    }

    void Start()
    {
        // ロゴが設定されていれば演出開始
        if (TitleLogo != null)
            StartCoroutine(Reveal());
    }

    // ------------------------------------------------------------
    // ロゴ演出メイン
    // ------------------------------------------------------------
    IEnumerator Reveal()
    {
        // ① 開始待機
        yield return new WaitForSeconds(StartDelay);

        // ② 0 → 99% 表示
        float t = 0f;

        while (t < RevealTime)
        {
            t += Time.deltaTime;

            // 0〜1 に正規化
            float p = Mathf.Clamp01(t / RevealTime);

            // 少し溜めのあるイージング
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, p));

            // 99%までしか出さない（完全に出さない）
            TitleLogo.fillAmount = Mathf.Min(0.99f, eased);

            yield return null;
        }

        // ③ 一呼吸止める
        yield return new WaitForSeconds(HoldTime);

        // ④ 99% → 100% をスッと出す
        float e = 0f;

        while (e < SnapTime)
        {
            e += Time.deltaTime;

            TitleLogo.fillAmount = Mathf.Lerp(0.99f, 1f, e / SnapTime);

            yield return null;
        }

        // ⑤ 完了SE
        if (FinalSfx != null)
            FinalSfx.Play();

        // ⑥ ボタン表示まで少し待つ
        yield return new WaitForSeconds(ButtonsDelay);

        if (startCg != null)
            StartCoroutine(FadeIn(startCg, ButtonsFade));

        if (optCg != null)
            StartCoroutine(FadeIn(optCg, ButtonsFade));
    }

    // ------------------------------------------------------------
    // CanvasGroup を必ず持たせる
    // ------------------------------------------------------------
    CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();

        if (cg == null)
            cg = go.AddComponent<CanvasGroup>();

        return cg;
    }

    // ------------------------------------------------------------
    // フェードイン処理
    // ------------------------------------------------------------
    IEnumerator FadeIn(CanvasGroup cg, float time)
    {
        float t = 0f;

        // フェード中はクリック不可
        cg.interactable = false;
        cg.blocksRaycasts = false;

        while (t < time)
        {
            t += Time.deltaTime;

            // alpha を 0 → 1 に
            cg.alpha = Mathf.SmoothStep(0f, 1f, t / time);

            yield return null;
        }

        // 完全表示
        cg.alpha = 1f;

        // クリック可能に戻す
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }
}