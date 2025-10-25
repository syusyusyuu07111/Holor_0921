using TMPro;
using UnityEngine;
using System.Collections;      // コルーチン用
using UnityEngine.Events;      // UnityEvent用

//==================================================
// MutteringToLine
// 特定のヒント(stateX.elementY)が全文表示された瞬間に
// 一時的なモノローグ行を出すコンポーネント。
//
// ・HintText.OnLineFullyRevealed(id) を購読して反応する
// ・text には line が入り、active になり、visibleSeconds 後に自動で消える
// ・showOnce=true なら一度きり
//
// ・レバー(スイッチ)の出現もここでやる
//   => 「この台詞が今表示された瞬間」に RevealObjects を SetActive(true)
//
// ・OnMutterShown は「今しゃべったよ」の通知用(外に知らせたい時用)
//
// ・triggerOnAlreadyRevealed が true の場合、
//   このコンポーネントが有効化された時点ですでに対象行が開いていたら即 ShowNow() する。
//   false ならそれはしない（今回これを false にすれば、後から有効化されても勝手にスイッチ出ない）
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

    [Header("イベント(台詞を実際に表示した瞬間に呼ばれる)")]
    public UnityEvent OnMutterShown;

    // ------------------------------------------------
    // レバー関連
    // 「この台詞を出した瞬間に、ここに入ってるオブジェクト群を有効化する」
    // 例えばレバー本体やスイッチUIなどを並べておく
    // ------------------------------------------------
    [Header("この台詞が出た瞬間に出現させたいオブジェクト(レバーなど)")]
    public GameObject[] RevealObjects;

    // ------------------------------------------------
    // すでにその行が開示済みのとき、OnEnable()で即ShowNow()するか？
    // これを false にすれば「過去にもう出てたやつでは出さない、今回リアルタイムで開いたときだけ」
    // ------------------------------------------------
    [Header("既に開示済みでもOnEnableで即表示する？")]
    public bool triggerOnAlreadyRevealed = false;

    // 内部状態
    private bool _fired;          // showOnce=true のとき、もう発火済みか
    private bool _leverDone;      // レバー(スイッチ)はもう出したか
    private Coroutine _showCo;    // 表示→待機→非表示 のコルーチン

    //==================================================
    // OnEnable
    //  - hint参照を自動で補完
    //  - HintText の "全文表示された" イベントを購読
    //  - triggerOnAlreadyRevealed が true の場合のみ、
    //    すでにその行が開示済みならここで ShowNow()
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

        // イベント購読
        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);
        Debug.Log($"[MutteringToLine] Subscribed to {hint.name}. target={MakeId(targetState, targetElement)}");

        // ここが今回のキモ
        if (triggerOnAlreadyRevealed)
        {
            if (hint.HasLineBeenRevealed(targetState, targetElement))
            {
                Debug.Log("[MutteringToLine] target already revealed. show immediately (because triggerOnAlreadyRevealed=true).");
                ShowNow();
            }
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
    // OnHintLineFullyRevealed
    // HintText から「stateX.elementY が今ぜんぶ出たよ」という通知
    // ここで対象なら ShowNow() に進む
    //==================================================
    private void OnHintLineFullyRevealed(string id)
    {
        if (_fired && showOnce) return;

        string targetId = MakeId(targetState, targetElement);
        Debug.Log($"[MutteringToLine] Received id={id} (target={targetId})");

        if (id == targetId)
        {
            ShowNow();
        }
    }

    //==================================================
    // ShowNow
    // 1) テキストを出す
    // 2) レバー(スイッチ)を出す（一度だけ）
    // 3) OnMutterShown.Invoke() を投げる
    //==================================================
    private void ShowNow()
    {
        // ---- テキスト出す ----
        if (!text)
        {
            _fired = true;
            TryRevealObjectsOnce(); // text が無くてもレバーだけは出す
            Debug.LogWarning("[MutteringToLine] text が割り当てられていません。");
            return;
        }

        // 表示コルーチンをリセット
        if (_showCo != null)
        {
            StopCoroutine(_showCo);
            _showCo = null;
        }
        _showCo = StartCoroutine(CoShowTemp());

        // ---- レバー出す(一度だけ) ----
        TryRevealObjectsOnce();

        // ---- イベント通知 ----
        if (OnMutterShown != null)
        {
            OnMutterShown.Invoke();
        }

        _fired = true;
        Debug.Log("[MutteringToLine] SHOWN (and lever revealed if any)");
    }

    //==================================================
    // CoShowTemp
    // text を visibleSeconds 秒だけ表示→消す
    //==================================================
    private IEnumerator CoShowTemp()
    {
        text.gameObject.SetActive(true);
        text.text = line;

        float waitSec = Mathf.Max(0f, visibleSeconds);
        if (waitSec > 0f)
        {
            yield return new WaitForSeconds(waitSec);
        }

        text.gameObject.SetActive(false);

        _showCo = null;
    }

    //==================================================
    // TryRevealObjectsOnce
    // RevealObjects に入っているオブジェクトを一度だけ SetActive(true)
    //==================================================
    private void TryRevealObjectsOnce()
    {
        if (_leverDone) return; // もうやった

        if (RevealObjects != null)
        {
            for (int i = 0; i < RevealObjects.Length; i++)
            {
                if (RevealObjects[i])
                {
                    RevealObjects[i].SetActive(true);
                }
            }
        }

        _leverDone = true;
    }

    //==================================================
    // MakeId
    // targetState / targetElement から "stateX.elementY" を組む
    //==================================================
    private static string MakeId(int state, int element)
    {
        return $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
    }
}
