/*
このスクリプトは、落下した絵（Painting）をプレイヤーが拾えるようにする処理です。

主な役割
・絵が「落ちた状態(IsDropped=true)」になったら拾える判定を開始する
・プレイヤーが拾える距離内に入ったら「拾うテキスト」を表示する
・一定時間（落下直後の誤操作防止）を過ぎていて、かつ Interact 入力が押されたら拾う
・拾ったらパズル管理（PaintingPuzzleManager）のフラグを更新し、絵を消す
*/

using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public enum PaintingType
{
    None,
    PaintingA,
    PaintingB
}

public class PaintingPickup : MonoBehaviour
{
    [Header("このPickupが担当する親の Painting（落ちる側）")]
    [SerializeField] private Painting _painting;   // Rigidbody & Painting が付いている絵のオブジェクト

    [Header("この絵はどの種類か（A/B/ハズレ）")]
    [SerializeField] private PaintingType _paintingType = PaintingType.None;

    [Header("プレイヤーの Transform")]
    [SerializeField] private Transform _playerTransform;

    [Header("拾える距離（この距離以内なら表示＆拾える）")]
    [SerializeField] private float _pickupDistance = 2.0f;

    [Header("落ちてから拾えるまでの最低待ち時間（秒） 0なら即OK")]
    [SerializeField] private float _minPickupDelayFromDrop = 0.0f;

    [Header("拾うときに表示する TMP テキスト")]
    [SerializeField] private TextMeshProUGUI _pickupText;

    [Header("パズル全体のフラグ管理")]
    [SerializeField] private PaintingPuzzleManager _puzzleManager;

    [Header("デバッグログを出すか")]
    [SerializeField] private bool _debugLog = true;

    // すでに拾った後に二重に拾えないようにするフラグ
    private bool _isPickedUp = false;

    // InputSystem（Interact 判定用）
    private InputSystem_Actions _input;

    // 初期化：参照チェック、拾うテキスト非表示、入力インスタンス生成
    private void Awake()
    {
        // 拾うテキストは初期は非表示にする（拾える距離に入った時だけ見せる）
        if (_pickupText != null)
        {
            _pickupText.gameObject.SetActive(false);
        }
        else if (_debugLog)
        {
            Debug.LogWarning($"[{name}] _pickupText が設定されていません。テキストは表示されません。");
        }

        // 親の Painting が未設定だと「落ちたかどうか」判定ができない
        if (_painting == null && _debugLog)
        {
            Debug.LogError($"[{name}] _painting が設定されていません。親の Painting をインスペクタで割り当ててください。");
        }

        // プレイヤーが未設定だと距離判定ができない
        if (_playerTransform == null && _debugLog)
        {
            Debug.LogWarning($"[{name}] _playerTransform が設定されていません。距離判定ができません。");
        }

        // InputActions を生成（有効化は OnEnable で行う）
        _input = new InputSystem_Actions();
    }

    // 有効化：Input を有効にして Interact が読める状態にする
    private void OnEnable()
    {
        // Disable→Enable のタイミングで _input が破棄されている可能性に備えて作り直す
        if (_input == null)
        {
            _input = new InputSystem_Actions();
        }

        _input.Player.Enable();
    }

    // 無効化：Input を無効にして不要な入力読み取りを止める
    private void OnDisable()
    {
        if (_input != null)
        {
            _input.Player.Disable();
        }
    }

    // 毎フレーム：拾える状態かどうかの判定、テキスト表示、入力による拾い処理
    private void Update()
    {
        // すでに拾っていたら何もしない（多重実行防止）
        if (_isPickedUp) return;

        // 参照が無い場合は判定できないので何もしない
        if (_playerTransform == null) return;
        if (_painting == null) return;

        // まだ絵が落ちていない間は拾えない（テキストも出さない）
        if (!_painting.IsDropped)
        {
            // 「落ちる前に拾うUIが出る」事故を防ぐ
            SetTextVisible(false);

            if (_debugLog)
            {
                Debug.Log($"[{name}] まだ落ちていません。IsDropped={_painting.IsDropped}");
            }

            return;
        }

        // 落ちてから何秒経ったかを計算する
        // 意図：落下した瞬間の入力がそのまま拾いに繋がるのを防ぐ（誤操作防止）
        float elapsedFromDrop = Time.time - _painting.DroppedTime;

        // プレイヤーとの距離を計算する（高さは無視してXZ距離だけで判定する）
        // 意図：上下階などで誤判定しないようにしたい場合はここを調整する
        Vector3 p = _playerTransform.position;
        Vector3 e = transform.position;
        p.y = 0f;
        e.y = 0f;

        float distance = Vector3.Distance(e, p);

        // 拾える距離内かどうか
        bool inRange = distance <= _pickupDistance;

        // 拾うテキストの表示条件
        // 意図：落ちている＋距離内の時だけ表示する
        SetTextVisible(inRange);

        if (_debugLog)
        {
            Debug.Log($"[{name}] inRange={inRange} 距離={distance:F2}, elapsedFromDrop={elapsedFromDrop:F2}");
        }

        // 拾える条件をまとめる
        // 条件：
        // ・距離内にいる
        // ・落ちてから一定時間が経過している（誤操作防止）
        bool canPickup =
            inRange &&
            elapsedFromDrop >= _minPickupDelayFromDrop;

        // 条件が揃っていて、Interact が押されたら拾う
        if (canPickup && _input.Player.Interact.WasPressedThisFrame())
        {
            if (_debugLog)
            {
                Debug.Log($"[{name}] Interact 入力検知 → Pickup() 実行");
            }

            Pickup();
        }
    }

    // 拾うテキストの表示/非表示を切り替える
    private void SetTextVisible(bool visible)
    {
        // テキスト未設定なら表示できないので抜ける（ゲーム進行は止めない）
        if (_pickupText == null)
        {
            if (_debugLog)
            {
                Debug.LogWarning($"[{name}] SetTextVisible({visible}) したいけど _pickupText が null です。");
            }
            return;
        }

        // UI をON/OFFする
        _pickupText.gameObject.SetActive(visible);

        if (_debugLog)
        {
            Debug.Log($"[{name}] SetTextVisible({visible}) → textObj={_pickupText.gameObject.name}, activeSelf={_pickupText.gameObject.activeSelf}");
        }
    }

    // 絵を拾ったときの処理（フラグ更新、UI消し、オブジェクト非表示）
    private void Pickup()
    {
        // 多重実行防止のため、最初に拾った扱いにする
        _isPickedUp = true;

        // パズル側のフラグを更新する
        // 意図：A/Bそれぞれ拾ったかどうかを PuzzleManager が見て進行管理する
        if (_puzzleManager != null)
        {
            switch (_paintingType)
            {
                case PaintingType.PaintingA:
                    _puzzleManager.pickedUpPaintingA = true;
                    break;

                case PaintingType.PaintingB:
                    _puzzleManager.pickedUpPaintingB = true;
                    break;

                // None はハズレ扱い（フラグは更新しない）
                case PaintingType.None:
                default:
                    break;
            }
        }

        // 拾ったら表示していたテキストを消す
        SetTextVisible(false);

        if (_debugLog)
        {
            Debug.Log($"[{name}] Pickup 完了。絵を非表示にします。");
        }

        // 親の Painting を消す（Rigidbody付きの落下オブジェクト側を消したい想定）
        // 意図：Pickup側だけ消して親が残る事故を防ぐ
        if (_painting != null)
        {
            _painting.gameObject.SetActive(false);
        }
        else
        {
            // 念のため、親が無ければこのオブジェクトを消す
            gameObject.SetActive(false);
        }
    }
}