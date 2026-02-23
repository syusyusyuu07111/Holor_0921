using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;

/*
     役割：
     「特定のヒント行を“最後まで読んだ瞬間”に、
      つぶやき表示＋ギミック出現を発火させるスクリプト」

     何のためにある？
     ・ヒントを読み切ったことを“次の進行条件”にしたい
     ・読了した瞬間に「……」などの演出を出したい
     ・そのタイミングでレバー/スイッチ/アイテム等を出現させたい

     具体的に何が起きる？
     1) HintText から「この行が全文表示された」という通知(id)を受け取る
        例: "state3.element2"

     2) Inspectorで設定した TriggerTargets のどれかと一致したら発火する

     3) 発火したら以下を行う
        ・text に line を表示して visibleSeconds 秒後に消す
        ・RevealObjects を SetActive(true) にして出現させる（レバー/スイッチ等）
        ・OnMutterShown を Invoke して外部へ通知する

     4) RevealObjects はゲーム開始時点で必ず隠す
        ・Awake と Start の両方で SetActive(false) する
        （他スクリプトが先に表示しても、開始時点では必ず隠したい想定）

     5) showOnce=true の場合は1回だけ発火する
        ・一度発火したら以降の通知は無視する
*/

public class MutteringToLine : MonoBehaviour
{
    //================
    // TriggerTargets（どのヒント行で発火するか）
    //================

    /*
         HintText から来る通知 id の形式は "stateX.elementY"
         ここでは「監視したい state と element の組」を複数登録できる

         例：
         TriggerTargets に (state=3, element=2) を入れる
         → "state3.element2" が来た瞬間に発火する
    */
    [System.Serializable]
    public struct TriggerTarget
    {
        [Min(1)] public int state;                           // state1 / state2 / state3...
        [Range(0, 4)] public int element;                    // element0 ~ element4
    }

    [Header("どのヒント行(複数可)が全文出たら発火させるか")]
    public TriggerTarget[] TriggerTargets;

    //================
    // HintText参照（通知元）
    //================

    /*
         通知元の HintText
         ・Inspectorで割り当てるのが基本
         ・未設定の場合は OnEnable で自動探索する
           （HintTextが複数あるシーンでは意図しない参照になる可能性がある）
    */
    [Header("ヒント管理 (同じシーンの HintText を割り当てる)")]
    public HintText hint;

    //================
    // 台詞表示（UI）
    //================

    /*
         つぶやきを一時的に表示するUI
         ・発火した瞬間にONにして line を表示
         ・visibleSeconds 秒後にOFFにする
    */
    [Header("一時的に表示するテキストUI")]
    public TextMeshProUGUI text;

    [Header("しゃべる内容")]
    [TextArea] public string line = "……（台詞）";

    [Header("この秒数だけ表示してから自動で消す")]
    public float visibleSeconds = 3f;

    /*
         true  : 1回だけ発火（最初の一度だけ演出したい用途）
         false : 条件を満たすたびに発火（繰り返し演出したい用途）
    */
    [Header("一度だけ発火するか")]
    public bool showOnce = true;

    /*
         「つぶやきを表示した瞬間」に外へ通知したい場合に使う
         例：
         ・次ミッションへ進める
         ・SEを鳴らす
         ・別の演出を開始する
    */
    [Header("イベント: セリフを実際に表示した瞬間呼ぶ")]
    public UnityEvent OnMutterShown;

    //================
    // 出現させるオブジェクト
    //================

    /*
         発火したタイミングで出現させたいオブジェクト群
         例：
         ・レバー
         ・スイッチ
         ・アイテム
         ・コライダー（当たり判定）
    */
    [Header("このタイミングで出現させたいオブジェクト(レバー/スイッチ等)")]
    public GameObject[] RevealObjects;

    //================
    // 内部状態
    //================

    private bool _fired = false;                             // showOnce用：一度発火したか
    private bool _revealed = false;                          // RevealObjectsをすでに出したか
    private Coroutine _showCo = null;                        // テキスト表示コルーチン

    //================
    // Unity Lifecycle
    //================

    private void Awake()
    {
        // ゲーム開始時は必ず隠す（PrefabでONでもOFFに戻す）
        HideRevealObjectsInitially();
    }

    private void Start()
    {
        // Startでももう一度隠す（他スクリプトが先にONにしても開始時点ではOFFにしたい）
        HideRevealObjectsInitially();
    }

    private void OnEnable()
    {
        //================
        // HintText参照の確保
        //================

        // hintが未設定ならシーンから探す
        if (!hint)
        {
#if UNITY_2023_1_OR_NEWER
            hint = Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            hint = FindObjectOfType<HintText>(true);
#endif
        }

        // HintText が無ければ通知を受け取れないので終了
        if (!hint)
        {
            Debug.LogWarning("[MutteringToLine] HintText が見つかりません。");
            return;
        }

        //================
        // HintTextイベント購読
        //================

        /*
             HintText の OnLineFullyRevealed は
             「ある行が全文表示された瞬間」に id を送ってくる
             ここで購読して通知を受け取る
        */
        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);

        //================
        // デバッグ：監視対象をログ表示
        //================

        for (int i = 0; i < TriggerTargets.Length; i++)
        {
            Debug.Log($"[MutteringToLine] Listen for {MakeId(TriggerTargets[i].state, TriggerTargets[i].element)}");
        }

        // 仕様：すでに開示済みなら即出しはしない（見切った瞬間だけ発火させたい用途）
    }

    private void OnDisable()
    {
        // 二重購読防止のため解除
        if (hint)
        {
            hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
    }

    //================
    // HintTextからの通知受け取り
    //================

    /*
         HintTextから呼ばれる
         id は "stateX.elementY"
         例: "state2.element0"
    */
    private void OnHintLineFullyRevealed(string id)
    {
        // showOnce=true で既に発火済みなら無視
        if (_fired && showOnce) return;

        // 監視対象に一致したら発火
        if (DoesMatchAnyTarget(id)) ShowNow();
    }

    //================
    // TriggerTargets判定
    //================

    /*
         TriggerTargets のどれかに一致するか（OR条件）
         ・一致したら true
         ・一致しなければ false
    */
    private bool DoesMatchAnyTarget(string id)
    {
        for (int i = 0; i < TriggerTargets.Length; i++)
        {
            string want = MakeId(TriggerTargets[i].state, TriggerTargets[i].element);
            if (id == want) return true;
        }
        return false;
    }

    //================
    // 発火処理（ここがメイン）
    //================

    /*
         発火した瞬間に行う処理
         1) 台詞テキストを表示（一定秒で消す）
         2) RevealObjects を出現（1回だけ）
         3) OnMutterShown を通知
    */
    private void ShowNow()
    {
        //================
        // 1) 台詞テキスト表示
        //================

        if (text)
        {
            // 既に表示中なら止めて、今の発火内容で出し直す
            if (_showCo != null)
            {
                StopCoroutine(_showCo);
                _showCo = null;
            }
            _showCo = StartCoroutine(CoShowTemp());
        }
        else
        {
            // textが無くても「ギミック解放」はしたいので処理を続行する
            Debug.LogWarning("[MutteringToLine] text 未設定。テキストは出せないけどオブジェクトは出します。");
        }

        //================
        // 2) オブジェクト出現
        //================

        RevealObjectsOnce();

        //================
        // 3) 外部通知
        //================

        OnMutterShown?.Invoke();

        // 1回きり制御
        _fired = true;

        Debug.Log("[MutteringToLine] ShowNow() 発火");
    }

    //================
    // 台詞テキスト表示（一定秒で消す）
    //================

    // visibleSeconds 秒だけ表示して、その後消す
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

    //================
    // RevealObjects：ゲーム開始時は必ず隠す
    //================

    /*
         RevealObjects を初期状態で必ずOFFにする
         ・Awake/Startの両方から呼ぶ
    */
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

    //================
    // RevealObjects：一度だけ出す
    //================

    /*
         多重発火しても一度しか出さない
         ・最初の一度だけ SetActive(true)
    */
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

    //================
    // id生成（HintTextと同じ形式）
    //================

    /*
         TriggerTargets(state, element) から
         HintText通知と同じ "stateX.elementY" を作る
    */
    private static string MakeId(int state, int element)
    {
        return $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
    }
}