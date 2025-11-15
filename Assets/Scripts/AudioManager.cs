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

    // 足音ループの状態を自前で覚える（ループを二重で鳴らさないため）
    private bool isFootstepPlaying = false;

    // ドアを開けた時のSEを鳴らす-----------------------------------------------------------------
    public void PlayDoorOpen()
    {
        if (doorOpenSource != null)
        {
            doorOpenSource.Play();
        }
        else
        {
            Debug.LogWarning("AudioManager: doorOpenSource が割り当てられていません。");
        }
    }
    //-----------------------------------------------------------------------------------------------
    // ドアを閉めた時のSEを鳴らす--------------------------------------------------------------------
    public void PlayDoorClose()
    {
        if (doorCloseSource != null)
        {
            doorCloseSource.Play();
        }
        else
        {
            Debug.LogWarning("AudioManager: doorCloseSource が割り当てられていません。");
        }
    }
    //---------------------------------------------------------------------------------------------------
    //幽霊が出現した時のSE-------------------------------------------------------------------------------
    public void GHOSTAPPEAR()
    {
        if (GhostAppearSource != null)
        {
            GhostAppearSource.Play();
        }
        else
        {
            Debug.LogWarning("AudioManager: GhostAppearSource が割り当てられていません。");
        }
    }
    //------------------------------------------------------------------------------------------------------
    //幽霊につかまった時のSE-------------------------------------------------------------------------------
    public void CatchSource()
    {
        if (SE_catchSource != null)
        {
            SE_catchSource.Play();
        }
        else
        {
            Debug.LogWarning("AudioManager: SE_catchSource が割り当てられていません。");
        }
    }
    //----------------------------------------------------------------------------------------------------------
    // 足音ループを開始（歩いてる間だけ再生したい）
    //----------------------------------------------------------------------------------------------------------
    public void StartFootstepLoop()
    {
        if (SE_walk == null)
        {
            Debug.LogWarning("AudioManager: SE_walk が割り当てられていません。");
            return;
        }

        // すでに鳴ってるなら何もしない（重複スタート防止）
        if (isFootstepPlaying) return;

        SE_walk.Play();          // CriAtomSource 側でループ設定されたキューを再生
        isFootstepPlaying = true;
    }

    //----------------------------------------------------------------------------------------------------------
    // 足音ループを止める（立ち止まったら呼ぶ）
    //----------------------------------------------------------------------------------------------------------
    public void StopFootstepLoop()
    {
        if (SE_walk == null) return;

        if (!isFootstepPlaying) return;

        SE_walk.Stop();          // 再生停止
        isFootstepPlaying = false;
    }
    //----------------------------------------------------------------------------------------------------------
}
