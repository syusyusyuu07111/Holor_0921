using UnityEngine;
using CriWare; // CRIを使うときの名前空間（プロジェクトによっては不要なこともあります）

public class CandleMissEvent : MonoBehaviour
{
    [Header("外れのときの処理に使うCandle")]
    [SerializeField] private Candle _candle;

    [Header("プレイヤーと幽霊生成設定")]
    [SerializeField] private Transform _player;          // プレイヤー
    [SerializeField] private GameObject _ghostPrefab;    // 生成する幽霊プレハブ
    [SerializeField] private float _spawnRadius = 3f;    // プレイヤー周囲の生成半径
    [SerializeField] private float _spawnHeightOffset = 0f; // 高さの補正

    [Header("SE再生（CRI AtomSource）")]
    [SerializeField] private CriAtomSource _atomSource;  // ここに同じオブジェクトのAtomSourceをアタッチ
    [SerializeField] private string _cueName;            // キュー名を指定して鳴らしたい場合用（空ならデフォルト）

    private bool _isPlayed = false; // 一度だけ処理するためのフラグ

    /// <summary>
    /// 外れのときの処理をチェックする
    /// </summary>
    void Update()
    {
        if (_candle == null) return;
        if (_isPlayed) return;

        // 「ろうそくが消されている」かつ「外れろうそく」のとき
        if (_candle.IsPutOut && !_candle.IsCorrectCandle)
        {
            _isPlayed = true;
            MissEvent();
        }
    }

    /// <summary>
    /// 外れたときに起きる処理を書く
    /// </summary>
    private void MissEvent()
    {
        //プレイヤーの周りに幽霊を5体生成する
        if (_player == null)
        {
            Debug.LogWarning("CandleMissEvent：プレイヤーの参照が設定されていません。");
            return;
        }

        if (_ghostPrefab == null)
        {
            Debug.LogWarning("CandleMissEvent：幽霊プレハブが設定されていません。");
            return;
        }

        for (int i = 0; i < 5; i++)
        {
            Vector3 spawnPos = GetRandomPositionAroundPlayer();
            Instantiate(_ghostPrefab, spawnPos, Quaternion.identity);
        }

        //幽霊が出現した時のSE再生する（CRI AtomSourceを使用）
        PlayGhostSpawnSE();

        Debug.Log("外れろうそく：ここで外れ演出を再生する");
    }

    /// <summary>
    /// プレイヤーの周りのランダムな位置を取得する
    /// </summary>
    private Vector3 GetRandomPositionAroundPlayer()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float x = Mathf.Cos(angle) * _spawnRadius;
        float z = Mathf.Sin(angle) * _spawnRadius;

        Vector3 basePos = _player.position;

        return new Vector3(
            basePos.x + x,
            basePos.y + _spawnHeightOffset,
            basePos.z + z
        );
    }

    /// <summary>
    /// CRIのAtomSourceで幽霊SEを再生する
    /// </summary>
    private void PlayGhostSpawnSE()
    {
        if (_atomSource == null)
        {
            Debug.LogWarning("CandleMissEvent：AtomSourceが設定されていないため幽霊SEが鳴りません。");
            return;
        }

        // ① インスペクタでデフォルトキューを設定している場合
        if (string.IsNullOrEmpty(_cueName))
        {
            _atomSource.Play(); // AtomSource側に設定しているキューを再生
        }
        else
        {
            // ② スクリプトからキュー名を指定したい場合
            _atomSource.Play(_cueName);
        }
    }
}