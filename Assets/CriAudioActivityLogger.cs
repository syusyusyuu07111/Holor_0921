using System.Collections.Generic;
using UnityEngine;
using CriWare;

public class CriAudioActivityLogger : MonoBehaviour
{
    [Header("Polling")]
    [Tooltip("何秒ごとにチェックするか")]
    [SerializeField] private float pollInterval = 0.1f;

    [Header("音量変化のしきい値")]
    [Tooltip("これ以上変わったときだけログを出す")]
    [SerializeField] private float volumeChangeThreshold = 0.01f;

    [Header("監視するカテゴリ名")]
    [SerializeField] private string[] categoryNames = { "BGM", "SE", "ENV" };

    private float _timer;

    // ソース単位の前回状態
    private class SourceInfo
    {
        public bool isPlaying;
        public float volume;
        public string cueSheet;
        public string cueName;
        public string path; // オブジェクト階層
        public bool is3D;
    }

    // key: InstanceID
    private readonly Dictionary<int, SourceInfo> _sourceStates = new Dictionary<int, SourceInfo>();

    // カテゴリ音量
    private readonly Dictionary<string, float> _categoryVolumes = new Dictionary<string, float>();

    private void Start()
    {
        // 初回カテゴリ音量を記録
        foreach (var cat in categoryNames)
        {
            float v = 0f;
            try
            {
                v = CriAtom.GetCategoryVolume(cat);
            }
            catch
            {
                // 存在しないカテゴリを指定していても死なないように
            }
            _categoryVolumes[cat] = v;
            Debug.Log($"[AUDIO] Init Category '{cat}' volume={v:0.00}");
        }
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < pollInterval) return;
        _timer = 0f;

        try
        {
            PollSources();
            PollCategories();
        }
        catch (System.SystemException e)
        {
            Debug.LogWarning($"[AUDIO] CriAudioActivityLogger exception: {e}");
        }
    }

    // ==== AtomSource の監視 ====
    private void PollSources()
    {
        var sources = FindObjectsOfType<CriAtomSource>(includeInactive: false);

        // どの InstanceID が今回見つかったか
        var seenIds = new HashSet<int>();

        foreach (var src in sources)
        {
            int id = src.GetInstanceID();
            seenIds.Add(id);

            bool isPlayingNow = (src.status == CriAtomSource.Status.Playing);
            float volNow = src.volume;
            string cueSheet = src.cueSheet;
            string cueName = src.cueName;
            bool is3D = src.use3dPositioning;
            string path = GetHierarchyPath(src.gameObject);

            // まだ登録されていないソース
            if (!_sourceStates.TryGetValue(id, out var info))
            {
                info = new SourceInfo
                {
                    isPlaying = isPlayingNow,
                    volume = volNow,
                    cueSheet = cueSheet,
                    cueName = cueName,
                    path = path,
                    is3D = is3D
                };
                _sourceStates[id] = info;

                if (isPlayingNow)
                {
                    Debug.Log(
                        $"[AUDIO] START  cue={cueName} sheet={cueSheet} obj={path} vol={volNow:0.00} 3D={is3D}"
                    );

                    // ★追加: 初回検出でも、すでに同じキューが他で鳴っていれば重複ログを出す
                    LogDuplicateIfExists(id, cueSheet, cueName, path);
                }

                continue;
            }

            // cue 情報や3D設定が変わった場合も一応更新
            info.cueSheet = cueSheet;
            info.cueName = cueName;
            info.path = path;
            info.is3D = is3D;

            bool wasPlaying = info.isPlaying;

            // 再生状態の変化
            if (!wasPlaying && isPlayingNow)
            {
                // 再生開始
                Debug.Log(
                    $"[AUDIO] START  cue={cueName} sheet={cueSheet} obj={path} vol={volNow:0.00} 3D={is3D}"
                );

                // ★追加: 同じキューが他で再生中なら警告
                LogDuplicateIfExists(id, cueSheet, cueName, path);
            }
            else if (wasPlaying && !isPlayingNow)
            {
                // 再生終了
                Debug.Log(
                    $"[AUDIO] STOP   cue={cueName} sheet={cueSheet} obj={path}"
                );
            }

            // 音量変化
            if (isPlayingNow)
            {
                float diff = Mathf.Abs(info.volume - volNow);
                if (diff >= volumeChangeThreshold)
                {
                    Debug.Log(
                        $"[AUDIO] VOLUME cue={cueName} sheet={cueSheet} obj={path} {info.volume:0.00} -> {volNow:0.00}"
                    );
                }
            }

            info.isPlaying = isPlayingNow;
            info.volume = volNow;
        }

        // 破棄された / シーンから消えたソースを検知
        var removedIds = new List<int>();
        foreach (var kv in _sourceStates)
        {
            if (!seenIds.Contains(kv.Key))
            {
                var info = kv.Value;
                if (info.isPlaying)
                {
                    Debug.Log(
                        $"[AUDIO] DESTROY (while playing?) cue={info.cueName} sheet={info.cueSheet} obj={info.path}"
                    );
                }
                removedIds.Add(kv.Key);
            }
        }
        foreach (var id in removedIds)
        {
            _sourceStates.Remove(id);
        }
    }

    // ★追加: 同じ cueSheet/cueName が他のソースで再生中なら警告を出す
    private void LogDuplicateIfExists(int currentId, string cueSheet, string cueName, string path)
    {
        foreach (var kv in _sourceStates)
        {
            if (kv.Key == currentId) continue;

            var other = kv.Value;
            if (!other.isPlaying) continue;

            // 同じキュー名 & シート名が別オブジェクトで再生中なら重複候補
            if (other.cueSheet == cueSheet && other.cueName == cueName)
            {
                Debug.LogWarning(
                    $"[AUDIO] DUPLICATE cue={cueName} sheet={cueSheet}\n" +
                    $"   new : obj={path}\n" +
                    $"   other: obj={other.path}"
                );
            }
        }
    }

    // ==== カテゴリ音量の監視 ====
    private void PollCategories()
    {
        foreach (var cat in categoryNames)
        {
            float prev = _categoryVolumes.TryGetValue(cat, out var p) ? p : 0f;
            float now = prev;
            bool ok = true;

            try
            {
                now = CriAtom.GetCategoryVolume(cat);
            }
            catch
            {
                // 存在しないカテゴリ名なら一旦スキップ
                ok = false;
            }

            if (!ok) continue;

            if (Mathf.Abs(prev - now) >= volumeChangeThreshold)
            {
                Debug.Log($"[AUDIO] CATEGORY '{cat}' volume {prev:0.00} -> {now:0.00}");
                _categoryVolumes[cat] = now;
            }
        }
    }

    // ==== 階層パスを文字列化（デバッグ用）====
    private static string GetHierarchyPath(GameObject obj)
    {
        if (obj == null) return "null";

        string path = obj.name;
        Transform t = obj.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
