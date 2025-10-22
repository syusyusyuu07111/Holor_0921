using TMPro;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// HintText の OnLineFullyRevealed(string id) を購読して、
/// 指定した (state, element) が“全文表示になった瞬間”に台詞を出す。
/// 例の id 文字列: "state1.element0"
/// </summary>
public class MutteringToLine : MonoBehaviour
{
    [Header("出力先")]
    public TextMeshProUGUI text;                // ここに台詞を表示

    [Header("参照")]
    public HintText hint;                       // シーン上の HintText。未設定なら自動検索

    [System.Serializable]
    public class Trigger
    {
        [Min(1)] public int state = 1;         // 1 / 2 / 3
        [Range(0, 4)] public int element = 0;  // 0..4
        [TextArea] public string line = "……（台詞）";
        public bool showOnce = true;           // 一度だけ発火
        [HideInInspector] public bool _fired;  // 内部フラグ
    }

    [Header("トリガー（いくつでも追加可）")]
    public List<Trigger> triggers = new List<Trigger>();

    public enum ShowMode { Replace, Append }
    [Header("表示モード")]
    public ShowMode showMode = ShowMode.Replace;

    [Header("前後の余白（Append時のみ）")]
    public string appendSeparator = "\n";

    private void OnEnable()
    {
        // 自動参照
        if (!hint)
        {
#if UNITY_2023_1_OR_NEWER
            hint = Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            hint = FindObjectOfType<HintText>(true);
#endif
        }

        if (hint != null)
        {
            hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);
        }
        else
        {
            Debug.LogWarning("[MutteringToLine] HintText が見つかりません。イベントを購読できません。");
        }
    }

    private void OnDisable()
    {
        if (hint != null)
        {
            hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
        }
    }

    // HintText から: 例 "state1.element0"
    private void OnHintLineFullyRevealed(string id)
    {
        if (triggers == null || triggers.Count == 0) return;

        // 受け取った id と一致するトリガーを全部処理（複数一致OK）
        for (int i = 0; i < triggers.Count; i++)
        {
            var t = triggers[i];
            if (t == null) continue;

            if (t.showOnce && t._fired) continue;

            if (id == MakeId(t.state, t.element))
            {
                ShowLine(t.line);
                t._fired = true; // showOnce のときにだけ実効
            }
        }
    }

    private void ShowLine(string line)
    {
        if (!text) return;

        if (showMode == ShowMode.Replace)
        {
            text.text = line;
        }
        else // Append
        {
            if (string.IsNullOrEmpty(text.text))
                text.text = line;
            else
                text.text = text.text + appendSeparator + line;
        }
    }

    private static string MakeId(int state, int element)
        => $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";

    // ------ デバッグ/運用用ユーティリティ ------

    [ContextMenu("Reset Fired Flags")]
    public void ResetFiredFlags()
    {
        if (triggers == null) return;
        for (int i = 0; i < triggers.Count; i++)
            if (triggers[i] != null) triggers[i]._fired = false;
    }

    /// <summary>
    /// 手動で発火させたいとき用（テスト用）
    /// </summary>
    public void ManualTrigger(int state, int element)
    {
        OnHintLineFullyRevealed(MakeId(state, element));
    }
}
