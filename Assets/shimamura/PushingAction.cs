using UnityEngine;
using UnityEngine.InputSystem;

public class PushingAction: MonoBehaviour
{
    [SerializeField] private float _rayDistance = 1.5f;       // 家具を検出する距離
    [SerializeField] private LayerMask _pushableLayer;        // 押せる家具のレイヤー
    [SerializeField] private Transform _holdPoint;            // 家具をくっつける位置（プレイヤーの前方）
    [SerializeField] private float _psuhSpeed = 2f;
    private bool _isPushing = false;          // 押している状態かどうか
    private Transform _pushingObj;            // 押している家具の Transform
    private Quaternion _lockedRotation;     // 押している間の固定された回転角度
    private Transform _tr;
    private PlayerInput _inputAction;

    private void Start()
    {
        _tr = GetComponent<Transform>();
        _inputAction = GetComponent<PlayerInput>();
    }

    void Update()
    {
        // pushキーの検出
        if (_inputAction.actions["Push"].WasPressedThisFrame() && !_isPushing)
        {
            // 前方に家具があるかRaycastで確認
            if (Physics.Raycast(_tr.position, _tr.forward, out RaycastHit hit, _rayDistance, _pushableLayer))
            {
                // 家具のtransformを取得
                _pushingObj = hit.collider.transform;

                // 家具をプレイヤーの子にして holdPoint に移動
                _pushingObj.SetParent(_holdPoint);
                _pushingObj.localPosition = Vector3.zero;
                //_pushingObj.localRotation = Quaternion.identity; //回転のリセット

                //プレイヤー回転を固定
                _lockedRotation = _tr.rotation;

                _isPushing = true;
            }
        }

        // キーを話したら
        if (_inputAction.actions["Push"].WasCompletedThisFrame() && _isPushing)
        {

            // プレイヤーの子オブジェクトから外す
            _pushingObj.SetParent(null);

            _isPushing = false;
            _pushingObj = null;
        }

        // 押してる間は前方向入力のみ許可
        if (_isPushing)
        {
            // 回転をロック（押してる間は向きを変えない）
            _tr.rotation = _lockedRotation;
            if (_inputAction.actions["Push"].IsPressed())
            {
                _tr.position += _tr.forward * Time.deltaTime * _psuhSpeed;// 押してる時のプレイヤー前進（要変更）
            }
        }
    }

    void OnDrawGizmosSelected()//シーンでレイを可視化
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position,transform.forward * _rayDistance);
    }
}
