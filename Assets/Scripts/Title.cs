using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Title : MonoBehaviour
{
    public Image TitleLogo;          // ロゴ画像（UI Image）
    public GameObject TitleButton;   // Start
    public GameObject OptionButton;  // Options

    public float StartDelay = 0.35f; // 開始待機
    public float RevealTime = 1.6f;  // 露出にかける時間
    public float HoldBeforeEnd = 0.25f; // 99%で一瞬“溜め”
    public float FlickerAmount = 0.04f; // 露光の揺れ幅（0で無効）
    public float FlickerHz = 0.8f;      // 揺れの周波数
    public float ButtonsDelay = 0.5f;   // 完成→ボタン表示まで
    public float ButtonsFade = 0.35f;   // ボタンのフェード時間

    public AudioSource FinalSfx;        // 完了時のSE（任意）

    private CanvasGroup btnStartCg;
    private CanvasGroup btnOptCg;

    void Awake()
    {
        if (TitleLogo == null) return;

        TitleLogo.type = Image.Type.Filled;
        TitleLogo.fillMethod = Image.FillMethod.Horizontal;
        TitleLogo.fillOrigin = 0;
        TitleLogo.fillAmount = 0f;

        if (TitleButton != null)
        {
            btnStartCg = EnsureCanvasGroup(TitleButton);
            btnStartCg.alpha = 0f; btnStartCg.interactable = false; btnStartCg.blocksRaycasts = false;
        }
        if (OptionButton != null)
        {
            btnOptCg = EnsureCanvasGroup(OptionButton);
            btnOptCg.alpha = 0f; btnOptCg.interactable = false; btnOptCg.blocksRaycasts = false;
        }
    }

    void Start()
    {
        if (TitleLogo != null) StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        yield return new WaitForSeconds(StartDelay);

        float t = 0f;
        float baseA = TitleLogo.color.a;
        float flicker = 1f;

        // 0→0.99 まで連続で“溜めながら”露出
        while (t < RevealTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / RevealTime);
            // 2段SmoothStepで“溜め→急→止め”
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, p));
            float targetFill = Mathf.Min(0.99f, eased); // 最後の1%を残して溜め
            TitleLogo.fillAmount = targetFill;

            // 低周波だけをゆっくりブレンドしてαに反映（急なチラつき防止）
            if (FlickerAmount > 0f)
            {
                float noise = (Mathf.PerlinNoise(Time.time * FlickerHz, 0f) - 0.5f) * 2f; // -1..1
                float targetMul = 1f + noise * FlickerAmount;
                flicker = Mathf.Lerp(flicker, targetMul, 0.2f);
                Color c = TitleLogo.color; c.a = Mathf.Clamp01(baseA * flicker); TitleLogo.color = c;
            }

            yield return null;
        }

        // 溜め
        yield return new WaitForSeconds(HoldBeforeEnd);

        // 最後の1%をスッと出す → SE
        float endT = 0f;
        while (endT < 0.15f)
        {
            endT += Time.deltaTime;
            float k = endT / 0.15f;
            TitleLogo.fillAmount = Mathf.Lerp(0.99f, 1f, k);
            yield return null;
        }
        if (FinalSfx != null) FinalSfx.Play();

        // αを1に固定（揺れ停止）
        Color c2 = TitleLogo.color; c2.a = 1f; TitleLogo.color = c2;

        // ボタンを遅延フェードイン
        yield return new WaitForSeconds(ButtonsDelay);
        if (btnStartCg != null) StartCoroutine(FadeIn(btnStartCg, ButtonsFade));
        if (btnOptCg != null) StartCoroutine(FadeIn(btnOptCg, ButtonsFade));
    }

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
            float k = t / time;
            cg.alpha = Mathf.SmoothStep(0f, 1f, k);
            yield return null;
        }
        cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
    }
}
