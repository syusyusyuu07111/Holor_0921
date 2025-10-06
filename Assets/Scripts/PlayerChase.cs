using UnityEngine;

public class PlayerChase : MonoBehaviour
{
    [Header("参照")]
    public Transform Player;      // プレイヤー
    public Transform Ghost;       // ゴースト

    [Header("移動設定")]
    public float moveSpeed = 3.0f;
    public float stopDistance = 0f;

    private bool isChasing = false;

    // -----------------------------------------
    // ここから【追加：怒りエフェクト】
    [Header("怒りエフェクト")]
    [Tooltip("追跡中に表示するエフェクトのPrefab（ParticleSystem等）")]
    [SerializeField] private GameObject angryEffectPrefab;

    [Tooltip("ゴーストのローカル座標でのオフセット（頭上に出したい時は y を上げる）")]
    [SerializeField] private Vector3 effectLocalOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("追従方法：true=親子付け（軽い＆楽）/ false=Updateで位置同期（指示通り）")]
    [SerializeField] private bool parentEffectToGhost = false;

    [Tooltip("追跡開始～終了のわずかな残像を出したい時の生存時間（秒）。0なら即消し")]
    [SerializeField] private float effectGraceSecondsOnStop = 0f;

    private Transform angryEffectInstance;   // 生成したエフェクトのTransform
    private float effectStopTimer = 0f;
    // ここまで【追加】
    // -----------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isChasing = true;
            SpawnAngryEffect();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isChasing = false;
            BeginStopAngryEffect();
        }
    }

    private void Update()
    {
        // 追従移動
        if (isChasing && Player != null && Ghost != null)
        {
            Vector3 toPlayer = Player.position - Ghost.position;
            toPlayer.y = 0f; // 水平のみ

            float dist = toPlayer.magnitude;
            if (dist > stopDistance)
            {
                if (toPlayer != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer);
                    Ghost.rotation = Quaternion.Slerp(Ghost.rotation, targetRot, 0.1f);
                }
                Ghost.position += Ghost.forward * moveSpeed * Time.deltaTime;
            }
        }

        // -----------------------------------------
        // ここから【追加：怒りエフェクトの追従＆停止処理】
        if (angryEffectInstance)
        {
            if (!parentEffectToGhost && Ghost)
            {
                // 親子にしていない場合、毎フレーム位置と向きを同期
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            if (!isChasing)
            {
                if (effectGraceSecondsOnStop > 0f)
                {
                    effectStopTimer -= Time.deltaTime;
                    if (effectStopTimer <= 0f)
                        DestroyAngryEffect();
                }
                else
                {
                    DestroyAngryEffect();
                }
            }
        }
        // ここまで【追加】
        // -----------------------------------------
    }

    // -----------------------------------------
    // ここから【追加：怒りエフェクト生成/破棄】
    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || !Ghost) return;

        if (!angryEffectInstance)
        {
            // 生成
            GameObject go = Instantiate(angryEffectPrefab);
            angryEffectInstance = go.transform;

            if (parentEffectToGhost)
            {
                // 親子付け：自然に追従（パフォーマンス◎）
                angryEffectInstance.SetParent(Ghost, worldPositionStays: false);
                angryEffectInstance.localPosition = effectLocalOffset;
                angryEffectInstance.localRotation = Quaternion.identity;
            }
            else
            {
                // 非親子：Updateで位置同期（指定通り）
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            // もしParticleSystemなら初期化
            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) ps.Play();
        }
        else
        {
            // 既にある場合は再生をリセット
            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) { ps.Clear(); ps.Play(); }
        }

        effectStopTimer = effectGraceSecondsOnStop;
    }

    private void BeginStopAngryEffect()
    {
        // 残像時間が設定されていれば Update でタイマーを減らす
        if (angryEffectInstance)
        {
            effectStopTimer = effectGraceSecondsOnStop;
            if (effectGraceSecondsOnStop <= 0f) DestroyAngryEffect();
        }
    }

    private void DestroyAngryEffect()
    {
        if (angryEffectInstance)
        {
            Destroy(angryEffectInstance.gameObject);
            angryEffectInstance = null;
        }
    }
    // ここまで【追加】
    // -----------------------------------------
}
