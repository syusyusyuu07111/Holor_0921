using UnityEngine;
using UnityEngine.SceneManagement;

public class Retry : MonoBehaviour
{
    /*
        =========================
        このスクリプトの役割
        =========================

        ■目的
        ・特定の入力（Attack）が押されたら
          指定したシーンを再読み込みする（＝リトライ）

        ■流れ
        1) Awake() で InputSystem_Actions を生成
        2) OnEnable() で Playerアクションを有効化
        3) Update() で毎フレーム入力チェック
        4) Attack が押された瞬間に
           SceneManager.LoadScene("SampleScene") を実行

        ※今はシーン名を文字列で直接指定している。
          ビルド設定（Build Settings）に "SampleScene" が登録されている必要あり。
    */

    // 入力アクション用
    InputSystem_Actions input;

    private void Awake()
    {
        // InputSystem_Actions のインスタンス生成
        input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        // Player アクションマップを有効化
        input.Player.Enable();
    }

    // 毎フレーム呼ばれる
    void Update()
    {
        // Attack が「押された瞬間」だけ反応（triggered）
        if (input.Player.Attack.triggered)
        {
            // 指定シーンを読み込み（＝現在シーンをリセット）
            SceneManager.LoadScene("SampleScene");
        }
    }
}