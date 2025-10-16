using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public class GameOver : MonoBehaviour
{
    public Transform Player;                    // プレイヤー
    public Transform Ghost;                     // 幽霊（追跡側）

    [Header("判定設定")]
    public float TriggerDistance = 0.735f;          // 距離がこの値以下でゲームオーバー（デフォルト: 0）
    public float CheckInterval = 0.05f;         // 距離チェック間隔（秒）

    [Header("シーン遷移")]
    public string GameoverScene = "";           // 空なら現シーンをリロード
    public bool StopAgentsOnGameOver = true;    // 遷移直前にNavMeshAgentを止める

    private bool _gameOverFired = false;        // 多重発火防止

    void Awake()
    {
        // 参照の自動補完（未設定なら Tag を頼る）
        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) Player = p.transform;
        }
        if (!Ghost)
        {
            var g = GameObject.FindGameObjectWithTag("Ghost"); // 幽霊に "Ghost" タグがある前提（無ければ手動割当）
            if (g) Ghost = g.transform;
        }
    }

    void OnEnable()
    {
        StartCoroutine(DistanceWatchLoop());
    }

    IEnumerator DistanceWatchLoop()
    {
        var wait = new WaitForSeconds(CheckInterval);
        while (enabled && !_gameOverFired)
        {
            if (Player && Ghost)
            {
                // 距離計測 & ログ出力
                float dist = Vector3.Distance(Player.position, Ghost.position);
                Debug.Log($"[GameOver] distance = {dist:0.000}");

                // 判定：距離がトリガー値以下
                if (dist <= TriggerDistance)
                {
                    FireGameOver();
                    yield break;
                }
            }
            yield return wait;
        }
    }

    private void FireGameOver()
    {
        if (_gameOverFired) return;
        _gameOverFired = true;

        // 遷移前にNavMeshAgentを止めて暴れ防止（任意）
        if (StopAgentsOnGameOver)
        {
            var a1 = Player ? Player.GetComponent<NavMeshAgent>() : null;
            var a2 = Ghost ? Ghost.GetComponent<NavMeshAgent>() : null;
            if (a1 && a1.isOnNavMesh) a1.isStopped = true;
            if (a2 && a2.isOnNavMesh) a2.isStopped = true;
        }

        // シーン遷移
        if (!string.IsNullOrEmpty(GameoverScene))
        {
            SceneManager.LoadScene(GameoverScene);
        }
        else
        {
            // 遷移先未指定なら現シーンをリロード
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }

    // デバッグ可視化（任意）
    private void OnDrawGizmosSelected()
    {
        if (Player && Ghost)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Ghost.position, Mathf.Max(TriggerDistance, 0.01f));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Player.position, Ghost.position);
        }
    }
}
