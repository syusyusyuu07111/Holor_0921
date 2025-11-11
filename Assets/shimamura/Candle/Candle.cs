using UnityEngine;

public class Candle : MonoBehaviour
{
    [SerializeField] private string _candleID;
    [SerializeField] private GameObject _candleEfect;

    private bool _isPutOut = false;
    /// <summary>
    /// 蝋燭を消す
    /// </summary>
    public void CandlePutOut()//これをプレイヤーで呼べばよき
    {
        _candleEfect.SetActive(false);//火のエフェクト削除
        if (_isPutOut) return;
        _isPutOut = true;

        foreach (var paintings in Painting.PaintingAll)
        {
            if (paintings.CompareTag(_candleID))
            {
                paintings.Drop();
            }
        }
    }
}
