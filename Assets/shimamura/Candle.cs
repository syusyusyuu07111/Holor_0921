using UnityEngine;

public class Candle : MonoBehaviour
{
    [SerializeField] private string _candleID;

    private bool _isPutOut = false;
    /// <summary>
    /// 蝋燭を消す
    /// </summary>
    public void CandlePutOut()//これをプレイヤーで呼べばよき
    {
        if (_isPutOut) return;
        Debug.Log("キャンドルはよばれた");
        _isPutOut = true;

        foreach(var paintings in Painting.PaintingAll)
        {
            if (paintings.CompareTag(_candleID))
            {
                paintings.Drop();
                Debug.Log("絵が落ちたよ！");
            }
        }
    }
}
