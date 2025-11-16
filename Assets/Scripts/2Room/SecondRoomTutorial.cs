using System.Collections;
using TMPro;
using UnityEngine;

// プレイヤーが2つ目の部屋にいるときに出るチュートリアルです
public class SecondRoomTutorial : MonoBehaviour
{
    // プレイヤーオブジェクト（インスペクターでセット）
    public GameObject Player;

    // 一つ目の部屋のチュートリアル（インスペクターでアサイン）
    [SerializeField] private Tutorial firstRoomTutorial;

    // どの位置を越えたらチュートリアルを出すか
    public float triggerPosX = -3.3f;

    // チュートリアルを出せる状態かどうか（フラグ）
    private bool canShowTutorial = false;

    // 「プレイヤーが2つ目の部屋にいるかどうか」を外から見れるようにするフラグ
    public bool IsPlayerInSecondRoom { get; private set; } = false;

    // 今どのチュートリアルステップか
    // 0 = まだ何も、
    // 1 = チュートリアル1（部屋に入ったときのつぶやき＋本を調べよう）、
    // 2 = チュートリアル2 へ進んだ
    private int tutorialStep = 0;

    // （必要なら）本を調べたかどうか（他のスクリプトから true にしてもらう想定）
    public bool isBookChecked = false;

    // 表示するテキスト（セリフ・ミッション）
    public TextMeshProUGUI Saytext;
    public TextMeshProUGUI missiontext;

    // テキストタイプ中のコルーチンを保持
    private Coroutine sayCoroutine;
    private Coroutine missionCoroutine;

    // 文字送りのスピード
    public float typeSpeed = 0.03f;

    // セリフの行間の待ち時間（何秒か置いて次のセリフに行く）
    public float sayLineInterval = 0.6f;

    // 2つ目の部屋に入ったときの「つぶやきセリフ」をリストで管理
    [Header("2つ目の部屋に入ったときのセリフ(順番に表示)")]
    [TextArea]
    public string[] enterRoomSayLines =
    {
        "ここの部屋は何だろう。",
        "部屋をしらべてみよう！"
    };

    private void Start()
    {
        Debug.Log($"[SecondRoomTutorial] Start. Time.timeScale={Time.timeScale}");

        if (Saytext == null)
            Debug.LogWarning("[SecondRoomTutorial] Saytext がアサインされていません");
        if (missiontext == null)
            Debug.LogWarning("[SecondRoomTutorial] missiontext がアサインされていません");
        if (firstRoomTutorial == null)
            Debug.LogWarning("[SecondRoomTutorial] firstRoomTutorial がアサインされていません");
    }

    private void Update()
    {
        if (Player == null)
        {
            Debug.LogWarning("[SecondRoomTutorial] Player が null です");
            return;
        }

        float px = Player.transform.position.x;

        // プレイヤーの X 座標が triggerPosX を超えたら（一回だけ）発火
        if (!canShowTutorial && px > triggerPosX)
        {
            canShowTutorial = true;
            IsPlayerInSecondRoom = true;  // ★ここで「2部屋目に入った」扱いにする

            Debug.Log($"[SecondRoomTutorial] トリガー通過 PlayerX={px} / triggerPosX={triggerPosX} → 2部屋目フラグON");
            StartTutorial1();
        }

        if (!canShowTutorial) return;

        // 必要ならここで Step ごとの進行管理をする
        /*
        if (tutorialStep == 1 && isBookChecked)
        {
            GoToTutorial2();
        }
        */
    }

    /// <summary>
    /// チュートリアル1を開始する
    /// ・一つ目の部屋のチュートリアル UI / ロジックを止める（幽霊スポーンだけ生かす）
    /// ・2つ目の部屋に入ったときのつぶやきセリフを Saytext に順番に表示
    /// ・ミッションテキストに「本を調べて情報を集めよう」を表示
    /// </summary>
    private void StartTutorial1()
    {
        tutorialStep = 1;

        Debug.Log($"[SecondRoomTutorial] StartTutorial1 呼び出し。Time.timeScale={Time.timeScale}");

        // 念のため、ここでタイムスケールを必ず 1 に戻す
        if (Time.timeScale == 0f)
        {
            Debug.Log("[SecondRoomTutorial] Time.timeScale が 0 だったので 1 に戻します");
            Time.timeScale = 1f;
        }

        // ★ 一つ目の部屋のチュートリアル側を処理
        if (firstRoomTutorial != null)
        {
            Debug.Log("[SecondRoomTutorial] firstRoomTutorial にスポーン開始＋停止指示を送ります");

            // 幽霊スポーンだけは先に開始させておく
            firstRoomTutorial.ForceStartSpawners();

            // その上で、1部屋目のチュートリアル UI / ロジックを止める
            firstRoomTutorial.StopTutorialForSecondRoom();
        }
        else
        {
            Debug.LogWarning("[SecondRoomTutorial] firstRoomTutorial がアサインされていません");
        }

        // 自分側のテキスト・コルーチンも一度全部リセット
        ResetAllTextAndCoroutines();

        // ここから「2部屋目専用のセリフ」で上書き表示
        if (Saytext != null && enterRoomSayLines != null && enterRoomSayLines.Length > 0)
        {
            Debug.Log($"[SecondRoomTutorial] つぶやき開始。行数={enterRoomSayLines.Length}");
            sayCoroutine = StartCoroutine(TypeLines(Saytext, enterRoomSayLines));
        }
        else
        {
            Debug.LogWarning("[SecondRoomTutorial] Saytext または enterRoomSayLines が設定されていません");
        }

        // ミッションテキスト側は固定文を出す
        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] ミッションテキストを表示開始");
            missionCoroutine = StartCoroutine(
                TypeText(missiontext, "ミッション：本を調べて情報を集めよう。")
            );
        }

        Debug.Log("【チュートリアル1（二つ目の部屋）】部屋に入ったときのつぶやき＋本を調べよう 開始");
    }

    /// <summary>
    /// チュートリアル2へ進む（ItemCountMissionText などから呼ばせる）
    /// 例：アイテムを5/5個集めたタイミングでこの関数を呼ぶ
    /// </summary>
    public void GoToTutorial2()
    {
        if (tutorialStep >= 2) return;

        tutorialStep = 2;

        Debug.Log($"[SecondRoomTutorial] GoToTutorial2 呼び出し。Time.timeScale={Time.timeScale}");

        ResetAllTextAndCoroutines();

        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル2 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "本からいくつかのヒントを得た。")
            );
        }

        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル2 ミッションテキスト表示開始");
            missionCoroutine = StartCoroutine(
                TypeText(missiontext, "ミッション：アイテムを集めて、さらに情報をあつめよう。")
            );
        }

        Debug.Log("【チュートリアル2（二つ目の部屋）】次のチュートリアルへ進みました");
    }

    /// <summary>
    /// テキスト系のコルーチンを止めて、テキスト内容を完全リセットする
    /// </summary>
    private void ResetAllTextAndCoroutines()
    {
        Debug.Log("[SecondRoomTutorial] ResetAllTextAndCoroutines 呼び出し");

        // コルーチン停止
        if (sayCoroutine != null)
        {
            StopCoroutine(sayCoroutine);
            sayCoroutine = null;
        }
        if (missionCoroutine != null)
        {
            StopCoroutine(missionCoroutine);
            missionCoroutine = null;
        }

        // テキストリセット＆表示ON
        if (Saytext != null)
        {
            Saytext.text = "";
            Saytext.gameObject.SetActive(true);
        }
        if (missiontext != null)
        {
            missiontext.text = "";
            missiontext.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 別スクリプトから呼び出して「本を調べた」ことにする用の関数
    /// （本のオブジェクト側から呼んでもOK）
    /// </summary>
    public void OnBookChecked()
    {
        isBookChecked = true;
    }

    /// <summary>
    /// 1文字ずつ表示するタイプ演出（単発テキスト用）
    /// 他スクリプトの書き込みの影響を受けないように、ローカルバッファで管理する
    /// </summary>
    private IEnumerator TypeText(TextMeshProUGUI target, string content)
    {
        if (target == null) yield break;

        Debug.Log($"[SecondRoomTutorial] TypeText 開始 content=\"{content}\"");

        string current = "";
        target.text = current;

        foreach (char c in content)
        {
            current += c;
            target.text = current;                  // 毎回、自分の current で上書き
            yield return new WaitForSecondsRealtime(typeSpeed);
        }

        Debug.Log($"[SecondRoomTutorial] TypeText 完了 text=\"{target.text}\"");
    }

    /// <summary>
    /// 複数行を順番に表示するタイプ演出（セリフリスト用）
    /// ★各行を表示するとき、前の行のテキストは消してから出す
    /// ★最後の行を出し終わったら、少し待ってテキストを消す
    /// </summary>
    private IEnumerator TypeLines(TextMeshProUGUI target, string[] lines)
    {
        if (target == null || lines == null || lines.Length == 0)
        {
            Debug.LogWarning("[SecondRoomTutorial] TypeLines: target または lines が無効です");
            yield break;
        }

        Debug.Log($"[SecondRoomTutorial] TypeLines 開始 行数={lines.Length}");

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Debug.Log($"[SecondRoomTutorial] 行 {i}: \"{line}\" の表示開始");

            // 前の行を消してから開始
            string current = "";
            target.text = current;

            // 1行ぶんタイプ
            foreach (char c in line)
            {
                current += c;
                target.text = current;              // 常に current を上書き
                yield return new WaitForSecondsRealtime(typeSpeed);
            }

            // 最後の行でなければ、少し待ってから次の行へ
            if (i < lines.Length - 1)
            {
                yield return new WaitForSecondsRealtime(sayLineInterval);
            }
        }

        // 全部の行を出し終わったあと、少しだけ見せてから消す
        yield return new WaitForSecondsRealtime(sayLineInterval);

        target.text = "";              // テキストを消す

        Debug.Log("[SecondRoomTutorial] TypeLines 完了。テキストをクリアしました");
    }
}
