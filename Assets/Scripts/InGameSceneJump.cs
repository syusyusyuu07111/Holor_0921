using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameSceneJump : MonoBehaviour
{
    [SerializeField] private string inGameSceneName = "InGame";

    // Signal Receiver／Button などから呼ぶ用（引数なし）
    public void Go()
    {
        SceneManager.LoadScene(inGameSceneName);
    }

    // 非同期で行きたい場合だけこちらを使う
    public void GoAsync()
    {
        StartCoroutine(LoadAsync());
    }

    private System.Collections.IEnumerator LoadAsync()
    {
        var op = SceneManager.LoadSceneAsync(inGameSceneName);
        while (!op.isDone) yield return null;
    }
}
