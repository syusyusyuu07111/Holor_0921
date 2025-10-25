using TMPro;
using UnityEngine;
using System.Collections;      // コルーチン用
using UnityEngine.Events;      // UnityEvent用

// ------------------------------------------------------------
// MutteringToLine
//   ・HintText から「stateX.elementY が全部出たよ」という合図を受け取って動く
//   ・その瞬間に：
//
//      1) text に line を表示して一時的にアクティブにする
//         （visibleSeconds 経ったら自動で非表示）
//
//      2) RevealObjects[] に入っているオブジェクトを SetActive(true) にする
//         （= レバー/スイッチなどが出現する）
//
//   ・Start時点では RevealObjects[] は強制で全部 SetActive(false) にして隠す
//     → セリフが全部出るまでは絶対に見えないようにする
//
//   ・showOnce=true の場合は一度だけ反応する
//
//   ・OnMutterShown は「いま実際にセリフを出したよ」というタイミングで呼びたい人向け
// ------------------------------------------------------------
public class MutteringToLine : MonoBehaviour
{
    // ----- 表示するテキストUI（このTextに line を流し込んで一時的に表示する） -----
    [Header("一時的に表示するテキストUI")]
    public TextMeshProUGUI text;

    // ----- HintText参照（どのヒント進行を監視するか） -----
    [Header("ヒント管理 (同じシーンの HintText を割り当てる)")]
    public HintText hint;

    // ----- どの行を監視するか：stateX / elementY -----
    [Header("どの行が全部出たら反応するか")]
    [Min(1)] public int targetState = 1;        // state1 / state2 / state3...
    [Range(0, 4)] public int targetElement = 0; // element0 〜 element4 想定

    // ----- 出すセリフ本体 -----
    [Header("しゃべる内容")]
    [TextArea] public string line = "……（台詞）";

    // ----- このセリフを出すのは一度だけにするか -----
    [Header("一度だけ表示するか")]
    public bool showOnce = true;

    // ----- この秒数だけ text を表示したあと自動で消す -----
    [Header("テキストの表示秒数")]
    public float visibleSeconds = 3f;

    // ----- イベント（必要な人用。外部に通知したいときに使える） -----
    [Header("イベント: セリフを実際に表示した瞬間")]
    public UnityEvent OnMutterShown;

    // ----- ここに入ってるオブジェクトは、最初に全部非表示にされる -----
    // ----- そしてセリフが最後まで出たタイミングでまとめて表示される -----
    [Header("セリフが全部出た瞬間に出現させたいオブジェクト(レバー/スイッチ等)")]
    public GameObject[] RevealObjects;

    // ----- 内部状態 -----
    private bool _fired;          // showOnce=true のとき もう発火したか
    private bool _revealed;       // RevealObjects をもう表示済みか
    private Coroutine _showCo;    // text の表示→待機→非表示コルーチン

    // ------------------------------------------------------------
    // Unityイベント系
    // ------------------------------------------------------------

    private void Awake()
    {
        // 念のため最初に全部消す（Awakeでもやっておく）
        HideRevealObjectsInitially();
    }

    private void Start()
    {
        // Startでもう一回保険で消す
        // （Unityの有効化順で Awake より後に SetActive(true) にされてもここで消す）
        HideRevealObjectsInitially();
    }

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

        // HintText の「この行が最後まで出たよ」イベントを購読
        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);

        Debug.Log($"[MutteringToLine] Listening to {hint.name} for {MakeId(targetState, targetElement)}");

        // ここでは「すでにもうその行が開示済みだったら即表示するか？」はやらない
        // つまり、ゲーム開始時点で RevealObjects は必ず非表示のまま
        // → プレイヤーがちゃんと今回ヒントを見終わるまでは出さない
    }

    private void OnDisable()
    {
        if (hint)
        {
            hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
    }

    // ------------------------------------------------------------
    // HintText から「stateX.elementY が全文出たよ」と呼ばれる
    // ------------------------------------------------------------
    private void OnHintLineFullyRevealed(string id)
    {
        if (_fired && showOnce)
        {
            // もうやった後ならスルー
            return;
        }

        string targetId = MakeId(targetState, targetElement);
        Debug.Log($"[MutteringToLine] Got id={id}, target={targetId}");

        if (id == targetId)
        {
            // この瞬間にテキストを表示して、オブジェクトを出す
            ShowNow();
        }
    }

    // ------------------------------------------------------------
    // ShowNow
    //   1) テキストUIを一時的に表示
    //   2) RevealObjects[] を一斉に SetActive(true)
    //   3) OnMutterShown.Invoke() で外部へ通知
    // ------------------------------------------------------------
    private void ShowNow()
    {
        // ---- テキストを一瞬出す ----
        if (text)
        {
            // すでに表示中ならいったん止める
            if (_showCo != null)
            {
                StopCoroutine(_showCo);
                _showCo = null;
            }
            _showCo = StartCoroutine(CoShowTemp());
        }
        else
        {
            Debug.LogWarning("[MutteringToLine] text が割り当てられていませんが、RevealObjects は出します。");
        }

        // ---- レバー / スイッチ の登場（この瞬間に本物として出す）----
        RevealObjectsOnce();

        // ---- イベント通知（外部でなにかしたいなら使える） ----
        if (OnMutterShown != null)
        {
            OnMutterShown.Invoke();
        }

        _fired = true;
        Debug.Log("[MutteringToLine] ShowNow() 完了");
    }

    // ------------------------------------------------------------
    // CoShowTemp
    //   text を visibleSeconds 秒だけ表示 → 消す
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // HideRevealObjectsInitially
    //   Start直後までは RevealObjects[] を必ず非表示にしておく
    //   （「まだセリフを見切ってないのにレバーが見える」を防ぐ）
    // ------------------------------------------------------------
    private void HideRevealObjectsInitially()
    {
        if (RevealObjects == null) return;

        for (int i = 0; i < RevealObjects.Length; i++)
        {
            if (RevealObjects[i])
            {
                RevealObjects[i].SetActive(false);
            }
        }

        // まだ出してないのでフラグもリセットしておく
        _revealed = false;
    }

    // ------------------------------------------------------------
    // RevealObjectsOnce
    //   RevealObjects[] を初回だけ SetActive(true) にする
    //   二重で呼んでも一回目以降は何もしない
    // ------------------------------------------------------------
    private void RevealObjectsOnce()
    {
        if (_revealed) return; // もう出してるなら終わり

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

        _revealed = true;
    }

    // ------------------------------------------------------------
    // MakeId
    //   監視対象の "stateX.elementY" 文字列を作るユーティリティ
    // ------------------------------------------------------------
    private static string MakeId(int state, int element)
    {
        return $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
    }
}
