using UnityEngine;
using CriWare;

/// <summary>
/// BGM 用の CriAtomSource を監視して、
/// 再生が止まったら自動で Play し直す「無理やり復活くん」。
/// </summary>
public class BgmAutoRestarter : MonoBehaviour
{
    [Header("監視対象の BGM ソース")]
    [SerializeField] private CriAtomSource bgmSource;

    [Header("初期再生設定")]
    [Tooltip("OnEnable 時に一度 Play() するかどうか")]
    [SerializeField] private bool playOnEnable = true;

    private bool _wasPlaying;

    private void Reset()
    {
        // 同じ GameObject にある CriAtomSource を自動で拾う
        bgmSource = GetComponent<CriAtomSource>();
    }

    private void OnEnable()
    {
        if (bgmSource == null)
        {
            bgmSource = GetComponent<CriAtomSource>();
        }

        if (bgmSource == null)
        {
            Debug.LogWarning("[BgmAutoRestarter] CriAtomSource がアサインされていません", this);
            return;
        }

        if (playOnEnable)
        {
            Debug.Log("[BgmAutoRestarter] OnEnable -> Play BGM", bgmSource);
            bgmSource.Play();
        }

        _wasPlaying = (bgmSource.status == CriAtomSource.Status.Playing);
    }

    private void Update()
    {
        if (bgmSource == null) return;

        bool isPlayingNow = (bgmSource.status == CriAtomSource.Status.Playing);

        // 以前は再生中だったのに、今フレームで再生中じゃなくなった → 強制復活
        if (_wasPlaying && !isPlayingNow)
        {
            Debug.Log(
                $"[BgmAutoRestarter] BGM stopped, force replay. cue={bgmSource.cueName}, sheet={bgmSource.cueSheet}",
                bgmSource
            );

            bgmSource.Play();

            // Play() 直後なので true にしておく
            isPlayingNow = true;
        }

        _wasPlaying = isPlayingNow;
    }
}
