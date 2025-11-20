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

    [Header("絵パズル管理（インスペクターでアサイン）")]
    [SerializeField] private PaintingPuzzleManager paintingPuzzleManager;

    [Header("幽霊ヒント連携（HintText をアサイン）")]
    [SerializeField] private HintText hintText;

    // どの位置を越えたらチュートリアルを出すか
    public float triggerPosX = -3.3f;

    // チュートリアルを出せる状態かどうか（フラグ）
    private bool canShowTutorial = false;

    // 「プレイヤーが2つ目の部屋にいるかどうか」を外から見れるようにするフラグ
    public bool IsPlayerInSecondRoom { get; private set; } = false;

    // 今どのチュートリアルステップか
    // 0 = まだ何も
    // 1 = チュートリアル1（部屋に入ったときのつぶやき＋本を調べよう）
    // 2 = チュートリアル2（幽霊のつぶやきからヒントを集めよう）
    // 3 = チュートリアル3（集めたヒントをもとに幽霊が好きな絵を集めるミッション）
    // 4 = チュートリアル4（正解の絵を集め終わったあと、幽霊に絵を渡すミッション）
    private int tutorialStep = 0;

    // 本を全部調べ終わったかどうか（他のスクリプトから true にしてもらう想定）
    public bool isBookChecked = false;

    // 外から「蝋燭イベントを発生させていいか」を見る用
    // （絵を集めるミッション以降ならOK）
    public bool CanTriggerCandleEvent
    {
        get { return tutorialStep >= 3; }
    }

    // 表示するテキスト（セリフ・ミッション）
    public TextMeshProUGUI Saytext;
    public TextMeshProUGUI missiontext;

    // テキストタイプ中のコルーチンを保持
    private Coroutine sayCoroutine;
    private Coroutine missionCoroutine;

    // 文字送りのスピード
    public float typeSpeed = 0.03f;

    // セリフの行間の待ち時間（何秒か置いて次のセリフに行く & TypeText の表示保持時間）
    public float sayLineInterval = 0.6f;

    // 2つ目の部屋に入ったときの「つぶやきセリフ」をリストで管理
    [Header("2つ目の部屋に入ったときのセリフ(順番に表示)")]
    [TextArea]
    public string[] enterRoomSayLines =
    {
        "ここの部屋は何だろう。",
        "部屋をしらべてみよう！"
    };

    // 幽霊のつぶやきミッション（チュートリアル2）が進行中かどうか
    public bool IsGhostWhisperMissionActive
    {
        get { return tutorialStep == 2; }
    }

    // 幽霊つぶやきミッションのベーステキスト
    private const string HintMissionBaseText = "ミッション：幽霊のつぶやきからヒントを集めよう。";

    private void Start()
    {
        Debug.Log($"[SecondRoomTutorial] Start. Time.timeScale={Time.timeScale}");

        if (Saytext == null)
            Debug.LogWarning("[SecondRoomTutorial] Saytext がアサインされていません");
        if (missiontext == null)
            Debug.LogWarning("[SecondRoomTutorial] missiontext がアサインされていません");
        if (firstRoomTutorial == null)
            Debug.LogWarning("[SecondRoomTutorial] firstRoomTutorial がアサインされていません");
        if (paintingPuzzleManager == null)
            Debug.LogWarning("[SecondRoomTutorial] paintingPuzzleManager がアサインされていません");
        if (hintText == null)
            Debug.LogWarning("[SecondRoomTutorial] hintText がアサインされていません（進捗 〇/〇 表示に使用）");
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

        // 本を全部調べ終わったらチュートリアル2へ進む
        if (tutorialStep == 1 && isBookChecked)
        {
            GoToTutorial2();
        }

        // チュートリアル3（絵を集めるミッション）中に、
        // 絵パズル側で「正解の絵2枚」がそろったら次のミッションへ進む
        if (tutorialStep == 3 &&
            paintingPuzzleManager != null &&
            paintingPuzzleManager.AllCorrectPickedUp)
        {
            Debug.Log("[SecondRoomTutorial] 絵パズルが完成したため、次のミッションへ進みます");
            GoToNextMissionAfterPictures();
        }

        // ★ チュートリアル2中は、HintText から常に進捗を取ってミッションテキスト末尾に表示
        if (tutorialStep == 2 && missiontext != null && hintText != null)
        {
            int have, need;
            hintText.GetSecondRoomHintProgress(out have, out need);

            if (need > 0)
            {
                missiontext.text = $"{HintMissionBaseText}（{have}/{need}）";
            }
            else
            {
                missiontext.text = HintMissionBaseText;
            }
        }
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

        // 一つ目の部屋のチュートリアル側を処理
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

        // ここから「2部屋目専用のセリフ」で上書き表示（セリフは消える）
        if (Saytext != null && enterRoomSayLines != null && enterRoomSayLines.Length > 0)
        {
            Debug.Log($"[SecondRoomTutorial] つぶやき開始。行数={enterRoomSayLines.Length}");
            sayCoroutine = StartCoroutine(TypeLines(Saytext, enterRoomSayLines));
        }
        else
        {
            Debug.LogWarning("[SecondRoomTutorial] Saytext または enterRoomSayLines が設定されていません");
        }

        // ミッションテキストは直接セット（消さない）
        if (missiontext != null)
        {
            missiontext.text = "ミッション：本を調べて情報を集めよう。";
        }

        Debug.Log("【チュートリアル1（二つ目の部屋）】部屋に入ったときのつぶやき＋本を調べよう 開始");
    }

    /// <summary>
    /// チュートリアル2へ進む（本アイテムを集め終わったタイミングで呼ばせる想定）
    /// 幽霊のつぶやきからヒントを集めるミッションに切り替える
    /// </summary>
    public void GoToTutorial2()
    {
        // まだチュートリアル1に入っていない場合は何もしない
        if (tutorialStep < 1) return;

        // すでにチュートリアル2以降に進んでいる場合は二重実行しない
        if (tutorialStep >= 2) return;

        tutorialStep = 2;

        Debug.Log($"[SecondRoomTutorial] GoToTutorial2 呼び出し。Time.timeScale={Time.timeScale}");

        ResetAllTextAndCoroutines();

        // つぶやき（セリフ）は TypeText で出して少ししたら消える
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル2 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "幽霊のつぶやきをよく聞けば、もっとヒントが得られそうだ。")
            );
        }

        // ミッションテキストは固定で表示（数字は Update で上書き）
        if (missiontext != null)
        {
            missiontext.text = HintMissionBaseText;
        }

        Debug.Log("【チュートリアル2（二つ目の部屋）】幽霊のつぶやきからヒントを集めよう ミッション開始");
    }

    /// <summary>
    /// チュートリアル3へ進む（幽霊のヒントを集め終わったタイミングで呼ばれる想定）
    /// 集めたヒントをもとに、幽霊が好きな絵を集めるミッションに切り替える
    /// </summary>
    public void GoToTutorial3()
    {
        // まだチュートリアル2（つぶやきヒント）に入っていない場合は何もしない
        if (tutorialStep < 2) return;

        // すでにチュートリアル3以降に進んでいる場合は二重実行しない
        if (tutorialStep >= 3) return;

        tutorialStep = 3;

        Debug.Log($"[SecondRoomTutorial] GoToTutorial3 呼び出し。Time.timeScale={Time.timeScale}");

        ResetAllTextAndCoroutines();

        // セリフは TypeText で一瞬出して消える
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル3 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "集めたヒントをもとに、幽霊が好きそうな絵を選んでみよう。")
            );
        }

        // ★ ミッションテキストは直接セットして、ずっと残す
        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル3 ミッションテキスト表示（固定）");
            missiontext.text = "ミッション：幽霊が好きな絵を集めよう。";
        }

        Debug.Log("【チュートリアル3（二つ目の部屋）】幽霊が好きな絵を集めよう ミッション開始");
    }

    /// <summary>
    /// 正解の絵を集め終わったあとの「幽霊に絵を渡そう」ミッションへ進む
    /// （Update 内で絵パズルの状態を見て自動的に呼ばれる想定）
    /// </summary>
    public void GoToNextMissionAfterPictures()
    {
        // まだ絵を集めるフェーズに入っていない場合は何もしない
        if (tutorialStep < 3) return;

        // すでにこのフェーズ以降に進んでいる場合は二重実行しない
        if (tutorialStep >= 4) return;

        tutorialStep = 4;

        Debug.Log($"[SecondRoomTutorial] GoToNextMissionAfterPictures 呼び出し。Time.timeScale={Time.timeScale}");

        ResetAllTextAndCoroutines();

        // セリフは一瞬表示して消える
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル4 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "これだけ集めれば、幽霊もきっと喜んでくれるはずだ。")
            );
        }

        // ミッションテキストは固定表示
        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル4 ミッションテキスト表示（固定）");
            missiontext.text = "ミッション：幽霊に絵を渡そう。";
        }

        Debug.Log("【チュートリアル4（二つ目の部屋）】幽霊に絵を渡そう ミッション開始");
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
    /// 他スクリプトから呼び出して「本を全部調べ終わった」と伝える用の関数
    /// （本の管理側から、最後の一冊を調べたタイミングで呼ぶ想定）
    /// </summary>
    public void OnBookChecked()
    {
        isBookChecked = true;
    }

    /// <summary>
    /// 2部屋目の幽霊つぶやき（state3/state4）が全部開示されたときに
    /// HintText 側から呼んでもらう関数
    /// </summary>
    public void OnSecondRoomAllHintsRevealed()
    {
        Debug.Log("[SecondRoomTutorial] OnSecondRoomAllHintsRevealed 受信 → チュートリアル3へ");
        GoToTutorial3();
    }

    /// <summary>
    /// 2部屋目ヒント進捗（have/need）が更新されたときに
    /// （イベント経由で呼ばれてもいいし、呼ばれなくてもいい・今は保険用）
    /// </summary>
    public void OnSecondRoomHintProgressUpdated(int have, int need)
    {
        Debug.Log($"[SecondRoomTutorial] OnSecondRoomHintProgressUpdated {have}/{need}");
        // 表示自体は Update 側でやっているのでここではログだけ
    }

    /// <summary>
    /// 1文字ずつ表示するタイプ演出（単発テキスト用）
    /// 表示し終わったら少しだけ残してから消す（セリフ用）
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

        // 少し表示してから消す（セリフなので永続しない）
        yield return new WaitForSecondsRealtime(sayLineInterval);
        target.text = "";

        Debug.Log($"[SecondRoomTutorial] TypeText 完了（テキストをクリア）");
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
