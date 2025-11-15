// TitleTextReveal.cs
// ロゴ(画像)を「左→右」に露出 → 一息 → ボタンをフェードイン
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TitleTextReveal : MonoBehaviour
{
    // ロゴ/ボタン ----------------------------------------------------------------
    public Image TitleLogo;            // ロゴ画像（UI Image）
    public GameObject StartButton;     // 任意
    public GameObject OptionButton;    // 任意
    public AudioSource FinalSfx;       // 任意

    // 時間調整 ------------------------------------------------------------------
    public float StartDelay = 0.35f; // 開始待機
    public float RevealTime = 1.60f; // 0→99% まで
    public float HoldTime = 0.25f; // 99%で一呼吸
    public float SnapTime = 0.15f; // 99→100%
    public float ButtonsDelay = 0.60f; // 完了→ボタン表示まで
    public float ButtonsFade = 0.35f; // ボタンのフェード秒

    // 内部 ----------------------------------------------------------------------
    CanvasGroup startCg;
    CanvasGroup optCg;

    void Awake()
    {
        if (TitleLogo == null) return;

        // ロゴを横方向のフィルにして0から開始
        TitleLogo.type = Image.Type.Filled;
        TitleLogo.fillMethod = Image.FillMethod.Horizontal;
        TitleLogo.fillOrigin = 0;
        TitleLogo.fillAmount = 0f;

        if (StartButton != null)
        {
            startCg = EnsureCanvasGroup(StartButton);
            startCg.alpha = 0f; startCg.interactable = false; startCg.blocksRaycasts = false;
        }
        if (OptionButton != null)
        {
            optCg = EnsureCanvasGroup(OptionButton);
            optCg.alpha = 0f; optCg.interactable = false; optCg.blocksRaycasts = false;
        }
    }

    void Start()
    {
        if (TitleLogo != null) StartCoroutine(Reveal());
    }

    // ロゴ：0→99%（溜め）→100% → ボタン表示
    IEnumerator Reveal()
    {
        yield return new WaitForSeconds(StartDelay);

        // 0→99%（“溜め→急→止め”のカーブ）
        float t = 0f;
        while (t < RevealTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / RevealTime);
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, p));
            TitleLogo.fillAmount = Mathf.Min(0.99f, eased);
            yield return null;
        }

        // 一呼吸
        yield return new WaitForSeconds(HoldTime);

        // 99→100%（スッと）
        float e = 0f;
        while (e < SnapTime)
        {
            e += Time.deltaTime;
            TitleLogo.fillAmount = Mathf.Lerp(0.99f, 1f, e / SnapTime);
            yield return null;
        }

        if (FinalSfx != null) FinalSfx.Play();

        // ボタン
        yield return new WaitForSeconds(ButtonsDelay);
        if (startCg != null) StartCoroutine(FadeIn(startCg, ButtonsFade));
        if (optCg != null) StartCoroutine(FadeIn(optCg, ButtonsFade));
    }

    // 共通 ----------------------------------------------------------------------
    CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    IEnumerator FadeIn(CanvasGroup cg, float time)
    {
        float t = 0f;
        cg.interactable = false; cg.blocksRaycasts = false;
        while (t < time)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.SmoothStep(0f, 1f, t / time);
            yield return null;
        }
        cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
    }
}
