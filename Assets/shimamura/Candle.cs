using UnityEngine;

public class Candle : MonoBehaviour
{
    [SerializeField] private string _candleID;

    private bool _isPutOut = false;
    /// <summary>
    /// ˜XC‚ğÁ‚·
    /// </summary>
    public void CandlePutOut()//‚±‚ê‚ğƒvƒŒƒCƒ„[‚ÅŒÄ‚×‚Î‚æ‚«
    {
        if (!_isPutOut) return;

        _isPutOut = true;

        foreach(var paintings in Painting.PaintingAll)
        {
            if (paintings.CompareTag(_candleID))
            {
                paintings.Drop();
                Debug.Log("ŠG‚ª—‚¿‚½‚æI");
            }
        }
    }
}
