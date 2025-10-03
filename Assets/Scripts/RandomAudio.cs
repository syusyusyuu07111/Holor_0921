using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class RandomAudio : MonoBehaviour
{
    [System.Serializable]
    public class ClipEntry
    {
        public AudioClip clip;                 // 再生する音声
        [TextArea] public string transcript;   // 表示する文字列
    }

    [SerializeField] private List<ClipEntry> entries = new();
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private TextMeshProUGUI textUI;

    [Header("再生間の余白（秒）")]
    [SerializeField] private float gapSeconds = 0.3f;

    [Header("プレイヤーと幽霊の距離")]
    public Transform Player;
    public Transform Ghost;

    // -----------------------------------------
    // ここから【追加：距離→可読度の設定（伏字方式）】
    [Header("距離レンジ（m）")]
    [Tooltip("これ以下で全文表示（くっきり）")]
    [SerializeField] private float nearDistance = 1.5f;
    [Tooltip("これ以上で完全に読めない（全文伏字）")]
    [SerializeField] private float farDistance = 8.0f;

    [Header("不可読化（伏字）設定")]
    [Tooltip("0~1の正規化値。これ未満は全文伏字、以上で段階的に開示")]
    [Range(0f, 1f)] public float unreadableGate = 0.35f;
    [Tooltip("伏字に使う文字")]
    [SerializeField] private char maskChar = '■';
    [Tooltip("段階的に徐々に読める（falseならゲート越えで一気に全文）")]
    [SerializeField] private bool progressiveReveal = true;
    [Tooltip("開示位置をランダムにする（falseなら左から順に開示）")]
    [SerializeField] private bool randomReveal = true;

    [Header("表示演出（任意）")]
    [Tooltip("遠い時に薄く、近い時に不透明にする（視認度の補助）")]
    [SerializeField] private bool useAlphaFade = true;
    [Range(0f, 1f)] public float alphaFar = 0.0f;
    [Range(0f, 1f)] public float alphaNear = 1.0f;

    [Tooltip("立ち上がりカーブ（0=遠い,1=近い）")]
    [SerializeField]
    private AnimationCurve revealCurve =
        AnimationCurve.EaseInOut(0, 0, 1, 1);
    // ここまで【追加】
    // -----------------------------------------

    private string _currentLine = "";      // 元テキスト
    private int[] _revealOrder;            // ランダム開示用のインデックス並び
    private System.Random _rng = new System.Random();

    private void Awake()
    {
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        if (entries.Count == 0)
        {
            Debug.LogWarning("[RandomAudio] Entries が空です。");
            return;
        }
        StartCoroutine(PlayBack());
    }

    private void Update()
    {
        if (!Player || !Ghost || !textUI) return;
        if (string.IsNullOrEmpty(_currentLine)) return;

        // ２人の距離 → 0~1正規化（近いほど1）
        float d = Vector3.Distance(Player.position, Ghost.position);
        float t = Mathf.InverseLerp(farDistance, nearDistance, d);
        t = Mathf.Clamp01(t);
        float k = revealCurve.Evaluate(t); // カーブで調整後の“近さ”

        // α演出（任意）
        if (useAlphaFade)
        {
            var c = textUI.color;
            c.a = Mathf.Lerp(alphaFar, alphaNear, k);
            textUI.color = c;
        }

        // ===== 可読テキストを生成（伏字ロジック） =====
        if (k < unreadableGate)
        {
            // ゲート未満：全文伏字
            textUI.text = MakeMask(_currentLine.Length, maskChar);
        }
        else
        {
            if (!progressiveReveal)
            {
                // 一気に全文開示
                textUI.text = _currentLine;
            }
            else
            {
                // 段階的開示：ゲートから1.0までを0~1に再正規化
                float local01 = Mathf.InverseLerp(unreadableGate, 1f, k);
                textUI.text = Obfuscate(_currentLine, local01, maskChar, randomReveal);
            }
        }
        // ============================================
    }

    private IEnumerator PlayBack()
    {
        while (true)
        {
            int idx = Random.Range(0, entries.Count);
            var entry = entries[idx];

            if (!entry.clip)
            {
                Debug.LogWarning("[RandomAudio] クリップ未設定のエントリがあります。");
                yield return null;
                continue;
            }

            // 表示する元文を更新（未入力ならファイル名を採用）
            _currentLine = string.IsNullOrEmpty(entry.transcript) ? entry.clip.name : entry.transcript;

            // ランダム開示用の並びを更新
            BuildRevealOrder(_currentLine.Length);

            // 再生
            audioSource.clip = entry.clip;
            audioSource.Play();

            // 1クリップ分＋余白だけ待機
            float wait = entry.clip.length + Mathf.Max(0f, gapSeconds);
            yield return new WaitForSeconds(wait);
        }
    }

    // -----------------------------------------
    // ここから【追加：伏字ユーティリティ】
    private string MakeMask(int length, char ch)
    {
        if (length <= 0) return "";
        return new string(ch, length);
    }

    // revealRatio（0~1）に応じて一部だけ本来の文字を見せ、残りを伏字
    // random=true なら開示位置はランダム、false なら左から順
    private string Obfuscate(string src, float revealRatio, char ch, bool random)
    {
        if (string.IsNullOrEmpty(src)) return "";
        revealRatio = Mathf.Clamp01(revealRatio);

        int n = src.Length;
        int revealCount = Mathf.RoundToInt(n * revealRatio);
        if (revealCount <= 0) return MakeMask(n, ch);
        if (revealCount >= n) return src;

        char[] buff = MakeMask(n, ch).ToCharArray();

        if (random)
        {
            // 事前に作ったランダム順で最初の revealCount 個を開示
            for (int i = 0; i < revealCount && i < _revealOrder.Length; i++)
            {
                int idx = _revealOrder[i];
                buff[idx] = src[idx];
            }
        }
        else
        {
            // 左から順に開示
            for (int i = 0; i < revealCount; i++)
                buff[i] = src[i];
        }
        return new string(buff);
    }

    private void BuildRevealOrder(int length)
    {
        _revealOrder = new int[length];
        for (int i = 0; i < length; i++) _revealOrder[i] = i;

        // フィッシャー–イェーツでランダム化
        for (int i = length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            int tmp = _revealOrder[i];
            _revealOrder[i] = _revealOrder[j];
            _revealOrder[j] = tmp;
        }
    }
    // ここまで【追加】
    // -----------------------------------------
}
