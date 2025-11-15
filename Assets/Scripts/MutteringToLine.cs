using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;

// ------------------------------------------------------------
// MutteringToLine
//
// ・HintText から「stateX.elementY が全文表示されたよ」という通知(id)を受け取る
// ・その id が TriggerTargets[] のどれかと一致したら発火
//
// 発火すると：
//   1) text に line を表示して一定秒数後に消す
//   2) RevealObjects[] を SetActive(true) にして出現させる
//   3) OnMutterShown.Invoke() を呼ぶ（外に知らせたい用）
//
// さらに、RevealObjects[] はゲーム開始時点では必ず隠す。
// showOnce = true なら一回きり。
// ------------------------------------------------------------
public class MutteringToLine : MonoBehaviour
{
    // ---- 複数トリガー対応用：監視したい (state, element) を並べる ----
    [System.Serializable]
    public struct TriggerTarget
    {
        [Min(1)] public int state;       // state1 / state2 / state3...
        [Range(0, 4)] public int element; // element0 ~ element4 想定
    }

    [Header("どのヒント行(複数可)が全文出たら発火させるか")]
    public TriggerTarget[] TriggerTargets;

    // ---- ヒント管理コンポーネント ----
    [Header("ヒント管理 (同じシーンの HintText を割り当てる)")]
    public HintText hint;

    // ---- セリフ表示まわり ----
    [Header("一時的に表示するテキストUI")]
    public TextMeshProUGUI text;

    [Header("しゃべる内容")]
    [TextArea] public string line = "……（台詞）";

    [Header("この秒数だけ表示してから自動で消す")]
    public float visibleSeconds = 3f;

    [Header("一度だけ発火するか")]
    public bool showOnce = true;

    [Header("イベント: セリフを実際に表示した瞬間呼ぶ")]
    public UnityEvent OnMutterShown;

    // ---- 出したいオブジェクト（レバーとか） ----
    [Header("このタイミングで出現させたいオブジェクト(レバー/スイッチ等)")]
    public GameObject[] RevealObjects;

    // ---- 内部状態 ----
    private bool _fired = false;        // showOnce の一回きり制御
    private bool _revealed = false;     // RevealObjects をもう出したか
    private Coroutine _showCo = null;   // テキストの表示コルーチン

    // ============================================================
    // Unity ライフサイクル
    // ============================================================

    private void Awake()
    {
        // 念のため Awake でも隠す
        HideRevealObjectsInitially();
    }

    private void Start()
    {
        // Start でもう一回隠す（ほかのスクリプトに先に SetActive(true) されてもここで消す）
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

        // HintText の「この行が全部表示されたよ」イベントを購読
        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);

        // デバッグ用ログ
        for (int i = 0; i < TriggerTargets.Length; i++)
        {
            Debug.Log($"[MutteringToLine] Listen for {MakeId(TriggerTargets[i].state, TriggerTargets[i].element)}");
        }

        // 今回は「すでに開示済みなら即出す」はやらない方針
        // → 毎回ちゃんとその場でヒントを見切った瞬間だけレバー出したい、って用途想定
    }

    private void OnDisable()
    {
        if (hint)
        {
            hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
    }

    // ============================================================
    // Hint 側から飛んでくるコールバック
    //   id は "stateX.elementY"
    //   例: "state2.element0"
    // ============================================================
    private void OnHintLineFullyRevealed(string id)
    {
        if (_fired && showOnce)
        {
            // もう発火済みで1回きり設定ならスルー
            return;
        }

        // 受け取った id が TriggerTargets のどれかに一致する？
        if (DoesMatchAnyTarget(id))
        {
            ShowNow(); // その瞬間に実行
        }
    }

    // TriggerTargets のどれかと一致するか（OR条件）
    private bool DoesMatchAnyTarget(string id)
    {
        for (int i = 0; i < TriggerTargets.Length; i++)
        {
            string want = MakeId(TriggerTargets[i].state, TriggerTargets[i].element);
            if (id == want) return true;
        }
        return false;
    }

    // ============================================================
    // ShowNow
    //   1) テキストを一時的に見せる
    //   2) RevealObjects を一斉に SetActive(true)
    //   3) OnMutterShown.Invoke() を呼ぶ
    // ============================================================
    private void ShowNow()
    {
        // ---- テキスト ----
        if (text)
        {
            // すでに再生中なら止めてリスタート
            if (_showCo != null)
            {
                StopCoroutine(_showCo);
                _showCo = null;
            }
            _showCo = StartCoroutine(CoShowTemp());
        }
        else
        {
            Debug.LogWarning("[MutteringToLine] text 未設定。テキストは出せないけどオブジェクトは出します。");
        }

        // ---- レバー/スイッチなどを出す ----
        RevealObjectsOnce();

        // ---- イベント通知 ----
        OnMutterShown?.Invoke();

        _fired = true;
        Debug.Log("[MutteringToLine] ShowNow() 発火");
    }

    // テキストを visibleSeconds 秒だけ表示して、その後消す
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

    // ============================================================
    // RevealObjects をゲーム開始時は必ず隠す
    // ============================================================
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

        _revealed = false;
    }

    // ============================================================
    // RevealObjects を一度だけ出す
    // ============================================================
    private void RevealObjectsOnce()
    {
        if (_revealed) return;

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

    // ============================================================
    // MakeId
    //   state / element から "stateX.elementY" を作る
    // ============================================================
    private static string MakeId(int state, int element)
    {
        return $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
    }
}
