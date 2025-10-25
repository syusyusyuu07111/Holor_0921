using UnityEngine;
using CriWare;

public class AudioManager : MonoBehaviour
{
    [Header("ドアを開ける音 (SE_dooropen)")]
    [SerializeField] private CriAtomSource doorOpenSource;

    [Header("ドアを閉める音 (SE_doorclose)")]
    [SerializeField] private CriAtomSource doorCloseSource;

    [Header("幽霊出現SE (CRI_CUESHEET_0_SE_GHOST_APPEAR)")]
    [SerializeField] private CriAtomSource GhostAppearSource;

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
}
