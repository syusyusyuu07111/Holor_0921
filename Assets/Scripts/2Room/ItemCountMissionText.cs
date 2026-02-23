using TMPro;
using UnityEngine;
using UnityEngine.Events;

/*
このスクリプトは、
アイテムの取得数をミッションUIに表示し、
目標数に達したときに次のチュートリアルへ進めるためのものです。

やっていることは次の3つです。

1. 現在の取得数を「0/5」の形式でミッションテキストに表示する
2. 取得数が変わったときだけUIを更新する
3. 目標数に達したら一度だけ次のチュートリアルを開始する
*/
public class ItemCountMissionText : MonoBehaviour
{
    [Header("参照元")]
    [SerializeField] private ItemPickupController2 itemPickup;
    // 現在の取得数を持っているスクリプト

    [Header("チュートリアル連携")]
    [SerializeField] private SecondRoomTutorial secondRoomTutorial;
    // 全部集まったら呼び出す先のチュートリアル

    [Header("ミッションUI")]
    [SerializeField] private TextMeshProUGUI missionText;
    // 「0/5」の表示を行うテキスト

    [SerializeField] private int targetCount = 5;
    // 目標個数

    [SerializeField]
    private string format =
        "ミッション：本を調べて情報をあつめよう　{0}/{1}";
    // 表示フォーマット
    // {0} に現在数、{1} に目標数が入る

    [Header("全部集まったときのイベント")]
    public UnityEvent OnAllCollected;
    // 目標達成時に追加処理を設定できるイベント

    private int lastCount = -1;
    // 前回表示した数値
    // 数字が変わったときだけUI更新するために保持している

    private bool completed = false;
    // 目標達成処理を一度だけ実行するためのフラグ

    private void Update()
    {
        // 必要な参照がなければ処理しない
        if (itemPickup == null || missionText == null) return;

        // 現在の取得数を取得
        int current = itemPickup.CollectedCount;

        // 表示上は目標数を超えないようにする
        int clamped = Mathf.Min(current, targetCount);

        // 取得数が変わったときだけテキストを更新する
        if (clamped != lastCount)
        {
            lastCount = clamped;
            missionText.text = string.Format(format, clamped, targetCount);
        }

        // まだ完了しておらず、目標数以上なら達成扱いにする
        if (!completed && current >= targetCount)
        {
            completed = true;

            // 次のチュートリアルへ進める
            if (secondRoomTutorial != null)
            {
                secondRoomTutorial.GoToTutorial2();
            }
            else
            {
                Debug.LogWarning("ItemCountMissionText: secondRoomTutorial が設定されていません。");
            }

            // インスペクターで設定された追加イベントも実行
            OnAllCollected?.Invoke();
        }
    }
}