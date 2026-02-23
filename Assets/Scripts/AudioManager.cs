using UnityEngine;
using CriWare;

public class AudioManager : MonoBehaviour
{
    [Header("ドアを開ける音 (SE_dooropen)")]
    [SerializeField] private CriAtomSource doorOpenSource;

    [Header("ドアを閉める音 (SE_doorclose)")]
    [SerializeField] private CriAtomSource doorCloseSource;

    [Header("幽霊出現SE (GHOST_APPEAR)")]
    [SerializeField] private CriAtomSource GhostAppearSource;

    [Header("幽霊に捕まったきの音 (SE_catch)")]
    [SerializeField] private CriAtomSource SE_catchSource;

    [Header("足音ループ (SE_walk / loop)")]
    [SerializeField] private CriAtomSource SE_walk;

    // 足音ループの再生状態
    private bool IsFootstepPlaying = false;

    //================
    // ドアを開けた時のSE
    //================
    public void PlayDoorOpen()
    {
        PlayOneShot(doorOpenSource, "doorOpenSource");
    }

    //================
    // ドアを閉めた時のSE
    //================
    public void PlayDoorClose()
    {
        PlayOneShot(doorCloseSource, "doorCloseSource");
    }

    //================
    // 幽霊出現SE
    //================
    public void GHOSTAPPEAR()
    {
        PlayOneShot(GhostAppearSource, "GhostAppearSource");
    }

    //================
    // 幽霊に捕まった時のSE
    //================
    public void CatchSource()
    {
        PlayOneShot(SE_catchSource, "SE_catchSource");
    }

    /*
         単発SE再生共通処理
         未設定ならErrorを出す
    */
    private void PlayOneShot(CriAtomSource Source, string SourceName)
    {
        if (Source == null)
        {
            Debug.LogError($"AudioManager: {SourceName} が未設定です。");
            return;
        }

        Source.Play();
    }

    //================
    // 足音ループ開始
    //================
    public void StartFootstepLoop()
    {
        if (SE_walk == null)
        {
            Debug.LogError("AudioManager: SE_walk が未設定です。");
            return;
        }

        if (IsFootstepPlaying) return;

        SE_walk.Play();
        IsFootstepPlaying = true;
    }

    //================
    // 足音ループ停止
    //================
    public void StopFootstepLoop()
    {
        if (SE_walk == null)
        {
            Debug.LogError("AudioManager: SE_walk が未設定です。");
            return;
        }

        if (!IsFootstepPlaying) return;

        SE_walk.Stop();
        IsFootstepPlaying = false;
    }
}