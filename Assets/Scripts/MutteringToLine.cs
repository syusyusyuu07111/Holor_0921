using TMPro;
using UnityEngine;

/// <summary>
/// 「stateX の elementY が “全文表示された瞬間” に台詞を出す」
/// HintText の OnLineFullyRevealed(string id)（例: "state1.element0"）を購読して実現。
/// 後から有効化されても、既に開示済みなら即座に表示（後追い補正）。
/// </summary>
public class MutteringToLine : MonoBehaviour
{
    [Header("出力先")]
    public TextMeshProUGUI text;

    [Header("参照")]
    public HintText hint;  // Inspector で Tutorial と同じ HintText を割り当て推奨

    [Header("トリガ条件")]
    [Min(1)] public int targetState = 1;   // 1 / 2 / 3
    [Range(0, 4)] public int targetElement = 0;

    [Header("表示する台詞")]
    [TextArea] public string line = "……（台詞）";

    [Header("一度だけ表示する")]
    public bool showOnce = true;

    private bool _fired;

    private void OnEnable()
    {
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

        hint.OnLineFullyRevealed.AddListener(OnHintLineFullyRevealed);
        Debug.Log($"[MutteringToLine] Subscribed to {hint.name}. target={MakeId(targetState, targetElement)}");

        // ★後追い補正：既に開示済みなら即表示
        if (hint.HasLineBeenRevealed(targetState, targetElement))
        {
            Debug.Log("[MutteringToLine] target already revealed. show immediately.");
            ShowNow();
        }
    }

    private void OnDisable()
    {
        if (hint) hint.OnLineFullyRevealed.RemoveListener(OnHintLineFullyRevealed);
    }

    private void OnHintLineFullyRevealed(string id)
    {
        if (_fired && showOnce) return;

        string targetId = MakeId(targetState, targetElement);
        Debug.Log($"[MutteringToLine] Received id={id} (target={targetId})");
        if (id == targetId) ShowNow();
    }

    private void ShowNow()
    {
        if (text)
        {
            text.gameObject.SetActive(true);
            text.text = line;
        }
        _fired = true;
        Debug.Log("[MutteringToLine] SHOWN");
    }

    private static string MakeId(int state, int element)
        => $"state{Mathf.Max(1, state)}.element{Mathf.Clamp(element, 0, 4)}";
}
