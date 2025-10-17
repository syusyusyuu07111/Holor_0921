using UnityEngine;
using UnityEngine.InputSystem;

public class PushingAction: MonoBehaviour
{
       [Header("Push Settings")]
    [SerializeField] private float pushDistance = 2f;      // 押せる距離
    [SerializeField] private float pushForce = 5f;         // 押す力
    [SerializeField] private LayerMask pushableLayer;      // 押せるオブジェクトのレイヤー

    [Header("Reference")]
    [SerializeField] private Transform playerCamera;       // プレイヤーの視点（正面方向にRayを飛ばす）


    private void Update()
    {
        // Input Systemの "Push" アクションが押されているかをチェック
        if (Input.GetKey(KeyCode.F))
        {
            TryPushObject();
        }
    }

    /// <summary>
    /// プレイヤーが正面を向いたオブジェクトを押す処理
    /// </summary>
    private void TryPushObject()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit hit;

        // 正面にRayを飛ばして押せるオブジェクトを検出
        if (Physics.Raycast(ray, out hit, pushDistance, pushableLayer))
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb != null)
            {
                // Rayが当たった面の法線方向に押す
                // （つまりプレイヤーがどの方向から当たったかで押す方向が決まる）
                Vector3 pushDir = hit.normal * -1f; // 法線の逆方向（=プレイヤーから見て前方向）
                rb.AddForce(pushDir * pushForce, ForceMode.Impulse);

                Debug.Log($"'{hit.collider.name}' を {pushDir} 方向に押した！");
            }
            else
            {
                Debug.Log("Rigidbody が無いので押せません。");
            }
        }

        //    Transform target = hit.transform;

        //    // プレイヤーの向きとオブジェクトの正面がある程度一致しているか判定
        //    float angle = Vector3.Angle(playerCamera.forward, target.forward);

        //    if (angle < 45f) // 正面に近ければ押せる
        //    {
        //        Rigidbody rb = target.GetComponent<Rigidbody>();
        //        if (rb != null)
        //        {
        //            // プレイヤーの正面方向に力を加える
        //            rb.AddForce(playerCamera.forward * pushForce, ForceMode.Impulse);

        //            Debug.Log($"オブジェクト '{target.name}' を押した！");
        //        }
        //    }
        //    else
        //    {
        //        Debug.Log("オブジェクトの正面を向いていません。押せません。");
        //    }
        //}
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(playerCamera.position, playerCamera.forward * pushDistance);
        }
    }
}
