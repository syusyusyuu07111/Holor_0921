using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// 絵を2枚持っている状態で幽霊に近づくと
/// 「幽霊に絵を渡す」テキストを表示し、
/// InputActions の Player.Interact が押されたときに
/// 条件がそろっていれば幽霊に絵を渡して消える処理を行う。
/// 絵を渡せたらシーン遷移を行う。
/// （どのゴーストのインスタンスでも同じ動作をする想定）
/// </summary>
public class GivePaintingToGhost : MonoBehaviour
{
    [Header("プレイヤーの Transform")]
    [SerializeField] private Transform player;

    [Header("絵パズルのマネージャー")]
    [SerializeField] private PaintingPuzzleManager paintingPuzzleManager;

    [Header("インタラクト可能な距離")]
    [SerializeField] private float interactDistance = 2.0f;

    [Header("『幽霊に絵を渡す』テキストUI")]
    [SerializeField] private GameObject interactMessageUI;

    [Header("渡せたあとに遷移するシーン名（空なら遷移しない）")]
    [SerializeField] private string nextSceneName;

    /// <summary>プレイヤーが範囲内かどうか</summary>
    private bool isPlayerInRange = false;

    /// <summary>
    /// 幽霊に絵を渡し終わったかどうか（多重実行防止用／他スクリプトから参照用）
    /// </summary>
    public bool HasGivenPainting { get; private set; } = false;

    /// <summary>
    /// Input Actions の C# クラスインスタンス
    /// ※クラス名はプロジェクトで生成されたものに合わせてください
    /// </summary>
    private InputSystem_Actions _input;   // ← ここはあなたの InputActions クラス名に合わせてね

    /// <summary>
    /// 「正解の絵2枚がそろったあと、一度キーが離されてからでないと
    /// 幽霊に渡せない」ようにするためのフラグ
    /// </summary>
    private bool readyForNewInteract = false;

    /// <summary>前フレーム時点で「正解の絵2枚がそろっていたか」</summary>
    private bool prevAllPickedUp = false;

    private void Awake()
    {
        if (_input == null)
        {
            _input = new InputSystem_Actions();   // ← ここもクラス名を合わせる
            Debug.Log("[GivePaintingToGhost] PlayerInputActions インスタンス生成");
        }
    }

    private void OnEnable()
    {
        Debug.Log("[GivePaintingToGhost] OnEnable");

        if (_input != null)
        {
            _input.Player.Enable();
            Debug.Log("[GivePaintingToGhost] _input.Player Enable");
        }

        // 開始時はUIを消しておく
        SetInteractUI(false);
        isPlayerInRange = false;
        readyForNewInteract = false;
        prevAllPickedUp = false;
    }

    private void OnDisable()
    {
        Debug.Log("[GivePaintingToGhost] OnDisable");

        if (_input != null)
        {
            _input.Player.Disable();
            Debug.Log("[GivePaintingToGhost] _input.Player Disable");
        }

        SetInteractUI(false);
        isPlayerInRange = false;
        readyForNewInteract = false;
        prevAllPickedUp = false;
    }

    private void Update()
    {
        // 必要な参照がなければ何もしない
        if (player == null || paintingPuzzleManager == null)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            return;
        }

        // すでにこのゴーストが絵を受け取っていたら処理不要
        if (HasGivenPainting)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            return;
        }

        bool allPickedUp = paintingPuzzleManager.AllCorrectPickedUp;

        // 正解の絵がまだそろっていない間は UI もインタラクトも無効
        if (!allPickedUp)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            readyForNewInteract = false;
            prevAllPickedUp = false;
            return;
        }

        // ★ ここから「絵がそろったあとのキー入力状態」を管理する

        // 1フレーム前まで絵がそろっていなかった → 今フレームで初めてそろった
        if (!prevAllPickedUp && allPickedUp)
        {
            // この瞬間のキー押しっぱなしは無視したいので、
            // いったん readyForNewInteract は false のままにしておく
            readyForNewInteract = false;
            Debug.Log("[GivePaintingToGhost] 全ての絵がそろいました（新規完了）→ 次のキーリリース待ち");
        }

        // まだ「新しい押下」を受け付けていない場合、
        // 一度キーが離されたら「次の押下からOK」にする
        if (!readyForNewInteract && _input != null)
        {
            // Interact が今押されていない状態になったら、次の押下を受け付ける
            if (!_input.Player.Interact.IsPressed())
            {
                readyForNewInteract = true;
                Debug.Log("[GivePaintingToGhost] Interact キーが一度離されたので、次の押下から幽霊に渡せる状態になりました");
            }
        }

        prevAllPickedUp = allPickedUp;

        // プレイヤーとこのゴーストとの距離を測る
        float distance = Vector3.Distance(player.position, transform.position);
        bool inRangeNow = distance <= interactDistance;

        // 範囲に出入りしたタイミングでUI ON/OFF
        if (inRangeNow != isPlayerInRange)
        {
            isPlayerInRange = inRangeNow;
            Debug.Log($"[GivePaintingToGhost] inRangeNow={inRangeNow}, distance={distance}");
            SetInteractUI(isPlayerInRange);
        }

        // ★ 実際に「次の Interact 押下」で渡す判定
        if (_input != null &&
            isPlayerInRange &&
            readyForNewInteract &&          // ← ここがポイント
            _input.Player.Interact.WasPressedThisFrame())
        {
            Debug.Log("[GivePaintingToGhost] Player.Interact.WasPressedThisFrame() && readyForNewInteract==true → 処理実行");
            HandleGivePaintingAndMaybeChangeScene();
        }
    }

    /// <summary>
    /// インタラクト用テキストUIの表示・非表示
    /// </summary>
    private void SetInteractUI(bool show)
    {
        if (interactMessageUI != null)
        {
            interactMessageUI.SetActive(show);
        }
    }

    /// <summary>
    /// 実際に「幽霊に絵を渡す」処理とシーン遷移を行う
    /// </summary>
    private void HandleGivePaintingAndMaybeChangeScene()
    {
        // 多重実行防止
        if (HasGivenPainting)
        {
            Debug.Log("[GivePaintingToGhost] すでに HasGivenPainting=true のため何もしません");
            return;
        }

        if (paintingPuzzleManager == null || player == null)
        {
            Debug.LogWarning("[GivePaintingToGhost] 必要な参照が null のため中断");
            return;
        }

        // 念のため条件をもう一度チェック
        if (!paintingPuzzleManager.AllCorrectPickedUp)
        {
            Debug.Log("[GivePaintingToGhost] AllCorrectPickedUp==false のため中断");
            return;
        }

        if (!isPlayerInRange)
        {
            Debug.Log("[GivePaintingToGhost] プレイヤーが範囲外のため中断");
            return;
        }

        Debug.Log("[GivePaintingToGhost] 幽霊に絵を渡しました");

        // このゴーストはもう絵を受け取った扱い
        HasGivenPainting = true;

        // UI を消す
        SetInteractUI(false);

        // このゴースト自体を消す（任意。演出で変えてOK）
        gameObject.SetActive(false);

        // シーン遷移（nextSceneName が空なら何もしない）
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            Debug.Log($"[GivePaintingToGhost] シーン遷移：{nextSceneName}");
            SceneManager.LoadScene(nextSceneName);
        }
    }

    /// <summary>
    /// シーン上でインタラクト範囲を視覚化（おまけ）
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}
