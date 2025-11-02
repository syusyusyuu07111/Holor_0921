using UnityEngine;

/// <summary>
/// ƒvƒŒƒCƒ„[‚ª˜XC‚ğÁ‚·‘€ì‚ğs‚¤ƒXƒNƒŠƒvƒg
/// </summary>
public class CandleInteraction : MonoBehaviour
{
    [SerializeField] private float _interactRange = 2f;    // ˜XC‚ğÁ‚¹‚é‹——£
    [SerializeField] private LayerMask _candleLayer;       // ˜XC‚ÌƒŒƒCƒ„[

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G)) // FƒL[‚Å˜XC‚ğÁ‚·
        {
            TryPutOutCandle();
        }
    }

    private void TryPutOutCandle()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _interactRange, _candleLayer))
        {
            Candle candle = hit.collider.GetComponent<Candle>();
            if (candle != null)
            {
                candle.CandlePutOut();
                Debug.Log("˜XC‚ğÁ‚µ‚½I");
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * _interactRange);
    }
}
