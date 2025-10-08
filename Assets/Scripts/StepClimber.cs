using UnityEngine;

public class StepClimber : MonoBehaviour
{
    [Header("足元の参照")]
    public GameObject onesfeet;          // 足元の空オブジェクト

    [Header("設定")]
    public float rayDistance = 0.5f;     // レイの長さ
    public LayerMask hitMask = ~0;       // 当てたいレイヤー（最初は全部）
    public float raiseAmount = 0.1f;     // 当たった時に上げる量

    bool wasHitting = false;             // 前フレームでヒットしてたか

    void Update()
    {
        if (!onesfeet)
        {
            Debug.LogWarning("[StepClimber] onesfeet 未設定");
            return;
        }

        // 足元から前方（水平）にレイ
        Vector3 origin = onesfeet.transform.position + Vector3.up * 0.02f;
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        Ray ray = new Ray(origin, fwd);

        Debug.DrawRay(origin, fwd * rayDistance, Color.yellow);

        bool hitting = Physics.Raycast(ray, out RaycastHit hit, rayDistance, hitMask, QueryTriggerInteraction.Ignore);

        // 当たった“瞬間”だけ持ち上げる
        if (hitting && !wasHitting)
        {
            Debug.Log($"[StepClimber] Hit {hit.collider.name} → Y を +{raiseAmount}");
            transform.position += Vector3.up * raiseAmount;
        }

        wasHitting = hitting;
    }
}
