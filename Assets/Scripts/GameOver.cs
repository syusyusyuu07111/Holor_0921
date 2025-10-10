// GameOver.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

[RequireComponent(typeof(Collider))]
public class GameOver : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string gameOverScene = "GameoverScene";
    [SerializeField] private float delaySeconds = 3f;

    [Header("Refs")]
    [SerializeField] private Transform cameraTransform;     // 実際に描画しているカメラ
    [SerializeField] private GameObject ghostPrefab;        // “同じ幽霊”のプレハブ

    [Header("Face anchor (center the FACE, not the body)")]
    [Tooltip("顔(Head/Face)の子Transform。未指定ならバウンズ上寄りを自動使用")]
    [SerializeField] private Transform faceAnchor;
    [Tooltip("アンカー未指定時に、バウンズのどこを顔扱いにするか（0=底, 1=頂点）")]
    [SerializeField, Range(0f, 1f)] private float boundsAnchorT = 0.9f; // 0.85〜0.95 推奨

    [Header("Framing (tight & always visible)")]
    [Tooltip("画面高さ/幅に対して、モデルが占める割合（0.95〜0.99で“ギリ寄せ”）")]
    [SerializeField, Range(0.05f, 0.99f)] private float screenFillPercent = 0.98f;
    [Tooltip("Near Clip を跨がないための余白(m)")]
    [SerializeField] private float nearClipMargin = 0.12f;
    [SerializeField] private bool lookAtCamera = true;
    [Tooltip("3秒の間、カメラに子付けして追従（カメラが動くならON推奨）")]
    [SerializeField] private bool parentToCamera = false;

    [Header("Layer/Culling (optional)")]
    [SerializeField] private bool forceLayer = false;
    [SerializeField] private int forcedLayer = 0; // Default

    [Header("Cleanup (original)")]
    [SerializeField] private Behaviour[] componentsToDisable;
    [SerializeField] private bool detachFromParent = true;

    private bool triggered;
    private static bool s_Loading;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) Run(other);
    }
    private void OnCollisionEnter(Collision c)
    {
        if (c.collider.CompareTag("Player")) Run(c.collider);
    }

    private void Run(Collider playerCol = null)
    {
        if (triggered || s_Loading) return;
        triggered = true;

        if (cameraTransform == null)
        {
            var cam = Camera.main;
            if (cam) cameraTransform = cam.transform;
        }
        if (cameraTransform == null || ghostPrefab == null)
        {
            Debug.LogError("[GameOver] cameraTransform / ghostPrefab 未設定。即遷移します。");
            SceneLoadRunner.Load(gameOverScene, 0f);
            return;
        }

        // 旧ゴースト停止
        var root = transform.root;
        foreach (var b in componentsToDisable) if (b) b.enabled = false;
        foreach (var c in root.GetComponentsInChildren<Collider>()) c.enabled = false;
        if (playerCol)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>())
                if (c) Physics.IgnoreCollision(c, playerCol, true);
        }
        var agent = root.GetComponent<NavMeshAgent>(); if (agent) agent.enabled = false;
        var rb = root.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = true;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        foreach (var anim in root.GetComponentsInChildren<Animator>()) anim.applyRootMotion = false;
        if (detachFromParent && root.parent != null) root.SetParent(null, true);

        // 新ゴースト生成（一旦原点。後で顔アンカー基準でワープ）
        var newGhost = Instantiate(ghostPrefab, Vector3.zero, Quaternion.identity);
        if (forceLayer) SetLayerRecursively(newGhost, forcedLayer);

        // 顔が“確実に画面中央に見える”ようにワープ
        TightWarpByFaceAnchor(newGhost.transform, cameraTransform, faceAnchor, boundsAnchorT, screenFillPercent, nearClipMargin, lookAtCamera);

        if (parentToCamera) newGhost.transform.SetParent(cameraTransform, true);

        // 生成直後の再発火防止
        var selfScript = newGhost.GetComponentInChildren<GameOver>();
        if (selfScript) selfScript.enabled = false;
        foreach (var c in newGhost.GetComponentsInChildren<Collider>()) c.enabled = false;
        var agent2 = newGhost.GetComponent<NavMeshAgent>(); if (agent2) agent2.enabled = false;
        var rb2 = newGhost.GetComponent<Rigidbody>();
        if (rb2)
        {
            rb2.isKinematic = true;
#if UNITY_6000_0_OR_NEWER
            rb2.linearVelocity = Vector3.zero;
#else
            rb2.velocity = Vector3.zero;
#endif
            rb2.angularVelocity = Vector3.zero;
        }
        foreach (var anim in newGhost.GetComponentsInChildren<Animator>()) anim.applyRootMotion = false;

        Debug.Log($"[GameOver] Respawned & warped (face centered). Load in {delaySeconds}s.");

        // 旧ゴースト削除 → 別オブジェクトで待ち＆遷移
        Destroy(root.gameObject);
        SceneLoadRunner.Load(gameOverScene, delaySeconds);
    }

    /// 顔アンカー（指定 or 自動推定）を画面中央に合わせ、
    /// 高さ/幅の“ギリ収まる距離”でワープ（NearClipも考慮）
    private static void TightWarpByFaceAnchor(
        Transform ghostRoot, Transform camTr, Transform faceAnchorOpt,
        float boundsAnchorT, float fillPercent, float nearMargin, bool look)
    {
        var cam = camTr.GetComponent<Camera>();
        if (cam == null)
        {
            Vector3 fb = camTr.TransformPoint(0f, 0f, 0.3f + Mathf.Max(0.01f, nearMargin));
            Vector3 offset = Vector3.zero; // アンカー不明なのでそのまま
            ghostRoot.position = fb - offset;
            if (look) LookAtCamera(ghostRoot, camTr);
            return;
        }

        // 1) ゴーストの合成バウンズを取得
        var rends = ghostRoot.GetComponentsInChildren<Renderer>(true);
        Bounds b;
        if (rends.Length > 0)
        {
            b = new Bounds(rends[0].bounds.center, Vector3.zero);
            for (int i = 0; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        }
        else
        {
            b = new Bounds(ghostRoot.position, new Vector3(1, 1, 1)); // 最低限
        }

        // 2) 顔アンカーのワールド位置
        Transform face = faceAnchorOpt;
        if (face == null)
        {
            // 名前から自動検出（Head/Face）
            face = FindByNameContains(ghostRoot, "Head") ?? FindByNameContains(ghostRoot, "Face") ?? FindByNameContains(ghostRoot, "head") ?? FindByNameContains(ghostRoot, "face");
        }
        Vector3 faceWorld;
        if (face != null)
        {
            faceWorld = face.position;
        }
        else
        {
            // バウンズの上寄り（頭側）を仮の顔位置に
            faceWorld = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, Mathf.Clamp01(boundsAnchorT)), b.center.z);
        }

        // 3) “ギリ収まる距離 d” を高さ/幅から計算（画面高さ H(d)=2*d*tan(vfov/2)、幅 W(d)=H(d)*aspect）
        fillPercent = Mathf.Clamp(fillPercent, 0.05f, 0.99f);
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float tanHalfV = Mathf.Tan(vfov * 0.5f);

        float sizeH = Mathf.Max(0.1f, b.size.y);
        float sizeW = Mathf.Max(0.1f, b.size.x);

        float dByH = sizeH / (2f * tanHalfV * fillPercent);
        float dByW = sizeW / (2f * tanHalfV * Mathf.Max(0.01f, cam.aspect) * fillPercent);

        float minZ = cam.nearClipPlane + Mathf.Max(0.01f, nearMargin);
        float d = Mathf.Max(minZ, Mathf.Max(dByH, dByW));

        // 4) 画面中央(0.5,0.5,z=d) に “顔アンカー” を合わせる
        Vector3 desiredFaceWorld = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, d));
        // ghostRoot をどれだけ動かせば顔が desired に来るか
        Vector3 rootToFace = faceWorld - ghostRoot.position;
        ghostRoot.position = desiredFaceWorld - rootToFace;

        if (look) LookAtCamera(ghostRoot, camTr);

        Debug.DrawLine(desiredFaceWorld, camTr.position, Color.magenta, 2f);
        Debug.Log($"[GameOver] Face-centered warp d={d:F3} (near+margin={minZ:F3}) " +
                  $"bounds(H={sizeH:F2},W={sizeW:F2}) fill={fillPercent:F2} anchor={(face ? face.name : "<auto>")}");
    }

    private static Transform FindByNameContains(Transform root, string keyword)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains(keyword)) return t;
        return null;
    }

    private static void LookAtCamera(Transform t, Transform camTr)
    {
        Vector3 dir = (camTr.position - t.position);
        if (dir.sqrMagnitude > 1e-6f)
            t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    // ---- 実時間待ち→ロード（ホスト破棄に強いランナー）----
    private class SceneLoadRunner : MonoBehaviour
    {
        public static void Load(string sceneName, float delaySeconds)
        {
            if (s_Loading) return;
            var go = new GameObject("GameOverLoader");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<SceneLoadRunner>();
            runner.StartCoroutine(runner.Run(sceneName, delaySeconds));
        }
        private IEnumerator Run(string sceneName, float delaySeconds)
        {
            s_Loading = true;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[GameOverLoader] Scene '{sceneName}' が Build Settings にありません。");
                s_Loading = false;
                Destroy(gameObject);
                yield break;
            }
            float until = Time.realtimeSinceStartup + delaySeconds;
            while (Time.realtimeSinceStartup < until) yield return null;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            Destroy(gameObject);
        }
    }
}
