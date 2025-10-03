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
    // ここから【追加：距離→段階表示の設定】
    [Header("距離レンジ（m）")]
    [Tooltip("これ以下で近い判定（最大開示）。これ以上で遠い判定（非表示側）。")]
    [SerializeField] private float nearDistance = 1.5f;
    [SerializeField] private float farDistance = 8.0f;

    [Header("段階ゲート（0〜1：遠い=0 / 近い=1 の正規化後）")]
    [Tooltip("この値未満：何も表示しない（完全非表示）")]
    [Range(0f, 1f)] public float maskAppearGate = 0.25f;
    [Tooltip("この値以上：伏字が外れ始める（段階開示開始）。その手前は全文伏字だけ表示。")]
    [Range(0f, 1f)] public float revealGate = 0.55f;

    [Header("伏字設定")]
    [SerializeField] private char maskChar = '■';
    [Tooltip("伏字解除の順序。true=ランダム / false=左から順")]
    [SerializeField] private bool randomReveal = true;

    [Header("見え方（任意）")]
    [Tooltip("透明度演出を使うか。使わない場合は文字の有無のみで切替。")]
    [SerializeField] private bool useAlphaFade = true;
    [Range(0f, 1f)] public float alphaHidden = 0.0f; // 非表示ゾーン
    [Range(0f, 1f)] public float alphaMask = 0.9f; // 全文伏字ゾーン
    [Range(0f, 1f)] public float alphaNear = 1.0f; // 開示ゾーン

    [Tooltip("立ち上がりカーブ（0=遠い, 1=近い）")]
    [SerializeField]
    private AnimationCurve revealCurve =
        AnimationCurve.EaseInOut(0, 0, 1, 1);
    // ここまで【追加】
    // -----------------------------------------

    private string _currentLine = "";      // 元テキスト
    private int[] _revealOrder;            // ランダム開示用のインデックス
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

        // 距離 → 0~1 正規化（近いほど 1）
        float d = Vector3.Distance(Player.position, Ghost.position);
        float t = Mathf.InverseLerp(farDistance, nearDistance, d);
        t = Mathf.Clamp01(t);

        // カーブ適用後の“近さ”
        float k = revealCurve.Evaluate(t);

        // -----------------------------------------
        // ここから【段階表示の本体ロジック】
        if (k < maskAppearGate)
        {
            // 段階1：完全非表示
            if (useAlphaFade)
            {
                var c = textUI.color; c.a = alphaHidden; textUI.color = c;
            }
            textUI.text = ""; // 文字自体出さない（読み取り防止）
        }
        else if (k < revealGate)
        {
            // 段階2：全文伏字だけ見える
            if (useAlphaFade)
            {
                var c = textUI.color; c.a = alphaMask; textUI.color = c;
            }
            textUI.text = MakeMask(_currentLine.Length, maskChar);
        }
        else
        {
            // 段階3：伏字が少しずつ外れる
            if (useAlphaFade)
            {
                var c = textUI.color; c.a = alphaNear; textUI.color = c;
            }
            // revealGate〜1.0 を 0〜1 に再正規化して開示割合に
            float local01 = Mathf.InverseLerp(revealGate, 1f, k);
            textUI.text = Obfuscate(_currentLine, local01, maskChar, randomReveal);
        }
        // ここまで【段階表示の本体ロジック】
        // -----------------------------------------
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

            // 元文更新（未入力ならファイル名）
            _currentLine = string.IsNullOrEmpty(entry.transcript) ? entry.clip.name : entry.transcript;
            BuildRevealOrder(_currentLine.Length);

            // 再生
            audioSource.clip = entry.clip;
            audioSource.Play();

            float wait = entry.clip.length + Mathf.Max(0f, gapSeconds);
            yield return new WaitForSeconds(wait);
        }
    }

    // -----------------------------------------
    // ここから【伏字ユーティリティ】
    private string MakeMask(int length, char ch)
    {
        if (length <= 0) return "";
        return new string(ch, length);
    }

    // revealRatio（0~1）に応じて一部だけ本来の文字を見せる
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
            for (int i = 0; i < revealCount && i < _revealOrder.Length; i++)
            {
                int idx = _revealOrder[i];
                buff[idx] = src[idx];
            }
        }
        else
        {
            for (int i = 0; i < revealCount; i++)
                buff[i] = src[i];
        }
        return new string(buff);
    }

    private void BuildRevealOrder(int length)
    {
        _revealOrder = new int[length];
        for (int i = 0; i < length; i++) _revealOrder[i] = i;

        // フィッシャー–イェーツ
        for (int i = length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_revealOrder[i], _revealOrder[j]) = (_revealOrder[j], _revealOrder[i]);
        }
    }
    // -----------------------------------------
}
