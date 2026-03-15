/*
このスクリプトは「プレイヤーが2つ目の部屋に入った後のチュートリアル進行」を管理します。

主な役割
・プレイヤーの位置（X座標）で「2部屋目に入った」タイミングを判定する
・2部屋目に入ったら、1部屋目チュートリアルを停止しつつ幽霊スポーンだけ維持する
・チュートリアルを段階（Step1〜4）で進め、セリフUIとミッションUIを更新する
・本を調べ終わったらチュートリアル2へ進める
・幽霊のつぶやきヒント収集（HintText）の進捗をミッション表示に反映する
・絵パズル（PaintingPuzzleManager）が完成したら次のミッションへ自動遷移する
・絵回収後、「幽霊に絵を渡そう」へ進んだタイミングで渡し先の幽霊を有効化する
・セリフはタイプライター演出で表示し、一定時間後に消える（ミッションは基本残す）

進行ステップ
0: まだ何もしていない
1: 部屋に入った時のセリフ + 「本を調べて情報を集めよう」ミッション
2: 「幽霊のつぶやきからヒントを集めよう」ミッション（進捗 〇/〇 を表示）
3: 「幽霊が好きな絵を集めよう」ミッション
4: 「幽霊に絵を渡そう」ミッション

ルール（コメント方針）
・クラス冒頭に「何をするスクリプトか」を説明する
・メソッドごとに「何をするメソッドか」を説明する
・メソッド内部でも、処理のまとまりごとに「何をしているか」を説明する
*/

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

    //========================
    // 絵回収後に出現させる幽霊の管理
    //========================

    // 絵回収後に有効化する対象をキャッシュ
    // インスペクターで触らなくても内部で自動特定する
    private GameObject _ghostToActivateAfterPictures;

    // 有効化処理の二重実行防止
    private bool _ghostActivatedAfterPictures = false;

    /// <summary>
    /// 参照が入っているかなど、初期状態のチェックを行う
    /// </summary>
    private void Start()
    {
        Debug.Log($"[SecondRoomTutorial] Start. Time.timeScale={Time.timeScale}");

        // UI参照や依存参照のチェック（動かない原因の切り分け用）
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

        // 絵回収後に出現させる幽霊を、開始時点で自動探索しておく
        ResolveGhostAfterPicturesIfNeeded();
    }

    /// <summary>
    /// 毎フレーム、2部屋目突入判定と、各チュートリアル段階の進行条件をチェックする
    /// </summary>
    private void Update()
    {
        // プレイヤー参照がないと位置判定ができないので中断
        if (Player == null)
        {
            Debug.LogWarning("[SecondRoomTutorial] Player が null です");
            return;
        }

        // 2部屋目突入の判定に使う座標（X）
        float px = Player.transform.position.x;

        // まだ開始していない状態で、しきい値を超えたら「2部屋目チュートリアル開始」
        if (!canShowTutorial && px > triggerPosX)
        {
            // 一度だけ発火するようフラグを立てる
            canShowTutorial = true;

            // 外部から参照できる「2部屋目にいる」フラグをONにする
            IsPlayerInSecondRoom = true;

            Debug.Log($"[SecondRoomTutorial] トリガー通過 PlayerX={px} / triggerPosX={triggerPosX} → 2部屋目フラグON");

            // 2部屋目チュートリアルの最初の段階へ
            StartTutorial1();
        }

        // 2部屋目に入っていなければ、以降の進行チェックは不要
        if (!canShowTutorial) return;

        // チュートリアル1中に「本を全部調べ終わった」フラグが立ったらチュートリアル2へ
        if (tutorialStep == 1 && isBookChecked)
        {
            GoToTutorial2();
        }

        // チュートリアル3中に「正解の絵2枚」が揃ったら、次のミッションへ
        if (tutorialStep == 3 &&
            paintingPuzzleManager != null &&
            paintingPuzzleManager.AllCorrectPickedUp)
        {
            Debug.Log("[SecondRoomTutorial] 絵パズルが完成したため、次のミッションへ進みます");
            GoToNextMissionAfterPictures();
        }

        // チュートリアル2中は、HintTextから進捗（〇/〇）を取り続けてミッションUIに反映
        if (tutorialStep == 2 && missiontext != null && hintText != null)
        {
            // HintText側の現在進捗を取得
            int have, need;
            hintText.GetSecondRoomHintProgress(out have, out need);

            // needが0の場合は表示を崩さない（仕様次第で調整）
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
    /// ・1部屋目チュートリアルのUI/ロジックを止める（幽霊スポーンだけは生かす）
    /// ・2部屋目突入時のセリフを順に表示
    /// ・ミッションUIを「本を調べて情報を集めよう」にする
    /// </summary>
    private void StartTutorial1()
    {
        // チュートリアル段階を1に設定
        tutorialStep = 1;

        Debug.Log($"[SecondRoomTutorial] StartTutorial1 呼び出し。Time.timeScale={Time.timeScale}");

        // もしTimeScaleが0のままだとコルーチンやゲームが止まるので戻す
        if (Time.timeScale == 0f)
        {
            Debug.Log("[SecondRoomTutorial] Time.timeScale が 0 だったので 1 に戻します");
            Time.timeScale = 1f;
        }

        // 1部屋目チュートリアルを「2部屋目用に停止」する
        if (firstRoomTutorial != null)
        {
            Debug.Log("[SecondRoomTutorial] firstRoomTutorial にスポーン開始＋停止指示を送ります");

            // 2部屋目に入った時点で幽霊スポーンは継続したいので、先に開始指示
            firstRoomTutorial.ForceStartSpawners();

            // その上で、1部屋目側のUI/ロジックを停止（2部屋目以降は不要）
            firstRoomTutorial.StopTutorialForSecondRoom();
        }
        else
        {
            Debug.LogWarning("[SecondRoomTutorial] firstRoomTutorial がアサインされていません");
        }

        // 自分側の表示を一旦全部初期化（途中のタイプ中断・テキスト消去）
        ResetAllTextAndCoroutines();

        // セリフ表示（複数行を順番にタイプ表示し、最後に消す）
        if (Saytext != null && enterRoomSayLines != null && enterRoomSayLines.Length > 0)
        {
            Debug.Log($"[SecondRoomTutorial] つぶやき開始。行数={enterRoomSayLines.Length}");
            sayCoroutine = StartCoroutine(TypeLines(Saytext, enterRoomSayLines));
        }
        else
        {
            Debug.LogWarning("[SecondRoomTutorial] Saytext または enterRoomSayLines が設定されていません");
        }

        // ミッションは「残す前提」なので即時セット
        if (missiontext != null)
        {
            missiontext.text = "ミッション：本を調べて情報を集めよう。";
        }

        Debug.Log("【チュートリアル1（二つ目の部屋）】部屋に入ったときのつぶやき＋本を調べよう 開始");
    }

    /// <summary>
    /// チュートリアル2へ進む
    /// ・本を全部調べ終わったタイミングで呼ばれる想定
    /// ・幽霊のつぶやきからヒントを集めるミッションに切り替える
    /// </summary>
    public void GoToTutorial2()
    {
        // まだチュートリアル1に入っていない場合は無効
        if (tutorialStep < 1) return;

        // すでにチュートリアル2以降なら二重実行しない
        if (tutorialStep >= 2) return;

        // 段階を2へ
        tutorialStep = 2;

        Debug.Log($"[SecondRoomTutorial] GoToTutorial2 呼び出し。Time.timeScale={Time.timeScale}");

        // 既存の表示・コルーチンをリセット
        ResetAllTextAndCoroutines();

        // セリフは一瞬表示して消す（注意喚起）
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル2 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "幽霊のつぶやきをよく聞けば、もっとヒントが得られそうだ。")
            );
        }

        // ミッションは固定文を置き、進捗（〇/〇）はUpdateで随時上書きする
        if (missiontext != null)
        {
            missiontext.text = HintMissionBaseText;
        }

        Debug.Log("【チュートリアル2（二つ目の部屋）】幽霊のつぶやきからヒントを集めよう ミッション開始");
    }

    /// <summary>
    /// チュートリアル3へ進む
    /// ・幽霊のヒントを集め終わったタイミングで呼ばれる想定
    /// ・幽霊が好きな絵を集めるミッションに切り替える
    /// </summary>
    public void GoToTutorial3()
    {
        // チュートリアル2に入っていないなら無効
        if (tutorialStep < 2) return;

        // すでにチュートリアル3以降なら二重実行しない
        if (tutorialStep >= 3) return;

        // 段階を3へ
        tutorialStep = 3;

        Debug.Log($"[SecondRoomTutorial] GoToTutorial3 呼び出し。Time.timeScale={Time.timeScale}");

        // 表示・コルーチンをリセット
        ResetAllTextAndCoroutines();

        // セリフは一瞬表示して消す（ミッション導入）
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル3 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "集めたヒントをもとに、幽霊が好きそうな絵を選んでみよう。")
            );
        }

        // ミッションは固定表示で残す
        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル3 ミッションテキスト表示（固定）");
            missiontext.text = "ミッション：幽霊が好きな絵を集めよう。";
        }

        Debug.Log("【チュートリアル3（二つ目の部屋）】幽霊が好きな絵を集めよう ミッション開始");
    }

    /// <summary>
    /// 正解の絵を集め終わった後のミッションへ進む
    /// ・Update内で絵パズル完成を検知して呼ばれる想定
    /// ・「幽霊に絵を渡そう」へ切り替える
    /// ・あわせて渡し先の幽霊を有効化する
    /// </summary>
    public void GoToNextMissionAfterPictures()
    {
        // まだチュートリアル3に入っていないなら無効
        if (tutorialStep < 3) return;

        // すでに4以降なら二重実行しない
        if (tutorialStep >= 4) return;

        // 段階を4へ
        tutorialStep = 4;

        Debug.Log($"[SecondRoomTutorial] GoToNextMissionAfterPictures 呼び出し。Time.timeScale={Time.timeScale}");

        // 表示・コルーチンをリセット
        ResetAllTextAndCoroutines();

        // セリフは一瞬表示して消す（達成感の補強）
        if (Saytext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル4 セリフ表示開始");
            sayCoroutine = StartCoroutine(
                TypeText(Saytext, "これだけ集めれば、幽霊もきっと喜んでくれるはずだ。")
            );
        }

        // ミッションは固定表示で残す
        if (missiontext != null)
        {
            Debug.Log("[SecondRoomTutorial] チュートリアル4 ミッションテキスト表示（固定）");
            missiontext.text = "ミッション：幽霊に絵を渡そう。";
        }

        // 絵回収後に渡し先の幽霊を有効化する
        ActivateGhostAfterPicturesIfNeeded();

        Debug.Log("【チュートリアル4（二つ目の部屋）】幽霊に絵を渡そう ミッション開始");
    }

    /// <summary>
    /// 絵回収後に出現させる幽霊を、必要なら自動特定する
    /// ・インスペクターで触らなくても良いように、シーン内の非アクティブオブジェクトも含めて探索する
    /// ・優先順位は「非アクティブの Ghostタグ付き」→「非アクティブの SearchChase持ち」→「Ghostタグ付き」
    /// </summary>
    private void ResolveGhostAfterPicturesIfNeeded()
    {
        // すでに見つかっていれば再探索しない
        if (_ghostToActivateAfterPictures != null) return;

        Debug.Log("[SecondRoomTutorial] 絵回収後に有効化する幽霊を自動探索します");

        GameObject fallbackActiveGhost = null;
        GameObject fallbackInactiveSearchChase = null;

        // 非アクティブも含めてシーン上の GameObject を全部見る
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;

            // Projectビュー上のPrefab等は除外し、シーン上にあるものだけ対象にする
            if (!go.scene.IsValid()) continue;

            // HideFlags付きの特殊オブジェクトは除外
            if (go.hideFlags != HideFlags.None) continue;

            bool isInactive = !go.activeInHierarchy;
            bool hasGhostTag = go.CompareTag("Ghost");
            bool hasSearchChase = go.GetComponent<SearchChase>() != null;

            // 最優先：非アクティブの Ghost タグ付き
            if (isInactive && hasGhostTag)
            {
                _ghostToActivateAfterPictures = go;
                Debug.Log($"[SecondRoomTutorial] 絵回収後の出現対象を発見（最優先: 非アクティブ Ghostタグ） name={go.name}");
                return;
            }

            // 次点：非アクティブの SearchChase 持ち
            if (isInactive && hasSearchChase && fallbackInactiveSearchChase == null)
            {
                fallbackInactiveSearchChase = go;
            }

            // 最後の保険：Ghostタグ付き
            if (hasGhostTag && fallbackActiveGhost == null)
            {
                fallbackActiveGhost = go;
            }
        }

        if (fallbackInactiveSearchChase != null)
        {
            _ghostToActivateAfterPictures = fallbackInactiveSearchChase;
            Debug.Log($"[SecondRoomTutorial] 絵回収後の出現対象を発見（次点: 非アクティブ SearchChase） name={fallbackInactiveSearchChase.name}");
            return;
        }

        if (fallbackActiveGhost != null)
        {
            _ghostToActivateAfterPictures = fallbackActiveGhost;
            Debug.Log($"[SecondRoomTutorial] 絵回収後の出現対象を発見（保険: Ghostタグ） name={fallbackActiveGhost.name}");
            return;
        }

        Debug.LogWarning("[SecondRoomTutorial] 絵回収後に有効化する幽霊が見つかりませんでした。Ghostタグ または SearchChase を確認してください");
    }

    /// <summary>
    /// 絵回収後に、必要なら幽霊を有効化する
    /// ・二重実行を防ぐ
    /// ・対象がまだ未特定ならここでも再探索する
    /// ・未設定/未発見時は警告ログを出す
    /// </summary>
    private void ActivateGhostAfterPicturesIfNeeded()
    {
        // すでに一度有効化済みなら何もしない
        if (_ghostActivatedAfterPictures)
        {
            Debug.Log("[SecondRoomTutorial] 絵回収後の幽霊有効化はすでに実行済みです");
            return;
        }

        // 対象が未確定ならここでも再探索
        if (_ghostToActivateAfterPictures == null)
        {
            ResolveGhostAfterPicturesIfNeeded();
        }

        // それでも見つからなければ警告
        if (_ghostToActivateAfterPictures == null)
        {
            Debug.LogWarning("[SecondRoomTutorial] 絵回収後に有効化する幽霊が特定できなかったため、SetActive(true) を実行できません");
            return;
        }

        // すでにアクティブなら、そのまま完了扱いにする
        if (_ghostToActivateAfterPictures.activeSelf)
        {
            _ghostActivatedAfterPictures = true;
            Debug.Log($"[SecondRoomTutorial] 絵回収後の幽霊はすでに active です name={_ghostToActivateAfterPictures.name}");
            return;
        }

        // 出現させる
        _ghostToActivateAfterPictures.SetActive(true);
        _ghostActivatedAfterPictures = true;

        Debug.Log($"[SecondRoomTutorial] 絵回収後の幽霊を有効化しました name={_ghostToActivateAfterPictures.name}");
    }

    /// <summary>
    /// テキスト表示とコルーチンを完全にリセットする
    /// ・途中のタイプ演出を中断する
    /// ・テキスト内容を空にする
    /// ・UIオブジェクトは表示状態に戻す（必要ならここは方針に合わせて変更）
    /// </summary>
    private void ResetAllTextAndCoroutines()
    {
        Debug.Log("[SecondRoomTutorial] ResetAllTextAndCoroutines 呼び出し");

        // セリフ側コルーチンを止める
        if (sayCoroutine != null)
        {
            StopCoroutine(sayCoroutine);
            sayCoroutine = null;
        }

        // ミッション側コルーチンを止める（現状は未使用だが保険で用意）
        if (missionCoroutine != null)
        {
            StopCoroutine(missionCoroutine);
            missionCoroutine = null;
        }

        // セリフUIを初期化
        if (Saytext != null)
        {
            Saytext.text = "";
            Saytext.gameObject.SetActive(true);
        }

        // ミッションUIを初期化
        if (missiontext != null)
        {
            missiontext.text = "";
            missiontext.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 外部から「本を全部調べ終わった」ことを通知するためのメソッド
    /// ・本の管理側から最後の本を調べたタイミングで呼ぶ想定
    /// </summary>
    public void OnBookChecked()
    {
        // Update側の条件（tutorialStep==1 && isBookChecked）で次へ進めるために立てる
        isBookChecked = true;
    }

    /// <summary>
    /// 2部屋目の幽霊ヒントがすべて開示されたことを受け取るメソッド
    /// ・HintText側から呼んでもらう想定
    /// ・チュートリアル3へ進める
    /// </summary>
    public void OnSecondRoomAllHintsRevealed()
    {
        Debug.Log("[SecondRoomTutorial] OnSecondRoomAllHintsRevealed 受信 → チュートリアル3へ");
        GoToTutorial3();
    }

    /// <summary>
    /// 2部屋目のヒント進捗が更新された時に呼ばれる想定のメソッド
    /// ・今は表示をUpdateで更新しているので、ここではログ程度に留める
    /// </summary>
    public void OnSecondRoomHintProgressUpdated(int have, int need)
    {
        Debug.Log($"[SecondRoomTutorial] OnSecondRoomHintProgressUpdated {have}/{need}");
    }

    /// <summary>
    /// 1行のテキストをタイプ表示するコルーチン
    /// ・1文字ずつ表示していく
    /// ・表示完了後に少し待ってからテキストを消す（セリフ用途）
    /// </summary>
    private IEnumerator TypeText(TextMeshProUGUI target, string content)
    {
        // 表示対象がない場合は何もしない
        if (target == null) yield break;

        Debug.Log($"[SecondRoomTutorial] TypeText 開始 content=\"{content}\"");

        // 最初に空にしてから開始
        string current = "";
        target.text = current;

        // 1文字ずつ追加して表示更新
        foreach (char c in content)
        {
            current += c;
            target.text = current;

            // UI演出はゲームのTimeScaleに影響されない方が扱いやすいのでRealtimeで待つ
            yield return new WaitForSecondsRealtime(typeSpeed);
        }

        // 表示が終わったら少し残してから消す
        yield return new WaitForSecondsRealtime(sayLineInterval);
        target.text = "";

        Debug.Log("[SecondRoomTutorial] TypeText 完了（テキストをクリア）");
    }

    /// <summary>
    /// 複数行のテキストを順番にタイプ表示するコルーチン
    /// ・各行は「前の行を消してから」タイプ表示する
    /// ・最後の行を表示後、少し残してから消す
    /// </summary>
    private IEnumerator TypeLines(TextMeshProUGUI target, string[] lines)
    {
        // 表示対象やデータが無い場合は何もしない
        if (target == null || lines == null || lines.Length == 0)
        {
            Debug.LogWarning("[SecondRoomTutorial] TypeLines: target または lines が無効です");
            yield break;
        }

        Debug.Log($"[SecondRoomTutorial] TypeLines 開始 行数={lines.Length}");

        // 行を順番に表示する
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Debug.Log($"[SecondRoomTutorial] 行 {i}: \"{line}\" の表示開始");

            // 行の開始時点で表示をクリア
            string current = "";
            target.text = current;

            // 1行ぶんタイプ表示
            foreach (char c in line)
            {
                current += c;
                target.text = current;
                yield return new WaitForSecondsRealtime(typeSpeed);
            }

            // 最後の行でなければ、次の行へ行く前に少し間を置く
            if (i < lines.Length - 1)
            {
                yield return new WaitForSecondsRealtime(sayLineInterval);
            }
        }

        // 全行の表示が終わったら少し残してから消す
        yield return new WaitForSecondsRealtime(sayLineInterval);
        target.text = "";

        Debug.Log("[SecondRoomTutorial] TypeLines 完了。テキストをクリアしました");
    }
}