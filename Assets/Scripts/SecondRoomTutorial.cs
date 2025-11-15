using UnityEngine;

// プレイヤーが2つ目の部屋にいるときに出るチュートリアルです
public class SecondRoomTutorial : MonoBehaviour
{
    // プレイヤーオブジェクト（インスペクターでセット）
    public GameObject Player;

    // どの位置を越えたらチュートリアルを出すか
    public float triggerPosX = -1.7f;

    // チュートリアルを出せる状態かどうか（フラグ）
    private bool canShowTutorial = false;

    // 今どのチュートリアルステップか
    // 0 = まだ何も、1 = チュートリアル1（本を調べよう）、2 = チュートリアル2 へ進んだ
    private int tutorialStep = 0;

    // 本を調べたかどうか（他のスクリプトから true にしてもらう想定）
    public bool isBookChecked = false;

    private void Update()
    {
        // プレイヤーのtransform xが-1.7より小さかったらこのコンポーネントを適応＝チュートリアルを出す
        if (!canShowTutorial && Player.transform.position.x < triggerPosX)
        {
            canShowTutorial = true;
            StartTutorial1();
        }

        // チュートリアル出せる状態のとき
        if (!canShowTutorial) return;

        // まずチュートリアル1
        // 「本を調べよう」を表示している状態
        if (tutorialStep == 1)
        {
            // 本をしらべているかチェック　これがクリアできていたらチュートリアル2へ
            if (isBookChecked)
            {
                GoToTutorial2();
            }
        }

        // tutorialStep == 2 のときに、チュートリアル2の処理を書いていくイメージ
        // 例：次の目標を表示する、UIを切り替えるなど
    }

    /// <summary>
    /// チュートリアル1を開始する（本を調べよう）
    /// </summary>
    private void StartTutorial1()
    {
        tutorialStep = 1;

        // 本をしらべよう
        // ここで実際はUIを出したりする
        Debug.Log("【チュートリアル1】本を調べよう");
        // 例：
        // tutorialText.text = "本を調べよう";
        // tutorialPanel.SetActive(true);
    }

    /// <summary>
    /// チュートリアル2へ進む
    /// </summary>
    private void GoToTutorial2()
    {
        tutorialStep = 2;

        // チュートリアル2の内容をここに書く
        Debug.Log("【チュートリアル2】次のチュートリアルへ進みました");

        // 例：
        // tutorialText.text = "次は◯◯しよう";
    }

    /// <summary>
    /// 別スクリプトから呼び出して「本を調べた」ことにする用の関数
    /// （本のオブジェクト側から呼んでもOK）
    /// </summary>
    public void OnBookChecked()
    {
        isBookChecked = true;
    }
}
