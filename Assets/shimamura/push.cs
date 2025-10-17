using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using static UnityEditor.Experimental.GraphView.GraphView;

public class push : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float _pushDistance = 0.2f;      // 家具を検出する距離
    [SerializeField] private float _pushForce = 2f;         // 押す力（小さめにしてスライド感）
    [SerializeField] private LayerMask _LayerPositoin;      // 押せるオブジェクトのレイヤー

    [Header("References")]
    [SerializeField] private Transform _rayOrigin;       　　　// レイを飛ばす起点
    [SerializeField] private float _angleLimit = 45f;  // 押せる角度の範囲
    [SerializeField] private Transform _player;                // プレイヤー本体（位置を動かす対象）

    [Header("Jump Settings")]
    [SerializeField] private float _jumpDuration = 0.7f;        // 飛ぶ時間（秒）
    [SerializeField] private float _jumpHeight = 0.75f;            // 放物線の高さ

    [Header("Item Pickup Settings")]
    [SerializeField] private float _itemPickupRange = 1f;       // アイテムを拾える距離
    [SerializeField] private LayerMask _itemLayer;              // アイテムのレイヤー（例：Item）

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI _itemTextMeshPro;
    [SerializeField] private TextMeshProUGUI _pushTextMeshPro;

    private Rigidbody _pushingRb = null;                    // 押しているオブジェクト
    private Vector3 _pushDirection;                         // 押す方向

    //ジャンプ関連
    private bool _isJumping = false;                    // 飛行中フラグ
    private Vector3 _jumpStart;                                  // ジャンプ開始位置
    private Vector3 _jumpEnd;                                    // ジャンプ目標位置
    private float _jumpElapsed = 0f;                             // ジャンプ経過時間
    private bool _isOnFurniture = false;                        // 家具の上に乗っているか

    //インベントリ
    private List<GameObject> _inventory = new List<GameObject>();  // 拾ったアイテムを保存するリスト

    private void Update()
    {
        if (_isJumping)
        {
            
            UpdateJump();
            return;
        }

        if (_isOnFurniture)
        {
            CheckItemPickup();
        }


        // Rayで正面のオブジェクトをチェック
        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, _pushDistance, _LayerPositoin))
        {
            _pushTextMeshPro.SetText("Eキーで椅子に乗る\nFキーで椅子を押す");
            ChairPush(hit);
            HandleJumpInput(hit);
        }
        else
        {
            // 何もヒットしていなければ解除
            _pushingRb = null;

            _pushTextMeshPro.SetText("");
        }
    }

    /// <summary>
    /// Fキーで椅子を押す処理
    /// </summary>
    private void ChairPush(RaycastHit hit)
    {
        if (Input.GetKey(KeyCode.F))
        {
            if (_pushingRb == null)
            {
                _pushingRb = hit.rigidbody;
                if (_pushingRb != null)
                {
                    _pushDirection = -hit.normal; // 押す方向をレイの逆方向に設定
                    Debug.Log("押し対象: " + hit.transform.name);
                }
            }
        }
        else
        {
            _pushingRb = null; // Fキーを離したら解除
        }
    }

    /// <summary>
    /// 放物線ジャンプの更新処理
    /// </summary>
    private void UpdateJump()
    {
        _jumpElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_jumpElapsed / _jumpDuration); // 0→1 に正規化

        // 水平移動（Lerp）
        Vector3 horizontal = Vector3.Lerp(_jumpStart, _jumpEnd, t);

        // 垂直移動（放物線）
        float height = Mathf.Sin(t * Mathf.PI) * _jumpHeight;

        _player.position = new Vector3(horizontal.x, horizontal.y + height, horizontal.z);

        if (t >= 1f)
        {
            _isJumping = false;
            _isOnFurniture = true; // ←ここ追加
            Debug.Log("家具の上に着地完了！");
        }
    }

    /// <summary>
    /// Eキーで椅子の上に放物線ジャンプする処理
    /// </summary>
    private void HandleJumpInput(RaycastHit hit)
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Collider col = hit.collider;
            if (col == null) return;

            // オブジェクトの上面中央をジャンプ目標に設定
            Vector3 topCenter = col.bounds.center + Vector3.up * col.bounds.extents.y;
            _jumpStart = _player.position;
            _jumpEnd = topCenter + Vector3.up * 0.05f;

            _jumpElapsed = 0f;
            _isJumping = true;

            Debug.Log("放物線ジャンプ開始 → " + hit.transform.name);
        }
    }

    /// <summary>
    /// 家具の上でアイテムを取得する処理
    /// </summary>
    private void CheckItemPickup()
    {
        Collider[] items = Physics.OverlapSphere(_player.position, _itemPickupRange, _itemLayer);

        // 該当するアイテムが1つもない場合は処理をスキップ
        if (items.Length == 0)
        {
            _itemTextMeshPro.SetText("");
            return;
        }


        _itemTextMeshPro.SetText("Eキーアイテムを取る");

        // 一番近いアイテムを記録する変数
        Collider nearestItem = null;
        float minDist = Mathf.Infinity;

        // 取得した全アイテムを調べる
        foreach (var item in items)
        {
            // レイヤーが正しいか再確認（OverLapSphereの結果に他の物が混ざった場合用）
            if (((1 << item.gameObject.layer) & _itemLayer) == 0) continue;

            // 距離を計算
            float dist = Vector3.Distance(_player.position, item.transform.position);

            // より近ければ更新
            if (dist < minDist)
            {
                minDist = dist;
                nearestItem = item;
            }
        }

        //一番近いアイテムがあり、Eキーを押したら拾う
        if (nearestItem != null && Input.GetKeyDown(KeyCode.E))
        {
            _inventory.Add(nearestItem.gameObject);//インベントリ追加
            Debug.Log($"アイテム取得: {nearestItem.name}");
            Destroy(nearestItem.gameObject);
        }
    }


    private void FixedUpdate()
    {
        // 押している間、押す力をゆっくり加える
        if (_pushingRb != null)
        {
            _pushingRb.AddForce(_pushDirection * _pushForce, ForceMode.Force);//継続的に力を加える
            Debug.Log("力加わってる？");
        }
    }

    private void OnDrawGizmosSelected() //シーンでRayを可視化
    {
        if (_rayOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_rayOrigin.position, _rayOrigin.forward * _pushDistance);
        }
    }



}

