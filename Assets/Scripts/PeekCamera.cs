using UnityEngine;

/*
     このスクリプトがやること

     1) プレイヤーがドア前など「覗ける場所」にいる時だけ
        Interact入力でカメラを覗き用ポジションへ移動させる

     2) 覗き開始時：
        ・現在のカメラ位置を savepos に保存する
        ・TPSCamera の操作を止める（tps.ControlEnable = false）
        ・MovePositions の中から「プレイヤーに一番近い覗き位置」を選び
          その位置に Camera.position を移動する
        ・IsPeeking = true にする

     3) 覗き中にもう一度 Interact入力が入ったら解除する
        ・Camera.position を savepos に戻す
        ・TPSCamera の操作を戻す（tps.ControlEnable = true）
        ・IsPeeking = false にする

     4) 「今この場所で覗けるかどうか」は openText.CanOpen を参照する
        ・OpenText は「距離が近いなら CanOpen = true」を作るスクリプト
        ・PeekCamera は OpenText.instance みたいな静的参照を使わず
          Inspectorで割り当てた openText を見る
*/

public class PeekCamera : MonoBehaviour
{
    //================
    // References
    //================
    public Transform Camera;                      // 動かすカメラTransform
    public Transform Player;                      // 距離判定に使うプレイヤーTransform
    public Transform[] MovePositions;             // 覗きカメラの候補位置
    public TPSCamera tps;                         // TPSカメラ制御（操作停止に使う）

    [SerializeField] OpenText openText;           // この場所のOpenText（距離OKなら CanOpen=true）
    public bool IsPeeking { get; private set; }   // 今覗き中かどうか（外から読める）

    //================
    // Internal
    //================
    InputSystem_Actions input;                    // 新InputSystem
    Vector3 savepos;                              // 覗く前のカメラ位置退避

    //================
    // Unity Lifecycle
    //================
    void Awake()
    {
        input = new InputSystem_Actions();
    }

    void OnEnable()
    {
        input.Player.Enable();
    }

    void Update()
    {
        //================
        // 覗き位置の選択（プレイヤーに最も近いpivot）
        //================
        Transform nearest = Nearest(Player.position, MovePositions);

        //================
        // 「この場所で覗けるか」の判定
        //================
        bool canOpenHere = openText != null && openText.CanOpen;

        //================
        // 覗き開始
        //================
        if (!IsPeeking && canOpenHere && input.Player.Interact.triggered)
        {
            // 覗く前の位置を保存
            savepos = Camera.position;

            // TPS操作を止める（カメラが勝手に動かないように）
            tps.ControlEnable = false;

            // 一番近い覗き位置へ移動
            if (nearest) Camera.position = nearest.position;

            IsPeeking = true;
        }
        //================
        // 覗き解除
        //================
        else if (IsPeeking && input.Player.Interact.triggered)
        {
            IsPeeking = false;

            // 保存しておいた位置へ戻す
            Camera.position = savepos;

            // TPS操作を戻す
            tps.ControlEnable = true;
        }
    }

    //================
    // Nearest
    //================
    /*
         fromPos から一番近い Transform を返す
         ・距離比較は sqrMagnitude を使う（sqrtしない）
         ・nullは無視する
    */
    Transform Nearest(Vector3 fromPos, Transform[] pivots)
    {
        float best = float.PositiveInfinity;
        Transform bestpos = null;

        foreach (var p in pivots)
        {
            if (!p) continue;

            float d = (p.position - fromPos).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestpos = p;
            }
        }

        return bestpos;
    }
}