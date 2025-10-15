using UnityEngine;
using System.Collections.Generic;
using TMPro;                                             // TMP_Text 用
using UnityEngine.UI;

public class HintText : MonoBehaviour
{
    public Transform Player;                             // プレイヤー
    public Transform Ghost;                              // ゴースト中心
    public SearchChase ChaseRef;                         // ゴースト状態(1/2)参照
    public HideCroset HideRef;                           // 隠れ状態（必要なら）

    // --------------- 表示（UI/3D 両対応） ---------------
    public TMP_Text[] HintLabels = new TMP_Text[5];      // 5つのテキスト（UIでも3DでもOK）
    public Canvas UICanvas;                               // Screen Space の Canvas（UI使用時）
    public bool ScreenSpaceUI = true;                     // true: Screen Space UI / false: 3Dテキスト

    // --------------- 進行管理 ---------------
    [System.Serializable]
    public class HintSet                                   // ステージごとの文言セット
    {
        [TextArea] public string[] State1 = new string[5]; // 状態1用 5本
        [TextArea] public string[] State2 = new string[5]; // 状態2用 5本
    }
    public List<HintSet> Stages = new List<HintSet>();     // 進行段階ごとのヒント
    public int ProgressStage = 0;                           // 初期0（インスペクタで上書き可）

    // --------------- 距離/開示 ---------------
    public float VisibleDistance = 10f;                    // ここより近いと伏字で出現
    public float RevealDistance = 7f;                      // ここより近いと開示が進む
    public float RevealCharsPerSecond = 6f;                // 秒あたり開示文字数
    public char MaskChar = '■';                            // 伏字文字

    // --------------- 配置演出 ---------------
    public float RingRadius = 1.8f;                        // ゴースト周りの半径
    public float OrbitSpeed = 20f;                         // 周回スピード（度/秒）
    public float BobAmplitude = 0.15f;                     // 縦ゆらぎ
    public float BobSpeed = 2.0f;                          // 縦ゆらぎ速度
    public float HeightOffset = 1.6f;                      // ベース高さ

    // --------------- 内部 ---------------
    private string[] activeLines = new string[5];          // 現在のステージ/状態の5行
    private int currentIndex = 0;                          // 今開示中の行（0→4）
    private float revealProgressChars = 0f;                // 現行の開示文字数
    private int cachedState = -1;                          // 前フレームの状態キャッシュ
    private int cachedStage = -1;                          // 前フレームのステージキャッシュ

    void Start()
    {
        // 進行状態の初期確認（初期は0に寄せる） -----------------------------------
        ProgressStage = Mathf.Max(0, ProgressStage);       // 初期0固定
        SelectLinesByStageAndState();                      // ステージ&状態から5行を決定
        ApplyMaskedAll();                                  // 全伏字で初期化

        // 距離外は非表示 -----------------------------------------------------------
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        if (!Player || !Ghost) return;

        // 進行状態が進む条件の確認（フック／中身は空白） ---------------------------
        CheckAndMaybeAdvanceProgress();                    // ここに進行条件を書く（今は空）

        // 状態/ステージが変わっていればラインを再選択 -----------------------------
        SelectLinesByStageAndState();

        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleNow = dist <= VisibleDistance;

        // 可視/不可視切替 ---------------------------------------------------------
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(visibleNow);
        if (!visibleNow) return;

        // リング配置の更新 ---------------------------------------------------------
        AnimateRingLayout();

        // 開示進行（RevealDistance 内 & まだ5行に到達していない時） --------------
        if (dist <= RevealDistance && currentIndex < 5)
        {
            revealProgressChars += RevealCharsPerSecond * Time.deltaTime; // 少しずつ開示
            UpdateMaskedLine(currentIndex, revealProgressChars);

            if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
            {
                currentIndex = Mathf.Min(currentIndex + 1, 4);            // 次の行へ
                revealProgressChars = 0f;                                  // カウンタリセット
            }
        }

        // 他行の見え方を整える -----------------------------------------------------
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i];      // 完全開示済
            else if (i == currentIndex) { /* UpdateMaskedLineで反映済み */ }
            else HintLabels[i].text = MaskAll(activeLines[i]); // 未着手は全伏字
        }
    }

    // --------------- ステージ＆状態でヒント5本を選ぶ ---------------
    private void SelectLinesByStageAndState()
    {
        int state = (ChaseRef ? ChaseRef.GetState() : 1); // 1/2（SearchChaseの固定状態）
        if (Stages == null || Stages.Count == 0)
        {
            EnsureActiveEmpty();                          // 文言未設定の安全策
            return;
        }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];
        var source = (state == 2) ? set.State2 : set.State1;

        // 変化が無いならスキップ
        if (cachedState == state && cachedStage == stage && IsSameLines(activeLines, source)) return;

        // ライン差し替え
        for (int i = 0; i < 5; i++)
            activeLines[i] = (source != null && i < source.Length && !string.IsNullOrEmpty(source[i])) ? source[i] : "";

        // 変更時は最初の行からやり直し
        currentIndex = 0;
        revealProgressChars = 0f;
        ApplyMaskedAll();

        cachedState = state;
        cachedStage = stage;
    }

    private bool IsSameLines(string[] a, string[] b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < 5; i++)
        {
            var aa = (i < a.Length) ? a[i] : null;
            var bb = (i < b.Length) ? b[i] : null;
            if (aa != bb) return false;
        }
        return true;
    }

    private void EnsureActiveEmpty()
    {
        for (int i = 0; i < 5; i++) activeLines[i] = "";
    }

    // --------------- 進行状態チェック（空白フック） ---------------
    private void CheckAndMaybeAdvanceProgress()
    {
        // ここに「進行状態を進める条件」を書く（空白）
        // 例：
        // if ( /* 進行を進める条件 */ )
        // {
        //     AdvanceProgress();                          // ステージを1つ進める
        // }

        // 戻す条件があるなら：
        // if ( /* 戻す条件 */ )
        // {
        //     SetProgress(0);                             // 任意の段階へ
        // }
    }

    public void AdvanceProgress() { SetProgress(ProgressStage + 1); } // 進める
    public void SetProgress(int next)
    {
        int clamped = Mathf.Clamp(next, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));
        if (clamped == ProgressStage) return;
        ProgressStage = clamped;
        currentIndex = 0; revealProgressChars = 0f;
        SelectLinesByStageAndState();                    // 反映
    }

    // --------------- 表示ユーティリティ ---------------
    private void ApplyMaskedAll()
    {
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].text = MaskAll(i < activeLines.Length ? activeLines[i] : "");
    }

    private void UpdateMaskedLine(int index, float revealedChars)
    {
        if (index < 0 || index >= activeLines.Length) return;
        if (!HintLabels[index]) return;
        string src = activeLines[index];
        int count = Mathf.Clamp(Mathf.FloorToInt(revealedChars), 0, src.Length);
        HintLabels[index].text = RevealLeftToRight(src, count);
    }

    private string MaskAll(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(MaskChar, s.Length);
    }

    private string RevealLeftToRight(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        n = Mathf.Clamp(n, 0, s.Length);
        return s.Substring(0, n) + new string(MaskChar, s.Length - n);
    }

    private bool IsFullyRevealed(string s, float revealedChars)
    {
        return Mathf.FloorToInt(revealedChars) >= (s?.Length ?? 0);
    }

    // --------------- リング配置（UI/3D 切替） ---------------
    private void AnimateRingLayout()
    {
        float t = Time.time;
        Camera cam = Camera.main;

        for (int i = 0; i < HintLabels.Length; i++)
        {
            var label = HintLabels[i];
            if (!label) continue;

            float angleDeg = (360f / Mathf.Max(1, HintLabels.Length)) * i + t * OrbitSpeed; // 周回
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 around = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * RingRadius;
            float bob = Mathf.Sin(t * BobSpeed + i * 0.6f) * BobAmplitude;

            Vector3 worldPos = Ghost.position + around + Vector3.up * (HeightOffset + bob);

            if (ScreenSpaceUI && UICanvas)                      // Screen Space UI
            {
                Vector3 screen = cam ? cam.WorldToScreenPoint(worldPos) : worldPos; // ワールド→スクリーン
                (label.transform as RectTransform).position = screen;               // 画面座標に配置
            }
            else                                                // 3D TextMeshPro
            {
                label.transform.position = worldPos;           // ワールド座標に配置
                if (cam) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position); // カメラへ面向き
            }
        }
    }

    // --------------- デバッグGizmos ---------------
    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;
        Gizmos.color = Color.white; Gizmos.DrawWireSphere(Ghost.position, VisibleDistance); // 伏字で出現距離
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(Ghost.position, RevealDistance);  // 開示進行距離
    }
}
