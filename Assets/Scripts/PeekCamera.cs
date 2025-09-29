using UnityEngine;

public class PeekCamera : MonoBehaviour
{
    public Transform Camera;
    public Transform Player;
    public Transform[] MovePositions;
    private Transform Selectposition;
    InputSystem_Actions input;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }

    void Update()
        //覗く機能　プレイヤーと近いpivotにカメラをよらせる===============================================================

    {
        //プレイヤーと近いpivotを探す---------------------------------------------------------------------------------------
        Transform nearest = Nearest(Player.transform.position, MovePositions);

        Debug.Log($"DBG cam:{Camera} player:{Player} pivots:{MovePositions?.Length} nearest:{nearest} openText:{OpenText.instance}");

        //プレイヤーに一番近いpivotにカメラを移動させる---------------------------------------------------------------------
        if (OpenText.instance.CanOpen&&input.Player.Interact.triggered)
        {
            Camera.transform.position = nearest.transform.position;
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
            Vector3 pos = transformpivot.position;
            float distance = (transformpivot.position - fromPos).sqrMagnitude;
            if(distance<best)
            {
                best = distance;
                bestpos = transformpivot;
            }
        }
        return bestpos;
        //------------------------------------------------------------------------------------------------------------------
    }
}
