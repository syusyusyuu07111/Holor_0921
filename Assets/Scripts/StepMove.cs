using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class StepMove : MonoBehaviour
{
    /*
        ============================================================
        StepMove がやること
        ============================================================

        ■目的
        ・CharacterController が「階段の段鼻（ほぼ垂直の面）」に当たったときだけ、
          ほんの少し上方向に持ち上げて、段差で引っかかるのを減らす。

        ■前提（超重要）
        ・階段として扱いたいコライダーには stairsTag（初期: "Stairs"）を付ける。
        ・このスクリプトは「横移動」は一切しない。
          横移動は別の PlayerController / cc.Move などが担当。
        ・ここは「段鼻にぶつかった瞬間」に cc.Move(Vector3.up * lift) を1回入れるだけ。

        ■ざっくり流れ（OnControllerColliderHit）
        1) 当たった相手が Stairs タグじゃないなら無視
        2) 当たった面が “段鼻っぽい（ほぼ垂直）” じゃないなら無視
           - hit.normal と Vector3.up の内積(dotUp)が小さいほど垂直
        3) 上方向に動いて当たったのなら無視（ジャンプ中など）
        4) 持ち上げ量 lift を決める（小さく）
        5) 頭上に空間があるか CanRise() でチェック
        6) OKなら cc.Move でちょい上げ

        ============================================================
    */

    // =========================
    // Stairs 判定
    // =========================
    [Header("Stairs判定")]
    [SerializeField] string stairsTag = "Stairs";   // 階段コライダーに付けるTag
    [SerializeField] LayerMask collisionMask = ~0;  // 頭上チェック用（天井/地形などのレイヤー）

    // =========================
    // 持ち上げパラメータ
    // =========================
    [Header("持ち上げパラメータ")]
    [SerializeField] float maxStepHeight = 0.45f;   // 1回の最大上昇量（登りたい段差より少し上）
    [SerializeField] float liftPerFrame = 0.06f;    // 1フレームで持ち上げる量（小刻みでガタつき抑え）

    [SerializeField] float wallDotThreshold = 0.5f;
    // dotUp = Dot(hit.normal, Up)
    // 1に近い：上向き面（床/斜面） / 0に近い：垂直面（壁/段鼻）
    // dotUp がこの閾値より大きい＝上向き成分が強い → “段鼻じゃない” と判断して除外

    // =========================
    // 内部参照
    // =========================
    CharacterController cc;

    void Awake()
    {
        // CharacterController を必ず持っている（RequireComponent）ので GetComponent で取る
        cc = GetComponent<CharacterController>();
    }

    // CharacterController が何かに当たったときに呼ばれる（物理衝突イベント）
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // -----------------------------
        // 1) 相手チェック：Stairs タグ以外は無視
        // -----------------------------
        if (!hit.collider) return;
        if (!hit.collider.CompareTag(stairsTag)) return;

        // -----------------------------
        // 2) 面の向きチェック：段鼻っぽい “ほぼ垂直面” だけ拾う
        //    dotUp が小さいほど垂直
        // -----------------------------
        float dotUp = Vector3.Dot(hit.normal, Vector3.up);

        // dotUp が大きい（上向きの面）＝床/斜面なので、持ち上げ処理しない
        if (dotUp > wallDotThreshold) return;

        // -----------------------------
        // 3) 衝突方向チェック：
        //    上方向へ動いてぶつかった場合は無視（ジャンプ中に段を登らせない）
        // -----------------------------
        if (hit.moveDirection.y > 0.05f) return;

        // -----------------------------
        // 4) このフレームの持ち上げ量を決める
        //    ・liftPerFrame を上限に
        //    ・maxStepHeight を超えない範囲で
        // -----------------------------
        float lift = Mathf.Min(liftPerFrame, maxStepHeight);
        if (lift <= 0f) return;

        // -----------------------------
        // 5) 頭上にスペースがあるか？
        //    天井にめり込むなら上げない
        // -----------------------------
        if (!CanRise(lift)) return;

        // -----------------------------
        // 6) 実際に “ちょい上げ”
        //    ※横移動は別スクリプト側が担当する前提
        // -----------------------------
        cc.Move(Vector3.up * lift);
    }

    // ============================================================
    // CanRise
    // ・CharacterController のカプセル形状を元に
    //   「upAmount だけ上にずらしたカプセルが何かにぶつかるか」をチェックする
    // ・ぶつかるなら false（上がれない）
    // ============================================================
    bool CanRise(float upAmount)
    {
        // CharacterController.center をワールド座標に変換
        Vector3 centerWorld = transform.TransformPoint(cc.center);

        // 半径はスケールのXZ（太さ）を反映
        float r = cc.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

        // カプセルの “半分の高さ” を計算
        // height*0.5 が r より小さいとカプセルが破綻するので最低 r+α を確保
        float halfH = Mathf.Max((cc.height * transform.lossyScale.y) * 0.5f, r + 0.01f);

        // カプセルの端点（球の中心）を作る
        // 上端＝中心 + (halfH - r)
        // 下端＝中心 - (halfH - r)
        Vector3 pTop = centerWorld + Vector3.up * (halfH - r);
        Vector3 pBot = centerWorld - Vector3.up * (halfH - r);

        // 少し余白を足して上げる（天井にギリギリ触れるのを避ける）
        Vector3 up = Vector3.up * (upAmount + 0.02f);

        // up した先でカプセルが何かに被っているかチェック
        bool blocked = Physics.CheckCapsule(
            pTop + up,
            pBot + up,
            r,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        // blocked なら上がれない
        return !blocked;
    }
}