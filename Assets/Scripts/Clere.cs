using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Clere : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Player"))
        {
            SceneManager.LoadScene("Clere");
        }
    }
}
