using UnityEngine;

public class StepClimber : MonoBehaviour
{
    [Header("参照")]
    public GameObject onesfeet;

    [Header("段差検知")]
    public float rayDistance = 0.5f;
    public LayerMask hitMask = ~0;
    public float footRadius = 0.03f;     // SphereCastで段鼻のバタつきを緩和

    [Header("押し上げ/前進")]
    public float raiseAmount = 0.1f;
    public float forwardNudge = 0.02f;
    public float contactEpsilon = 0.01f;

    [Header("ガタガタ抑制")]
    public float minMoveSpeed = 0.02f;   // これ未満の移動速度なら段差処理しない（停止中ガタガタ防止）
    public float stepCooldown = 0.08f;   // 押し上げ後のクールダウン
    public float snapDownDist = 0.15f;   // 地面への貼り付け距離
    public float snapDownSpeed = 0.2f;   // 1フレームあたりの最大スナップ量

    bool wasHitting = false;
    float cooldownTimer = 0f;

    Vector3 lastPos;

    void Start()
    {
        lastPos = transform.position;
    }

    void Update()
    {
        if (!onesfeet) return;

        // 実移動速度（停止中は段差処理を止めたい）
        float moveSpeed = (transform.position - lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPos = transform.position;

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        // 足元から前方（水平）にSphereCast（Rayより安定）
        Vector3 origin = onesfeet.transform.position + Vector3.up * 0.02f;
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        bool hitting;
        RaycastHit hit;
        if (footRadius > 0f)
            hitting = Physics.SphereCast(origin, footRadius, fwd, out hit, rayDistance, hitMask, QueryTriggerInteraction.Ignore);
        else
            hitting = Physics.Raycast(origin, fwd, out hit, rayDistance, hitMask, QueryTriggerInteraction.Ignore);

        // —— 段差処理: 動いている & クールダウン中でない & 今フレーム初ヒット ——
        if (hitting && !wasHitting && cooldownTimer <= 0f && moveSpeed >= minMoveSpeed)
        {
            // 上げる
            transform.position += Vector3.up * raiseAmount;

            // 前へ“ほんの少し”。安全距離でクランプ
            float maxSafe = Mathf.Max(0f, hit.distance - contactEpsilon);
            float nudge = Mathf.Min(forwardNudge, maxSafe);
            if (nudge > 0f) transform.position += fwd * nudge;

            // 連打防止
            cooldownTimer = stepCooldown;
        }

        wasHitting = hitting;

        // —— 地面への貼り付け（浮き→落ち→当たり直しのループを止める）——
        // 足元の真下に短いレイを撃って、近ければそっと下に寄せる
        if (Physics.Raycast(onesfeet.transform.position + Vector3.up * 0.02f, Vector3.down, out RaycastHit downHit, snapDownDist, hitMask, QueryTriggerInteraction.Ignore))
        {
            float wantDown = Mathf.Max(0f, (onesfeet.transform.position - downHit.point).y - 0.02f);
            if (wantDown > 0f)
            {
                float step = Mathf.Min(wantDown, snapDownSpeed * Time.deltaTime);
                transform.position += Vector3.down * step;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!onesfeet) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(onesfeet.transform.position + Vector3.up * 0.02f, footRadius);
    }
}
