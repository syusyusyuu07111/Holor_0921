using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 絵を2枚持っている状態で幽霊に近づくと
/// 「幽霊に絵を渡す」テキストを表示し、
/// Player.Interact 入力で幽霊に絵を渡して消える処理を行う。
/// 絵を渡せたらシーン遷移を行う。
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

    [Header("Input System の Player.Interact アクション")]
    [SerializeField] private InputActionReference interactAction;

    [Header("渡せたあとに遷移するシーン名（空なら遷移しない）")]
    [SerializeField] private string nextSceneName;

    /// <summary>プレイヤーが範囲内かどうか</summary>
    private bool isPlayerInRange = false;

    /// <summary>
    /// 幽霊に絵を渡し終わったかどうか（他スクリプトから参照用）
    /// true になったら「渡せた」と判定できる
    /// </summary>
    public bool HasGivenPainting { get; private set; } = false;

    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed += OnInteract;
            interactAction.action.Enable();
        }

        // 念のため開始時はUIを消しておく
        SetInteractUI(false);
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed -= OnInteract;
            interactAction.action.Disable();
        }
    }

    private void Update()
    {
        if (player == null || paintingPuzzleManager == null)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            return;
        }

        // すでに幽霊に絵を渡し終わっているなら、UIも判定も不要
        if (HasGivenPainting)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            return;
        }

        // 絵2枚を持っていなければ、UIは出さないし判定もしない
        if (!paintingPuzzleManager.AllCorrectPickedUp)
        {
            SetInteractUI(false);
            isPlayerInRange = false;
            return;
        }

        // プレイヤーと幽霊との距離を計測
        float distance = Vector3.Distance(player.position, transform.position);
        bool inRangeNow = distance <= interactDistance;

        // 範囲内/外に入ったタイミングでUIの表示を切り替え
        if (inRangeNow != isPlayerInRange)
        {
            isPlayerInRange = inRangeNow;
            SetInteractUI(isPlayerInRange);
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
    /// Player.Interact が押されたときに呼ばれる
    /// </summary>
    private void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        // すでに渡し終わっているなら何もしない（多重実行防止）
        if (HasGivenPainting) return;

        // 条件：絵2枚持っている ＋ 範囲内
        if (paintingPuzzleManager == null) return;
        if (!paintingPuzzleManager.AllCorrectPickedUp) return;
        if (!isPlayerInRange) return;

        Debug.Log("[GivePaintingToGhost] 幽霊に絵を渡しました");

        // 「幽霊に絵を渡せた」フラグを立てる
        HasGivenPainting = true;

        // 幽霊を消す処理（DestroyでもOK）
        gameObject.SetActive(false);

        // メッセージも非表示に
        SetInteractUI(false);

        // ここでSE再生や演出トリガーなども仕込める
        // e.g. AudioSource.PlayClipAtPoint(clip, transform.position);

        // シーン遷移（nextSceneName が空でなければ）
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
