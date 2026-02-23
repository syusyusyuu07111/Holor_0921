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

    private string TargetId;

    private void Awake()
    {
        //================
        // 参照チェック
        //================
        if (Hint == null) Debug.LogError("[ActivateObjectOnHintLine] Hint が未設定です。");
        if (TargetObject == null) Debug.LogError("[ActivateObjectOnHintLine] TargetObject が未設定です。");

        //================
        // ターゲットIDを事前計算
        //================
        TargetId = CreateTargetId();
    }

    private void OnEnable()
    {
        if (Hint == null) return;

        //================
        // イベント登録
        //================
        Hint.OnLineFullyRevealed.AddListener(OnHintLineRevealed);
    }

    private void OnDisable()
    {
        if (Hint == null) return;

        //================
        // イベント解除
        //================
        Hint.OnLineFullyRevealed.RemoveListener(OnHintLineRevealed);
    }

    /*
         HintText側で行が完全表示された瞬間に呼ばれる
         IDが一致したらターゲットをアクティブ化する
    */
    private void OnHintLineRevealed(string Id)
    {
        if (Id != TargetId) return;
        if (TargetObject == null)
        {
            Debug.LogError("[ActivateObjectOnHintLine] TargetObject が未設定のまま呼ばれました。");
            return;
        }
        if (TargetObject.activeSelf) return;

        //================
        // アクティブ化
        //================
        TargetObject.SetActive(true);
        Debug.Log($"[ActivateObjectOnHintLine] Activated {TargetObject.name} by hint: {Id}");
    }

    //================
    // ターゲットID生成
    //================
    private string CreateTargetId()
    {
        // 不正値が入っても挙動が壊れないように丸める
        int FixedStageIndex = Mathf.Max(0, StageIndex);
        int FixedHintState = Mathf.Clamp(HintState, 1, 4);
        int FixedLineIndex = Mathf.Clamp(LineIndex, 0, 4);

        return $"stage{FixedStageIndex}.state{FixedHintState}.element{FixedLineIndex}";
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        //================
        // インスペクター変更時の反映
        //================
        TargetId = CreateTargetId();
    }
#endif
}