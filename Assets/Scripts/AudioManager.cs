using UnityEngine;
using CriWare;
public class AudioManager : MonoBehaviour
{
    [Header("ドアを開ける音")]
    public CriAtomSource DoorOpenSource;

    [Header("ドアを閉める音")]
    public CriAtomSource DoorCloseSource;

    //ドアを開けた時にSEを鳴らす------------------------------------------------------------------------------
    public void DoorOpenAudio()
    {
        DoorOpenSource.Play();
    }
    //---------------------------------------------------------------------------------------------------------
    //ドアをしめた時に音を鳴らす-------------------------------------------------------------------------------
    public void DoorCloseDoor()
    {
        DoorCloseSource.Play();
    }
    //------------------------------------------------------------------------------------------------------------
}
