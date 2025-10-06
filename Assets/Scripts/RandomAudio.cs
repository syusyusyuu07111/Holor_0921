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

    // ===== 既存の段階表示パラメータ =====
    [Header("段階ゲート（0〜1：遠い=0 / 近い=1 の正規化後）")]
    [Range(0f, 1f)] public float maskAppearGate = 0.25f;
    [Range(0f, 1f)] public float revealGate = 0.55f;

    [Header("伏字設定")]
    [SerializeField] private char maskChar = '■';
    [SerializeField] private bool randomReveal = true;

    [Header("見え方（任意）")]
    [SerializeField] private bool useAlphaFade = true;
    [Range(0f, 1f)] public float alphaHidden = 0.0f;
    [Range(0f, 1f)] public float alphaMask = 0.9f;
    [Range(0f, 1f)] public float alphaNear = 1.0f;

    [SerializeField]
    private AnimationCurve revealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // ===== 高さ＆横距離スコア =====
    [Header(" 高さスコア（|dy|が小さいほど強い）")]
    [Tooltip("高さ差の減衰幅（ガウス）: 小さいほど“同じ高さ付近で急に強く”なる")]
    [SerializeField] private float elevSigma = 1.0f;      // 例: 1.0m
    [Tooltip("ガウスの指数（2=正規分布、3でより急峻）")]
    [SerializeField] private float elevPower = 2.0f;      // 2〜3 推奨

    [Header("横距離（XZ）スコア")]
    [SerializeField] private float horizNear = 2.0f;      // 近い=1 になる距離
    [SerializeField] private float horizFar = 12.0f;     // 遠い=0 になる距離
    [Tooltip("横距離スコアの曲がり（>1で近接強調）")]
    [SerializeField] private float horizExp = 0.8f;      // 0.8〜1.2

    // 別フロアは“かすかに”だけにする
    [Header(" クロスフロア対策")]
    [Tooltip("この高さ差以上は“別フロア扱い”。音量と可視スコアに上限をかける")]
    [SerializeField] private float crossFloorDy = 2.5f;   // 例: 階高に合わせて 2.5〜3.5m
    [Tooltip("別フロア時の最大音量（0〜1）。“かすかに”なら 0.01〜0.06")]
    [SerializeField] private float crossFloorMaxVolume = 0.04f;
    [Tooltip("別フロア時の最大可視スコア（0〜1）。伏字のままにしたいなら 0〜0.2 あたり")]
    [SerializeField] private float crossFloorMaxVisual = 0.15f;

    [Header("音量もスコア連動")]
    [SerializeField] private bool gateAudioByScore = true;
    [SerializeField] private float volumeSmoothTime = 0.08f;
    private float _volVel;

    private string _currentLine = "";
    private int[] _revealOrder;
    private System.Random _rng = new System.Random();

    private void Awake()
    {
        if (!audioSource) audioSource = GetComponent<AudioSource>();
        if (audioSource)
        {
            audioSource.spatialBlend = 1f;           // 3D
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            // MaxDistance / MinDistance はプロジェクトに合わせて
        }
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

        // --- 高さ差 |dy| ---
        float dyAbs = Mathf.Abs(Player.position.y - Ghost.position.y);

        //  ガウス減衰：|dy|=0 → 1、離れるほど指数的に 0 へ
        // ky = exp( - (|dy| / sigma)^power )
        float denom = Mathf.Max(0.0001f, elevSigma);
        float ky = Mathf.Exp(-Mathf.Pow(dyAbs / denom, elevPower));

        // --- 横距離（XZ） ---
        Vector2 pXZ = new Vector2(Player.position.x, Player.position.z);
        Vector2 gXZ = new Vector2(Ghost.position.x, Ghost.position.z);
        float dXZ = Vector2.Distance(pXZ, gXZ);
        float kh = Mathf.InverseLerp(horizFar, horizNear, dXZ); // 遠→近で 0→1
        // 近接を少しだけ強調/緩和
        kh = Mathf.Pow(Mathf.Clamp01(kh), Mathf.Max(0.0001f, horizExp));

        // 合成：高さ最優先（乗算）。どちらかが低ければ全体も低い
        float tCombined = Mathf.Clamp01(ky * kh);

        //  クロスフロア時のキャップ
        bool crossFloor = dyAbs >= crossFloorDy;
        float visualScore = tCombined;
        float audioScore = tCombined;
        if (crossFloor)
        {
            visualScore = Mathf.Min(visualScore, crossFloorMaxVisual);
            audioScore = Mathf.Min(audioScore, crossFloorMaxVolume);
        }

        // UI（伏字段階）は既存のカーブに通す
        float k = revealCurve.Evaluate(visualScore);
        ApplyVisibility(k);

        // 音量も追従
        if (gateAudioByScore && audioSource)
        {
            float v = Mathf.SmoothDamp(audioSource.volume, audioScore, ref _volVel, volumeSmoothTime);
            audioSource.volume = v;
        }
    }

    private void ApplyVisibility(float k)
    {
        if (k < maskAppearGate)
        {
            if (useAlphaFade) { var c = textUI.color; c.a = alphaHidden; textUI.color = c; }
            textUI.text = "";
        }
        else if (k < revealGate)
        {
            if (useAlphaFade) { var c = textUI.color; c.a = alphaMask; textUI.color = c; }
            textUI.text = MakeMask(_currentLine.Length, maskChar);
        }
        else
        {
            if (useAlphaFade) { var c = textUI.color; c.a = alphaNear; textUI.color = c; }
            float local01 = Mathf.InverseLerp(revealGate, 1f, k);
            textUI.text = Obfuscate(_currentLine, local01, maskChar, randomReveal);
        }
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

            _currentLine = string.IsNullOrEmpty(entry.transcript) ? entry.clip.name : entry.transcript;
            BuildRevealOrder(_currentLine.Length);

            audioSource.clip = entry.clip;
            audioSource.Play();

            float wait = entry.clip.length + Mathf.Max(0f, gapSeconds);
            yield return new WaitForSeconds(wait);
        }
    }

    // 伏字-----------------------------------------------------------------
    private string MakeMask(int length, char ch)
    {
        if (length <= 0) return "";
        return new string(ch, length);
    }

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

        for (int i = length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_revealOrder[i], _revealOrder[j]) = (_revealOrder[j], _revealOrder[i]);
        }
    }
}
