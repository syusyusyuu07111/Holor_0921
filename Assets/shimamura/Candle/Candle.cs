using UnityEngine;
using CriWare;

public class Candle : MonoBehaviour
{
    [SerializeField] private string _candleID;
    [SerializeField] private GameObject _candleEfect;

    [Header("あたりならチェックつける　当たりのときは正解＝絵が落ちる")]
    [SerializeField] private bool _isCorrectCandle = true;

    [Header("SE設定")]
    [SerializeField] private CriAtomSource _putOutSeSource; // ★ 火を消すときのSE

    private bool _isPutOut = false;

    /// <summary>
    /// 蝋燭を消す
    /// </summary>
    public void CandlePutOut() // これをプレイヤーで呼べばよき
    {
        // すでに消えていたら何もしない
        if (_isPutOut) return;

        _isPutOut = true;

        // 火のエフェクト削除
        if (_candleEfect != null)
        {
            _candleEfect.SetActive(false);
        }

        // ★ SE 再生
        if (_putOutSeSource != null)
        {
            _putOutSeSource.Play();
        }

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

    // 別スクリプトから参照したい場合
    public bool IsPutOut { get { return _isPutOut; } }
    public bool IsCorrectCandle { get { return _isCorrectCandle; } }
}
