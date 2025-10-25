using TMPro;
using UnityEngine;
using System.Collections;  // コルーチン用

//==================================================
// MutteringToLine
// 特定のヒント(stateX.elementY)が全文表示された瞬間に
// 一時的なモノローグ行を出すコンポーネント。
// - HintText.OnLineFullyRevealed(id) を購読して反応する
// - 既に開示済みなら有効化時に即表示して追いつく
// - テキストは visibleSeconds 秒後に自動で消える
// - showOnce=true なら一度だけ
//==================================================
public class MutteringToLine : MonoBehaviour
{
    [Header("出力先 (このTextに台詞を表示して一時的にActiveにする)")]
    public TextMeshProUGUI text;

    [Header("参照 (同じシーンの HintText を割り当てる)")]
    public HintText hint;

    [Header("どの行でしゃべるか (stateX / elementY)")]
    [Min(1)] public int targetState = 1;        // 1 / 2 / 3 …
    [Range(0, 4)] public int targetElement = 0; // 0〜4 想定

    [Header("しゃべる内容")]
    [TextArea] public string line = "……（台詞）";

    [Header("一度だけ表示するか")]
    public bool showOnce = true;

    [Header("表示時間(秒) (この秒数経ったら自動で消える)")]
    public float visibleSeconds = 3f;

    // 内部状態
    private bool _fired;          // もう出したかどうか（showOnce=true用）
    private Coroutine _showCo;    // 表示→待機→非表示のコルーチン

    //==================================================
    // ライフサイクル / セットアップ
    // - hint が未指定なら探す
    // - HintText のイベント購読
    // - すでに対象行が開示済みなら即表示で追いつく
    //==================================================
    private void OnEnable()
    {
        // hint が未割り当てなら自動で探す
        if (!hint)
        {
#if UNITY_2023_1_OR_NEWER
            hint = Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            hint = FindObjectOfType<HintText>(true);
#endif
        }

        if (!hint)
        {
            Debug.LogWarning("[MutteringToLine] HintText が見つかりません。");
            return;
        }

        // ヒント全文表示イベントを購読
        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);
        Debug.Log($"[MutteringToLine] Subscribed to {hint.name}. target={MakeId(targetState, targetElement)}");

        // すでにその行が開示済みなら、今すぐ表示して追いつく
        if (hint.HasLineBeenRevealed(targetState, targetElement))
        {
            Debug.Log("[MutteringToLine] target already revealed. show immediately.");
            ShowNow();
        }
    }

    private void OnDisable()
    {
        if (hint)
        {
            hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
    }

    //==================================================
    // イベントコールバック
    // HintText から「この行が全部出たよ」って通知が来る
    // id は "stateX.elementY" 形式
    //==================================================
    private void OnHintLineFullyRevealed(string id)
    {
        if (_fired && showOnce) return; // 一度だけモードなら二回目は無視

        string targetId = MakeId(targetState, targetElement);
        Debug.Log($"[MutteringToLine] Received id={id} (target={targetId})");

        if (id == targetId)
        {
            ShowNow();
        }
    }

    //==================================================
    // ShowNow
    // 実際に台詞を画面に出すトリガ。
    // - text に line を入れてActiveにする
    // - visibleSeconds 秒後に自動で消すコルーチンを走らせる
    //==================================================
    private void ShowNow()
    {
        if (!text)
        {
            _fired = true;
            Debug.LogWarning("[MutteringToLine] text が割り当てられていません。");
            return;
        }

        // すでに表示中だったらリセット（秒数カウントし直す）
        if (_showCo != null)
        {
            StopCoroutine(_showCo);
            _showCo = null;
        }

        _showCo = StartCoroutine(CoShowTemp());

        _fired = true;
        Debug.Log("[MutteringToLine] SHOWN (temp)");
    }

    //==================================================
    // CoShowTemp
    // 一定時間表示→消す
    //==================================================
    private IEnumerator CoShowTemp()
    {
        // 表示
        text.gameObject.SetActive(true);
        text.text = line;

        // visibleSeconds 秒待つ（0以下なら即消し）
        float waitSec = Mathf.Max(0f, visibleSeconds);
        if (waitSec > 0f)
        {
            yield return new WaitForSeconds(waitSec);
        }

        // 消す
        text.gameObject.SetActive(false);

        _showCo = null;
    }

    //==================================================
    // MakeId
    // targetState / targetElement から "stateX.elementY" 文字列を作る
    //==================================================
    private static string MakeId(int state, int element)
    {
        return $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
    }
}
