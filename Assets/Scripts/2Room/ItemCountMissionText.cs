using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class ItemCountMissionText : MonoBehaviour
{
    [Header("参照元")]
    [SerializeField] private ItemPickupController2 itemPickup; // 取得数を持ってるやつ

    [Header("チュートリアル連携")]
    [SerializeField] private SecondRoomTutorial secondRoomTutorial; // 全部集まったら進めたい先

    [Header("ミッションUI")]
    [SerializeField] private TextMeshProUGUI missionText;      // 0/5 を表示するテキスト
    [SerializeField] private int targetCount = 5;              // 目標個数（5とか）
    [SerializeField]
    private string format =
        "ミッション：本を調べて情報をあつめよう　{0}/{1}";

    [Header("全部集まったときのイベント")]
    public UnityEvent OnAllCollected; // 必要ならインスペクター側でもフックできるように残しておく

    private int lastCount = -1;
    private bool completed = false;

    private void Update()
    {
        if (itemPickup == null || missionText == null) return;

        // ItemPickupController2 が持っている取得数
        int current = itemPickup.CollectedCount;

        // 表示上は targetCount を上限にしておく（6/5 みたいにならないように）
        int clamped = Mathf.Min(current, targetCount);

        // 数字が変わったときだけテキスト更新
        if (clamped != lastCount)
        {
            lastCount = clamped;
            missionText.text = string.Format(format, clamped, targetCount);
        }

        // まだ完了していなくて、目標数以上なら「全部集まった」と扱う
        if (!completed && current >= targetCount)
        {
            completed = true;

            // ★ コード上から「次のチュートリアルへ」をはっきり呼ぶ
            if (secondRoomTutorial != null)
            {
                secondRoomTutorial.GoToTutorial2();
            }
            else
            {
                Debug.LogWarning("ItemCountMissionText: secondRoomTutorial が設定されていません。");
            }

            // ★ もともとの UnityEvent も残しておく（必要ならインスペクターで追加処理も可）
            OnAllCollected?.Invoke();
        }
    }
}
