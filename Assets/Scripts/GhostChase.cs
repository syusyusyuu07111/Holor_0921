using UnityEngine;

// ======================================================
// GhostChase
//
// 役割:
// プレイヤーを常に追いかけるシンプルな追跡AI
//
// 特徴:
// ・毎フレームプレイヤー方向へ移動する
// ・Y座標は固定し、水平面(XZ)のみで追う
// ・NavMeshなどは使わない直線追跡
//
// 動きの流れ:
// 1 Awakeでプレイヤーを一度だけ探してキャッシュ
// 2 Updateでプレイヤー方向を計算
// 3 正規化した方向ベクトル × speed で移動
// ======================================================
public class GhostChase : MonoBehaviour
{
    // 1秒あたりの移動速度
    public float speed = 5f;

    // プレイヤー参照を保持しておく
    // 毎フレームFindしないためのキャッシュ
    Transform player;

    // ==================================================
    // Awake
    //
    // シーン開始時に一度だけプレイヤーを探す
    // タグ "Player" が付いたオブジェクトを取得する
    // ==================================================
    void Awake()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null)
        {
            player = go.transform;
        }
    }

    // ==================================================
    // Update
    //
    // 毎フレーム実行される追跡処理
    //
    // やっていること:
    // 1 プレイヤーのXZ座標だけを取得（Yは自分を維持）
    // 2 自分 → プレイヤー方向ベクトルを作る
    // 3 正規化して「方向だけ」にする
    // 4 speedとdeltaTimeを掛けて移動
    // ==================================================
    void Update()
    {
        if (!player) return;

        // プレイヤーの高さは無視し、自分のYを維持する
        // これにより幽霊は上下に動かず水平追跡になる
        Vector3 target = new Vector3(
            player.position.x,
            transform.position.y,
            player.position.z
        );

        // 現在位置 → 目標位置 の方向ベクトル
        // normalizedにすることで長さ1の方向だけを取得
        Vector3 dir = (target - transform.position).normalized;

        // 方向 × speed × フレーム時間 で移動
        // Time.deltaTimeを掛けることでフレームレート依存を防ぐ
        transform.position += dir * speed * Time.deltaTime;
    }
}