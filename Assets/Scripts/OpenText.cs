using TMPro;
using UnityEngine;

/*
     役割：
     プレイヤーがドアに近づいたときだけ
     「開ける」などのUIテキストを表示するスクリプト

     やっていること：
     ・player と Door の距離を毎フレーム測る
     ・一定距離以内ならテキストを表示
     ・距離外ならテキストを非表示
     ・現在「開ける距離にいるか？」を CanOpen で外部に公開する

     ※ 実際にドアを開ける処理はここではやらない
        あくまで「表示制御」専用
*/

public class OpenText : MonoBehaviour
{
    //================
    // Inspector参照
    //================

    [Header("表示するUIテキスト")]
    public TextMeshProUGUI opentext;     // 「Eで開ける」などのテキスト

    [Header("対象参照")]
    public Transform player;             // プレイヤー
    public Transform Door;               // ドアの位置（基準点）

    [Header("表示距離")]
    public float openDistance;           // この距離未満なら表示する

    //================
    // 外部参照用プロパティ
    //================

    /*
         現在「開ける距離内にいるか？」を外部から読めるようにする

         true  → プレイヤーは開けられる距離にいる
         false → 距離外

         set は private にしているので
         外部から勝手に変更はできない
    */
    public bool CanOpen { get; private set; }

    //================
    // 初期化
    //================

    void Start()
    {
        // ゲーム開始時は必ず非表示
        opentext.enabled = false;

        // 初期状態は「開けない」
        CanOpen = false;
    }

    //================
    // 毎フレーム処理
    //================

    void Update()
    {
        //================
        // 1) 距離を計算
        //================

        /*
             プレイヤーとドアのワールド座標の距離を測る
        */
        float dist = Vector3.Distance(player.position, Door.position);

        //================
        // 2) 表示すべきか判定
        //================

        /*
             openDistance 未満なら表示OK
        */
        bool can = (dist < openDistance);

        //================
        // 3) UI表示の切り替え
        //================

        /*
             毎フレーム enabled を代入すると無駄なので、
             状態が変わった時だけ切り替える
        */
        if (opentext.enabled != can)
            opentext.enabled = can;

        //================
        // 4) 外部参照用フラグ更新
        //================

        /*
             今の状態をプロパティに保存
             他スクリプトから
             if(openText.CanOpen) のように使える
        */
        CanOpen = can;
    }
}