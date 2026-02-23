using UnityEngine;
using UnityEngine.SceneManagement;

/*
     インゲームシーンへ遷移する
     ・Signal Receiver / Button など「引数なしで呼びたい」用途向け
     ・同期ロード(Go) と 非同期ロード(GoAsync) を用意
*/

public class InGameSceneJump : MonoBehaviour
{
    //================
    // Inspector設定
    //================

    [SerializeField] private string inGameSceneName = "InGame";

    //================
    // Scene Jump
    //================

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

    //================
    // Async Load
    //================

    // シーンを非同期でロード完了するまで待つ
    private System.Collections.IEnumerator LoadAsync()
    {
        var op = SceneManager.LoadSceneAsync(inGameSceneName);
        while (!op.isDone) yield return null;
    }
}