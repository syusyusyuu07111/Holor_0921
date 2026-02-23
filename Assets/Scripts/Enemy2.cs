using UnityEngine;

public class Enemy2 : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _playerBaseSpeed = 5f; // プレイヤー基準速度（計算用）
    [SerializeField] private float _lifetime = 10f;       // 生成されてから消えるまでの時間（秒）
    [SerializeField] private float _rotationSpeed = 180f; // 旋回速度（度/秒）
    [SerializeField] private float _enemySpeed = 1.3f;    // エネミー速度倍率（プレイヤー基準×倍率）
    [SerializeField] private float _rayDistance = 1.5f;   // 前方レイの検知距離（障害物検知用）

    private Rigidbody _rb;

    private float _speed;               // 実際に使う移動速度（playerBaseSpeed×enemySpeed）
    private float _timer;               // 寿命カウント（0になったら消える）

    private bool _isRotation = false;   // trueの間は旋回中（前進しない）
    private int _turnDirection = 1;     // -1=左, 1=右（回避方向）
    private Quaternion _targetRot;      // 旋回先の角度

    private void Start()
    {
        //================
        // Rigidbody 取得
        //================
        _rb = GetComponent<Rigidbody>();

        //================
        // 移動速度の確定
        // プレイヤー基準速度 × エネミー倍率
        //================
        _speed = _playerBaseSpeed * _enemySpeed;

        //================
        // 寿命タイマー初期化
        //================
        _timer = _lifetime;
    }

    private void Update()
    {
        //================
        // 寿命の減算
        // 0以下になったら自身を破棄する
        //================
        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        //================
        // 旋回中は「回転だけ」やる（前進しない）
        //================
        if (_isRotation)
        {
            // 現在角度 → 目標角度へ、一定速度で回す
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                _targetRot,
                _rotationSpeed * Time.fixedDeltaTime
            );

            // 目標角度にほぼ到達したら旋回終了
            if (Quaternion.Angle(transform.rotation, _targetRot) < 1f) _isRotation = false;

            return;
        }

        //================
        // 前方にレイを飛ばして障害物を検知する
        //================
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, _rayDistance))
        {
            //================
            // 家具に当たりそうなら、左右ランダムに回避方向を決める
            //================
            if (hit.collider.CompareTag("Furniture"))
            {
                // 左右ランダム（0なら右、1なら左）
                _turnDirection = Random.Range(0, 2) == 0 ? 1 : -1;

                /*
                    回避方向の目標角度を作る

                    ・transform.forward と Vector3.up の外積で「横方向」を作る
                    ・それに turnDirection（左/右）を掛けて左右を切り替える
                    ・LookRotation で「その方向を向く回転」にする
                */
                _targetRot = Quaternion.LookRotation(
                    Vector3.Cross(transform.forward, Vector3.up).normalized * _turnDirection,
                    Vector3.up
                );
            }

            //================
            // 回避行動に入る（旋回中フラグON）
            //================
            _isRotation = true;
            return;
        }

        //================
        // 障害物が無いときは前進し続ける
        //================
        _rb.MovePosition(_rb.position + transform.forward * _speed * Time.fixedDeltaTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        //================
        // 衝突したら進行方向を反射させて向きを変える
        //================
        Vector3 reflectDir = Vector3.Reflect(transform.forward, collision.contacts[0].normal);
        transform.rotation = Quaternion.LookRotation(reflectDir, Vector3.up);
    }
}