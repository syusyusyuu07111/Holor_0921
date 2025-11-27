using UnityEngine;

[DefaultExecutionOrder(10)]
public class GhostScreenOrbs : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("ゴーストの出現状態を見る EnemyAI（CurrentGhost を参照）")]
    public EnemyAI Enemy;

    [Tooltip("最初からカメラの前に置いておくオーブ（パーティクル）のルートオブジェクト")]
    public GameObject OrbObject;

    [Header("起動時設定")]
    [Tooltip("Start 時に自動で非表示にするか")]
    public bool HideOnStart = true;

    void Start()
    {
        if (!OrbObject)
        {
            Debug.LogWarning("[GhostScreenOrbs] OrbObject が設定されていません。");
            return;
        }

        if (HideOnStart)
        {
            OrbObject.SetActive(false);
        }
    }

    void Update()
    {
        if (!Enemy || !OrbObject) return;

        // ゴーストがいる → 表示
        // いない → 非表示
        bool shouldShow = (Enemy.CurrentGhost != null);

        if (OrbObject.activeSelf != shouldShow)
        {
            OrbObject.SetActive(shouldShow);
        }
    }
}
