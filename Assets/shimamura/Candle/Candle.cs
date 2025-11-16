using UnityEngine;

public class Candle : MonoBehaviour
{
    [SerializeField] private string _candleID;
    [SerializeField] private GameObject _candleEfect;

    [Header("あたりならチェックつける　当たりのときは正解＝絵が落ちる")]
    [SerializeField] private bool _isCorrectCandle = true; // ← ここで当たり/ハズレを設定

    private bool _isPutOut = false;
    /// <summary>
    /// 蝋燭を消す
    /// </summary>
    public void CandlePutOut()//これをプレイヤーで呼べばよき
    {
        _candleEfect.SetActive(false);//火のエフェクト削除
        if (_isPutOut) return;
        _isPutOut = true;

        // ハズレならここで終了（絵は落ちない）
        if (!_isCorrectCandle) return;

        foreach (var paintings in Painting.PaintingAll)
        {
            if (paintings.CompareTag(_candleID))
            {
                paintings.Drop();
            }
        }
    }

    // 別スクリプトから参照したい場合用
    public bool IsPutOut { get { return _isPutOut; } }
    public bool IsCorrectCandle { get { return _isCorrectCandle; } }
}
