using UnityEngine;

public class ActivateObjectOnHintLine : MonoBehaviour
{
    [Header("参照する HintText")]
    public HintText Hint;

    [Header("アクティブにしたいオブジェクト（LightOff付き）")]
    public GameObject TargetObject;

    [Header("トリガーとなるヒント行")]
    [Tooltip("HintText.Stages の index（0始まり）")]
    public int StageIndex = 0;

    [Tooltip("HintText の hintState（1〜4）\n1:1部屋目 state1, 2:1部屋目 state2, 3:2部屋目 state1, 4:2部屋目 state2")]
    public int HintState = 1;

    [Tooltip("その State の何行目か（0〜4）")]
    public int LineIndex = 0;

    private string _targetId;

    private void Awake()
    {
        // ターゲットIDを事前計算
        _targetId = $"stage{StageIndex}.state{Mathf.Max(1, HintState)}.element{Mathf.Clamp(LineIndex, 0, 4)}";
    }

    private void OnEnable()
    {
        if (Hint != null)
        {
            Hint.OnLineFullyRevealed.AddListener(OnHintLineRevealed);
        }
    }

    private void OnDisable()
    {
        if (Hint != null)
        {
            Hint.OnLineFullyRevealed.RemoveListener(OnHintLineRevealed);
        }
    }

    private void OnHintLineRevealed(string id)
    {
        // 指定した行IDと一致したらターゲットをアクティブ化
        if (id == _targetId && TargetObject != null && !TargetObject.activeSelf)
        {
            TargetObject.SetActive(true);
            Debug.Log($"[ActivateObjectOnHintLine] Activated {TargetObject.name} by hint: {id}");
        }
    }
}
