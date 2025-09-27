using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomAudio : MonoBehaviour
{
   [SerializeField]private List<AudioClip> ClipList = new();
    public AudioSource audioSource;
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        StartCoroutine("PlayBack");
    }
    IEnumerator PlayBack()
    {
        while(true)
        {
            int idx = Random.Range(0, ClipList.Count);
            AudioClip clip = ClipList[idx];
            audioSource.clip = clip;
            audioSource.Play();
            yield return new WaitForSeconds(5.0f);
        }
    }
}
