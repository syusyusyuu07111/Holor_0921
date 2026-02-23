using UnityEngine;

// ======================================================
// GhostScreenOrbs
//
// 役割:
// ゴーストが出現している間だけ
// 画面前のオーブ演出(パーティクルなど)を表示する制御クラス
//
// 仕組み:
// EnemyAI の CurrentGhost を毎フレーム監視し
// null かどうかで OrbObject の表示を切り替える
//
// つまり:
// 「ゴースト出現状態」→「画面エフェクト表示」の橋渡し
// ======================================================
[DefaultExecutionOrder(10)]
public class GhostScreenOrbs : MonoBehaviour
{
    // ==================================================
    // 参照
    // ==================================================

    [Header("参照")]
    [Tooltip("ゴーストの出現状態を見る EnemyAI（CurrentGhost を参照）")]
    public EnemyAI Enemy;

    [Tooltip("最初からカメラの前に置いておくオーブ（パーティクル）のルートオブジェクト")]
    public GameObject OrbObject;

    // ==================================================
    // 起動時の初期状態
    // ==================================================

    [Header("起動時設定")]
    [Tooltip("Start 時に自動で非表示にするか")]
    public bool HideOnStart = true;

    // ==================================================
    // Start
    //
    // シーン開始時の初期化
    // 必要なら最初はオーブを非表示にする
    // ==================================================
    void Start()
    {
        if (!OrbObject)
        {
            Debug.LogWarning("[GhostScreenOrbs] OrbObject が設定されていません。");
            return;
        }

        // ゲーム開始時にオーブを隠す設定ならOFFにする
        if (HideOnStart)
        {
            OrbObject.SetActive(false);
        }
    }

    // ==================================================
    // Update
    //
    // 毎フレーム、ゴーストの存在を確認する
    //
    // CurrentGhost が null でなければ
    //   → ゴーストが存在している
    //   → オーブ表示ON
    //
    // null なら
    //   → ゴースト不在
    //   → オーブ非表示
    // ==================================================
    void Update()
    {
        if (!Enemy || !OrbObject) return;

        // ゴーストが存在するかどうか
        bool shouldShow = (Enemy.CurrentGhost != null);

        // すでに状態が同じなら何もしない
        // 状態が違うときだけ SetActive する（無駄な切り替え防止）
        if (OrbObject.activeSelf != shouldShow)
        {
            OrbObject.SetActive(shouldShow);
        }
    }
}