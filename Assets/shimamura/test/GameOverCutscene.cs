using System.Collections;
using UnityEngine;

public class GameOverCutscene: MonoBehaviour
{
    [Header("参照設定")]
    [SerializeField] private GameOver gameOver;   // 監視する GameOver スクリプト
    [SerializeField] private Camera mainCamera;   // メインカメラ
    [SerializeField] private Transform ghost;     // 幽霊
    [SerializeField] private float cameraMoveTime = 2f; // ズーム時間
    [SerializeField] private Vector3 offset = new Vector3(0, 1.2f, -1.5f); // 幽霊に対する位置
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private bool startedZoom = false;

    private void Update()
    {
        // GameOverスクリプトの内部フラグがtrueになった瞬間を検知
        if (gameOver != null && IsGameOverFired(gameOver) && !startedZoom)
        {
            startedZoom = true;
            StartCoroutine(CameraZoom());
        }
    }

    private IEnumerator CameraZoom()
    {
        if (mainCamera == null || ghost == null) yield break;

        Transform camTr = mainCamera.transform;
        Vector3 startPos = camTr.position;
        Quaternion startRot = camTr.rotation;

        Vector3 targetPos = ghost.position + ghost.TransformDirection(offset);
        Quaternion targetRot = Quaternion.LookRotation(ghost.position - targetPos);

        float elapsed = 0f;
        while (elapsed < cameraMoveTime)
        {
            float t = ease.Evaluate(elapsed / cameraMoveTime);
            camTr.position = Vector3.Lerp(startPos, targetPos, t);
            camTr.rotation = Quaternion.Slerp(startRot, targetRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ?? private フィールドでも反射で安全にチェックできる
    private bool IsGameOverFired(GameOver go)
    {
        var field = typeof(GameOver).GetField("_gameOverFired",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null && (bool)field.GetValue(go);
    }
}