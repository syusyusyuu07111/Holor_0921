/*
このスクリプトはエンディング演出の進行をまとめて行います。

主な流れ
・開始時にUIテキストを初期化する（メッセージは空、ENDは透明）
・一定時間待ってからメッセージを1文字ずつ表示する（タイプ表示）
・メッセージを少し保持した後、フェードアウトする
・「END」をフェードインして軽く揺らす
・しばらく表示したらタイトルシーンへ戻る

ルール（コメント方針）
・クラス冒頭に「何をするスクリプトか」を説明する
・メソッドごとに「何をするメソッドか」を説明する
・メソッド内部でも、処理のまとまりごとに「何をしているか」を説明する
*/

using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class EndSequence : MonoBehaviour
{
    [Header("テキスト参照")]
    [SerializeField] private TextMeshProUGUI messageText; // メッセージ表示（例：「……あそんでくれて、ありがとう。」）
    [SerializeField] private TextMeshProUGUI endText;     // 「END」表示

    [Header("演出パラメータ")]
    [SerializeField] private float typeSpeed = 0.12f;            // 1文字の表示速度
    [SerializeField] private float messageStayTime = 1.5f;       // メッセージ保持時間
    [SerializeField] private float messageFadeDuration = 1.0f;   // メッセージ消える時間
    [SerializeField] private float endFadeDuration = 0.8f;       // ENDフェードイン時間
    [SerializeField] private float endStayTime = 3.0f;           // ENDの表示時間

    [Header("シーン名")]
    [SerializeField] private string titleSceneName = "Title";    // タイトルシーン名

    // 表示するエンディングメッセージ本文
    private string endingMessage = "……あそんでくれて、ありがとう。";

    /// <summary>
    /// 初期化を行い、エンディング演出のコルーチンを開始する
    /// </summary>
    private void Start()
    {
        // メッセージ側の初期化：文字は空、透明度は表示状態(1)
        if (messageText != null)
        {
            messageText.text = "";

            var c = messageText.color;
            c.a = 1f;
            messageText.color = c;
        }

        // END側の初期化：文字はEND、透明度は非表示(0)
        if (endText != null)
        {
            endText.text = "END";

            var c = endText.color;
            c.a = 0f;
            endText.color = c;
        }

        // 一連のエンディング進行を開始する
        StartCoroutine(Sequence());
    }

    /// <summary>
    /// エンディング演出の全体進行
    /// 待機 → タイプ表示 → 保持 → フェードアウト → ENDフェードイン → 保持 → タイトルへ戻る
    /// </summary>
    private IEnumerator Sequence()
    {
        // 最初に少し待って雰囲気を作る
        yield return new WaitForSeconds(1f);

        // メッセージを1文字ずつ表示する
        yield return StartCoroutine(TypeMessage());

        // 表示したまま少し待つ
        yield return new WaitForSeconds(messageStayTime);

        // メッセージをフェードアウトする
        yield return StartCoroutine(FadeOutMessage());

        // ENDをフェードインする（最後に軽い揺れも入る）
        yield return StartCoroutine(FadeInEnd());

        // ENDを少し表示したままにする
        yield return new WaitForSeconds(endStayTime);

        // タイトルシーンへ戻す
        SceneManager.LoadScene(titleSceneName);
    }

    /// <summary>
    /// endingMessage を1文字ずつ messageText に追加してタイプ表示する
    /// </summary>
    private IEnumerator TypeMessage()
    {
        // 参照がない場合は何もしない
        if (messageText == null) yield break;

        // 文字を一度空にしてから開始する
        messageText.text = "";

        // 1文字ずつ追加して表示する
        foreach (char c in endingMessage)
        {
            messageText.text += c;
            yield return new WaitForSeconds(typeSpeed);
        }
    }

    /// <summary>
    /// messageText を時間をかけてフェードアウト（alpha 1→0）する
    /// </summary>
    private IEnumerator FadeOutMessage()
    {
        // 参照がない場合は何もしない
        if (messageText == null) yield break;

        // フェードの進行管理用
        float t = 0f;

        // 現在色を基準にしてアルファだけ変える
        Color start = messageText.color;

        // 指定時間かけて透明にしていく
        while (t < messageFadeDuration)
        {
            t += Time.deltaTime;

            // 進行度0..1
            float n = Mathf.Clamp01(t / messageFadeDuration);

            // ちょっと滑らかなカーブ（序盤ゆっくり→後半早め）
            float eased = n * n;

            // alpha を 1→0 に補間
            Color c = start;
            c.a = Mathf.Lerp(1f, 0f, eased);
            messageText.color = c;

            yield return null;
        }

        // 念のため完全に透明にして終了する
        Color done = messageText.color;
        done.a = 0f;
        messageText.color = done;
    }

    /// <summary>
    /// endText を時間をかけてフェードイン（alpha 0→1）する
    /// フェード後に TinyShake で軽く揺らす
    /// </summary>
    private IEnumerator FadeInEnd()
    {
        // 参照がない場合は何もしない
        if (endText == null) yield break;

        // フェードの進行管理用
        float t = 0f;

        // 開始時点は透明に固定
        Color start = endText.color;
        start.a = 0f;
        endText.color = start;

        // 指定時間かけて表示にしていく
        while (t < endFadeDuration)
        {
            t += Time.deltaTime;

            // 進行度0..1
            float n = Mathf.Clamp01(t / endFadeDuration);

            // ちょっと滑らかなカーブ（ゆっくり始まってふわっと出る）
            float eased = n * n;

            // alpha を 0→1 にして反映
            Color c = start;
            c.a = eased;
            endText.color = c;

            yield return null;
        }

        // フェード完了後、軽く揺らす演出を入れる
        yield return StartCoroutine(TinyShake(endText));
    }

    /// <summary>
    /// 対象テキストのRectTransformを短時間だけ小さく揺らし、最後に元の位置に戻す
    /// </summary>
    private IEnumerator TinyShake(TextMeshProUGUI target, float duration = 0.2f, float strength = 6f)
    {
        // 参照がない場合は何もしない
        if (target == null) yield break;

        // 揺らす対象のRectTransformと元位置
        RectTransform rt = target.rectTransform;
        Vector3 original = rt.anchoredPosition;

        float t = 0f;

        // duration の間だけランダムなオフセットを与える
        while (t < duration)
        {
            t += Time.deltaTime;

            // 進行度0..1
            float n = t / duration;

            // 徐々に揺れを弱める（最後は0に近づける）
            float damper = 1f - n;

            // X/Y にランダムな揺れを作る
            float offsetX = Random.Range(-1f, 1f) * strength * damper;
            float offsetY = Random.Range(-1f, 1f) * strength * damper;

            // 元位置 + オフセット
            rt.anchoredPosition = original + new Vector3(offsetX, offsetY, 0);

            yield return null;
        }

        // 最後に必ず元位置へ戻す
        rt.anchoredPosition = original;
    }
}