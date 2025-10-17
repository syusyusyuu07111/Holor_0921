using UnityEngine;
using UnityEngine.InputSystem;

public class push : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // 家具を検出する距離
    [SerializeField] private float _pushForce = 2f;         // 押す力（小さめにしてスライド感）
    [SerializeField] private LayerMask _LayerPositoin;      // 押せるオブジェクトのレイヤー

    [Header("References")]
    [SerializeField] private Transform _playerCamera;       // プレイヤーの視点（カメラ）
    [SerializeField] private float _alignAngleLimit = 60f;  // 押せる角度の範囲

    private Rigidbody _pushingRb = null;                    // 押しているオブジェクト
    private Vector3 _pushDirection;                         // 押す方向

    private void Update()
    {
        // Rayで正面のオブジェクトをチェック
        Ray ray = new Ray(_playerCamera.position, _playerCamera.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            // Eキーを押し続けている間
            if (Input.GetKey(KeyCode.F))
            {
                // 押しているオブジェクトを取得
                if (_pushingRb == null)
                {
                    _pushingRb = hit.rigidbody;
                    if (_pushingRb != null)
                    {
                        // Rayが当たった面の法線ベクトルの逆方向へ押す（押した方向）
                        _pushDirection = -hit.normal;
                        Debug.Log("動いた？");
                    }
                }
            }
            else
            {
                // キーを離したら解除
                _pushingRb = null;
            }
        }
        else
        {
            // 何もヒットしていなければ解除
            _pushingRb = null;
        }
    }

    private void FixedUpdate()
    {
        // 押している間、押す力をゆっくり加える
        if (_pushingRb != null)
        {
            _pushingRb.AddForce(_pushDirection * _pushForce, ForceMode.Force);
            Debug.Log("力加わってる？");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_playerCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_playerCamera.position, _playerCamera.forward * _pushDistance);
        }
    }

}

