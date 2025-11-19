using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class EndSequence : MonoBehaviour
{
    [Header("テキスト参照")]
    [SerializeField] private TextMeshProUGUI messageText; // 「……あそんでくれて、ありがとう。」
    [SerializeField] private TextMeshProUGUI endText;     // 「END」

    [Header("演出パラメータ")]
    [SerializeField] private float typeSpeed = 0.12f;      // 1文字の表示速度
    [SerializeField] private float messageStayTime = 1.5f; // メッセージ保持時間
    [SerializeField] private float messageFadeDuration = 1.0f; // メッセージ消える時間
    [SerializeField] private float endFadeDuration = 0.8f; // ENDフェードイン時間
    [SerializeField] private float endStayTime = 3.0f;     // ENDの表示時間

    [Header("シーン名")]
    [SerializeField] private string titleSceneName = "Title"; // タイトルシーン名

    private string endingMessage = "……あそんでくれて、ありがとう。";

    private void Start()
    {
        // 初期状態
        if (messageText != null)
        {
            messageText.text = "";
            var c = messageText.color;
            c.a = 1f;
            messageText.color = c;
        }

        if (endText != null)
        {
            endText.text = "END";
            var c = endText.color;
            c.a = 0f;
            endText.color = c;
        }

        StartCoroutine(Sequence());
    }

    private IEnumerator Sequence()
    {
        // 1秒待って雰囲気作り
        yield return new WaitForSeconds(1f);

        // メッセージをタイピング表示
        yield return StartCoroutine(TypeMessage());

        // 表示したまま少し置く
        yield return new WaitForSeconds(messageStayTime);

        // メッセージをフェードアウト
        yield return StartCoroutine(FadeOutMessage());

        // ENDをフェードイン
        yield return StartCoroutine(FadeInEnd());

        // ENDを少し見せたまま
        yield return new WaitForSeconds(endStayTime);

        // タイトルシーンへ
        SceneManager.LoadScene(titleSceneName);
    }

    private IEnumerator TypeMessage()
    {
        if (messageText == null) yield break;

        messageText.text = "";

        foreach (char c in endingMessage)
        {
            messageText.text += c;
            yield return new WaitForSeconds(typeSpeed);
        }
    }

    private IEnumerator FadeOutMessage()
    {
        if (messageText == null) yield break;

        float t = 0f;
        Color start = messageText.color;

        while (t < messageFadeDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / messageFadeDuration);
            float eased = n * n; // ちょっと滑らか
            Color c = start;
            c.a = Mathf.Lerp(1f, 0f, eased);
            messageText.color = c;
            yield return null;
        }

        Color done = messageText.color;
        done.a = 0f;
        messageText.color = done;
    }

    private IEnumerator FadeInEnd()
    {
        if (endText == null) yield break;

        float t = 0f;
        Color start = endText.color;
        start.a = 0f;
        endText.color = start;

        while (t < endFadeDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / endFadeDuration);
            float eased = n * n; // ゆっくり始まって、ふわっと出る
            Color c = start;
            c.a = eased;
            endText.color = c;
            yield return null;
        }

        // 軽く揺れる演出
        yield return StartCoroutine(TinyShake(endText));
    }

    private IEnumerator TinyShake(TextMeshProUGUI target, float duration = 0.2f, float strength = 6f)
    {
        if (target == null) yield break;

        RectTransform rt = target.rectTransform;
        Vector3 original = rt.anchoredPosition;

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float n = t / duration;
            float damper = 1f - n; // 徐々に弱まる

            float offsetX = Random.Range(-1f, 1f) * strength * damper;
            float offsetY = Random.Range(-1f, 1f) * strength * damper;

            rt.anchoredPosition = original + new Vector3(offsetX, offsetY, 0);

            yield return null;
        }

        rt.anchoredPosition = original; // 戻す
    }
}
