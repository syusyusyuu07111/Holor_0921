using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class StepMove : MonoBehaviour
{
    [Header("Stairs判定")]
    [SerializeField] string stairsTag = "Stairs"; // ← 階段コライダーにこのTagを付ける
    [SerializeField] LayerMask collisionMask = ~0; // 天井チェック用（地形のレイヤーを入れる）

    [Header("持ち上げパラメータ")]
    [SerializeField] float maxStepHeight = 0.45f; // 1回の最大上昇量（登りたい段差より少し上）
    [SerializeField] float liftPerFrame = 0.06f;  // 1フレームの上昇量（ガタつき抑え）
    [SerializeField] float wallDotThreshold = 0.5f;
    // これ以下なら「ほぼ垂直面に正対」＝段差/壁扱い（Dot(法線, Up) ≈ 0）

    CharacterController cc;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Tagが違う or すでに上方向へ動いてるときは無視
        if (!hit.collider || !hit.collider.CompareTag(stairsTag)) return;

        // “段差っぽい”面だけ（上向きの斜面は除外）
        // 法線が水平寄り（Upと直交に近い）＝壁/段鼻
        float dotUp = Vector3.Dot(hit.normal, Vector3.up);
        if (dotUp > wallDotThreshold) return;

        // 水平にぶつかっている/押し付けている時だけ（下向き衝突ではない）
        if (hit.moveDirection.y > 0.05f) return;

        // このフレームで持ち上げる量を決定（安全側で小刻みに）
        float lift = Mathf.Min(liftPerFrame, maxStepHeight);
        if (lift <= 0f) return;

        // 頭上スペース確認（天井にめり込まないように）
        if (!CanRise(lift)) return;

        // 実際にちょい上げ（横の移動はあなたのPlayerControllerがやる）
        cc.Move(Vector3.up * lift);
    }

    // --- ここから補助：CC寸法で頭上クリアを確認 ---
    bool CanRise(float upAmount)
    {
        // CharacterControllerのカプセル端点をワールドで計算
        Vector3 centerWorld = transform.TransformPoint(cc.center);
        float r = cc.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float halfH = Mathf.Max((cc.height * transform.lossyScale.y) * 0.5f, r + 0.01f);

        Vector3 pTop = centerWorld + Vector3.up * (halfH - r);
        Vector3 pBot = centerWorld - Vector3.up * (halfH - r);

        Vector3 up = Vector3.up * (upAmount + 0.02f); // 少しだけ余白
        bool blocked = Physics.CheckCapsule(pTop + up, pBot + up, r, collisionMask, QueryTriggerInteraction.Ignore);
        return !blocked;
    }
}
