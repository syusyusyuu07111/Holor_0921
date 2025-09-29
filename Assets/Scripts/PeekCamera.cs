using UnityEngine;

public class PeekCamera : MonoBehaviour
{
    public static PeekCamera Instance;
    public Transform Camera;
    public Transform Player;
    public Transform[] MovePositions;
    private Transform Selectposition;
    InputSystem_Actions input;
    public TPSCamera tps;//カメラ制御
    public bool IsPeeking { get; set; }
    private Vector3 savepos;
    private void Awake()
    {
        Instance = this;
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }
    private void Start()
    {
        IsPeeking = false;

    }
    void Update()
    //覗く機能　プレイヤーと近いpivotにカメラをよらせる===============================================================

    {
        //プレイヤーと近いpivotを探す---------------------------------------------------------------------------------------
        Transform nearest = Nearest(Player.transform.position, MovePositions);

        //プレイヤーに一番近いpivotにカメラを移動させる---------------------------------------------------------------------
        if (IsPeeking == false && OpenText.instance.CanOpen && input.Player.Interact.triggered)
        {
            savepos = Camera.transform.position;           // 元位置を保存
            tps.ControlEnable = false;                      // TPS停止
            if (nearest != null)
            {
                Camera.transform.position = nearest.transform.position;
            }
            IsPeeking = true;                               // 覗き状態にする
        }
        //カメラ位置戻す------------------------------------------------------------------------------------------------------
        else if (IsPeeking == true && input.Player.Interact.triggered)
        {
            IsPeeking = false;
            Camera.position = savepos;                      // 戻す
            tps.ControlEnable = true;                       // TPS再開
        }
    }
    //最寄りのpivotを返す
    Transform Nearest(Vector3 fromPos, Transform[] pivots)
    {
        float best = float.PositiveInfinity;//距離わからないから最大化させて初期化しておく
        Transform bestpos = null;

        //配列の中の座標を取得する----------------------------------------------------------------------------------------
        foreach (Transform transformpivot in pivots)
        {
            if (!transformpivot) continue;                 //
            Vector3 pos = transformpivot.position;
            float distance = (transformpivot.position - fromPos).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                bestpos = transformpivot;
            }
        }
        return bestpos;
        //------------------------------------------------------------------------------------------------------------------
    }
}
