using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class RandomAudio : MonoBehaviour
{
    /*
        =========================
        このスクリプトがやること
        =========================

        ■目的
        ・「音声クリップ（幽霊の声など）」をランダムにループ再生する
        ・同時に、字幕（transcript）を TextMeshPro に表示する
        ・ただし字幕は “距離/高さ関係” によって見え方が変わる
            - 遠い：表示しない（透明 or 空）
            - 中間：全部伏字（■■■■）
            - 近い：徐々に伏字がほどけて全文に近づく
        ・別フロア（高さ差が大きい）と判定した場合は
            - 字幕の見え方に上限をかける（ほぼ読めない）
            - 音量も上限をかける（かすかに聞こえるだけ）

        ■動きの流れ
        1) Start() で PlayBack() コルーチン開始
        2) PlayBack() が entries からランダムに1つ選び
           - _currentLine（字幕に使う文章）を更新
           - AudioSource で clip を再生
           - clip.length + gapSeconds だけ待つ
           をずっと繰り返す
        3) Update() では毎フレーム
           - Player と Ghost の「高さ差」と「横距離（XZ）」からスコア(0〜1)を作る
           - スコアが低いほど見えない/伏字
           - スコアが高いほど伏字がほどける
           - さらに crossFloor（別フロア）なら上限でキャップする
           - 音量もスコアに追従させる（任意）
    */

    // ------------------------------------------------------------
    // entries：再生する音声と、表示する字幕のセット
    // ------------------------------------------------------------
    [System.Serializable]
    public class ClipEntry
    {
        public AudioClip clip;                 // 再生する音声
        [TextArea] public string transcript;   // 表示する文字列（未指定なら clip名）
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
    [Range(0f, 1f)] public float maskAppearGate = 0.25f; // ここ未満は“非表示”
    [Range(0f, 1f)] public float revealGate = 0.55f;     // ここ未満は“伏字”、超えると“ほどける”

    [Header("伏字設定")]
    [SerializeField] private char maskChar = '■';        // 伏字に使う文字
    [SerializeField] private bool randomReveal = true;   // true: ランダムに文字が解ける / false: 左から順に

    [Header("見え方（任意）")]
    [SerializeField] private bool useAlphaFade = true;   // 透明度でフェードするか
    [Range(0f, 1f)] public float alphaHidden = 0.0f;     // 非表示時のアルファ
    [Range(0f, 1f)] public float alphaMask = 0.9f;       // 伏字時のアルファ
    [Range(0f, 1f)] public float alphaNear = 1.0f;       // 近い時のアルファ

    [SerializeField]
    private AnimationCurve revealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // スコア→見え方の曲線

    // ===== 高さ＆横距離スコア =====
    [Header(" 高さスコア（|dy|が小さいほど強い）")]
    [Tooltip("高さ差の減衰幅（ガウス）: 小さいほど“同じ高さ付近で急に強く”なる")]
    [SerializeField] private float elevSigma = 1.0f;      // 例: 1.0m
    [Tooltip("ガウスの指数（2=正規分布、3でより急峻）")]
    [SerializeField] private float elevPower = 2.0f;      // 2〜3 推奨

    [Header("横距離（XZ）スコア")]
    [SerializeField] private float horizNear = 2.0f;      // 近い=1 になる距離
    [SerializeField] private float horizFar = 12.0f;      // 遠い=0 になる距離
    [Tooltip("横距離スコアの曲がり（>1で近接強調）")]
    [SerializeField] private float horizExp = 0.8f;       // 0.8〜1.2

    // 別フロアは“かすかに”だけにする
    [Header(" クロスフロア対策")]
    [Tooltip("この高さ差以上は“別フロア扱い”。音量と可視スコアに上限をかける")]
    [SerializeField] private float crossFloorDy = 2.5f;   // 例: 階高に合わせて 2.5〜3.5m
    [Tooltip("別フロア時の最大音量（0〜1）。“かすかに”なら 0.01〜0.06")]
    [SerializeField] private float crossFloorMaxVolume = 0.04f;
    [Tooltip("別フロア時の最大可視スコア（0〜1）。伏字のままにしたいなら 0〜0.2 あたり")]
    [SerializeField] private float crossFloorMaxVisual = 0.15f;

    [Header("音量もスコア連動")]
    [SerializeField] private bool gateAudioByScore = true;  // trueなら音量が距離スコアに追従
    [SerializeField] private float volumeSmoothTime = 0.08f; // SmoothDamp 用
    private float _volVel;                                   // SmoothDamp の速度バッファ

    // 現在再生中クリップの字幕（Updateがこれを加工して表示）
    private string _currentLine = "";

    // randomReveal 用：伏字をどの順で解くか（毎回シャッフルして使う）
    private int[] _revealOrder;
    private System.Random _rng = new System.Random();

    private void Awake()
    {
        // AudioSource 未指定なら自分から取る
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        // 3D音にしたいので Awake で基本設定（必要ならプロジェクトに合わせて調整）
        if (audioSource)
        {
            audioSource.spatialBlend = 1f;                 // 3D（0=2D, 1=3D）
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            // minDistance / maxDistance は Inspector で調整推奨
        }
    }

    private void Start()
    {
        // entries が無いと再生できないので警告
        if (entries.Count == 0)
        {
            Debug.LogWarning("[RandomAudio] Entries が空です。");
            return;
        }

        // ランダム再生ループ開始
        StartCoroutine(PlayBack());
    }

    private void Update()
    {
        // 必要参照が無ければ何もしない
        if (!Player || !Ghost || !textUI) return;

        // まだ字幕がセットされてないなら何もしない
        if (string.IsNullOrEmpty(_currentLine)) return;

        // =========================================================
        // 1) 高さ差 |dy| から「高さスコア ky」を作る（0〜1）
        //    ・同じ高さほど 1
        //    ・高さが離れるほど指数的に 0
        // =========================================================
        float dyAbs = Mathf.Abs(Player.position.y - Ghost.position.y);

        // ky = exp( - (|dy| / sigma)^power )
        float denom = Mathf.Max(0.0001f, elevSigma);
        float ky = Mathf.Exp(-Mathf.Pow(dyAbs / denom, elevPower));

        // =========================================================
        // 2) 横距離（XZ）から「横距離スコア kh」を作る（0〜1）
        //    ・horizNear より近いほど 1 に近い
        //    ・horizFar より遠いほど 0 に近い
        // =========================================================
        Vector2 pXZ = new Vector2(Player.position.x, Player.position.z);
        Vector2 gXZ = new Vector2(Ghost.position.x, Ghost.position.z);
        float dXZ = Vector2.Distance(pXZ, gXZ);

        // InverseLerp(遠い, 近い, 現在距離) → 遠い=0 / 近い=1
        float kh = Mathf.InverseLerp(horizFar, horizNear, dXZ);

        // 近接の感じ方を調整（0.8なら少し緩く、>1なら近接を強調）
        kh = Mathf.Pow(Mathf.Clamp01(kh), Mathf.Max(0.0001f, horizExp));

        // =========================================================
        // 3) 合成スコア（高さ優先の乗算）
        //    ・どちらかが低いと全体も低い
        // =========================================================
        float tCombined = Mathf.Clamp01(ky * kh);

        // =========================================================
        // 4) 別フロア判定（dyが大きい）
        //    ・visual と audio に上限をかける
        // =========================================================
        bool crossFloor = dyAbs >= crossFloorDy;

        float visualScore = tCombined;
        float audioScore = tCombined;

        if (crossFloor)
        {
            // “かすかに”に制限
            visualScore = Mathf.Min(visualScore, crossFloorMaxVisual);
            audioScore = Mathf.Min(audioScore, crossFloorMaxVolume);
        }

        // =========================================================
        // 5) UIの見え方にカーブを通して、段階表示する
        // =========================================================
        float k = revealCurve.Evaluate(visualScore);
        ApplyVisibility(k);

        // =========================================================
        // 6) 音量もスコアに追従させる（任意）
        // =========================================================
        if (gateAudioByScore && audioSource)
        {
            // SmoothDampで急変しないようにする
            float v = Mathf.SmoothDamp(audioSource.volume, audioScore, ref _volVel, volumeSmoothTime);
            audioSource.volume = v;
        }
    }

    // ------------------------------------------------------------
    // ApplyVisibility
    // ・スコアk(0〜1)に応じて字幕の段階表示を行う
    //   1) k < maskAppearGate → 表示なし
    //   2) maskAppearGate <= k < revealGate → 全伏字
    //   3) revealGate <= k → 徐々に解読（伏字がほどける）
    // ------------------------------------------------------------
    private void ApplyVisibility(float k)
    {
        // ① まだ遠い：何も表示しない
        if (k < maskAppearGate)
        {
            if (useAlphaFade)
            {
                var c = textUI.color;
                c.a = alphaHidden;
                textUI.color = c;
            }
            textUI.text = "";
        }
        // ② 中距離：全文伏字
        else if (k < revealGate)
        {
            if (useAlphaFade)
            {
                var c = textUI.color;
                c.a = alphaMask;
                textUI.color = c;
            }
            textUI.text = MakeMask(_currentLine.Length, maskChar);
        }
        // ③ 近距離：伏字がほどける
        else
        {
            if (useAlphaFade)
            {
                var c = textUI.color;
                c.a = alphaNear;
                textUI.color = c;
            }

            // revealGate〜1 の範囲を 0〜1 に正規化
            float local01 = Mathf.InverseLerp(revealGate, 1f, k);

            // local01 に応じて伏字解除（ランダム or 左から）
            textUI.text = Obfuscate(_currentLine, local01, maskChar, randomReveal);
        }
    }

    // ------------------------------------------------------------
    // PlayBack
    // ・entries からランダムに1つ選んで再生し続けるループ
    // ・再生開始時に _currentLine を更新する（Updateがこれを表示加工する）
    // ------------------------------------------------------------
    private IEnumerator PlayBack()
    {
        while (true)
        {
            int idx = Random.Range(0, entries.Count);
            var entry = entries[idx];

            // clip が無いエントリはスキップ
            if (!entry.clip)
            {
                Debug.LogWarning("[RandomAudio] クリップ未設定のエントリがあります。");
                yield return null;
                continue;
            }

            // 字幕：transcript が空なら clip名を使う
            _currentLine = string.IsNullOrEmpty(entry.transcript) ? entry.clip.name : entry.transcript;

            // ランダム解除用の順番を作る
            BuildRevealOrder(_currentLine.Length);

            // 再生
            audioSource.clip = entry.clip;
            audioSource.Play();

            // 次の再生まで待機（クリップ長 + gap）
            float wait = entry.clip.length + Mathf.Max(0f, gapSeconds);
            yield return new WaitForSeconds(wait);
        }
    }

    // ------------------------------------------------------------
    // MakeMask
    // ・length文字分の伏字（■■■■）を作る
    // ------------------------------------------------------------
    private string MakeMask(int length, char ch)
    {
        if (length <= 0) return "";
        return new string(ch, length);
    }

    // ------------------------------------------------------------
    // Obfuscate
    // ・revealRatio(0〜1)に応じて伏字を解除して返す
    // ・random=true なら _revealOrder の順でランダム解除
    // ・random=false なら左から順に解除
    // ------------------------------------------------------------
    private string Obfuscate(string src, float revealRatio, char ch, bool random)
    {
        if (string.IsNullOrEmpty(src)) return "";
        revealRatio = Mathf.Clamp01(revealRatio);

        int n = src.Length;

        // 何文字見せるか（割合→文字数）
        int revealCount = Mathf.RoundToInt(n * revealRatio);

        // 0なら全部伏字、全部なら全文
        if (revealCount <= 0) return MakeMask(n, ch);
        if (revealCount >= n) return src;

        // まず全部伏字で埋める
        char[] buff = MakeMask(n, ch).ToCharArray();

        if (random)
        {
            // ランダム解除：_revealOrder で決めた順に revealCount 文字だけ見せる
            for (int i = 0; i < revealCount && i < _revealOrder.Length; i++)
            {
                int idx = _revealOrder[i];
                buff[idx] = src[idx];
            }
        }
        else
        {
            // 左から順に revealCount 文字見せる
            for (int i = 0; i < revealCount; i++)
                buff[i] = src[i];
        }

        return new string(buff);
    }

    // ------------------------------------------------------------
    // BuildRevealOrder
    // ・0..length-1 の配列を作ってシャッフルする（Fisher-Yates）
    // ・randomReveal=true の時に「どの文字から解けるか」の順番になる
    // ------------------------------------------------------------
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