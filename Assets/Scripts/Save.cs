using UnityEngine;

public class Save : MonoBehaviour
{
    public static Save Instance;

    [Header("PlayerPrefs Key")]
    [SerializeField] private string prefsKey = "GAME_STATE_V1";

    [System.Serializable]
    public class State
    {
        // —— 基本/前段まわり ——
        public bool tutorialCleared;         // 前段クリアしたか（=次回スキップ）
        public bool basicDone;               // 前段を完了したか（内部フラグの実値）
        
        // —— ミッションUI ——
        public int  doorMissionStage;        // 0=None,1=DoorCheck,2=FindGhost,3=HearVoiceGoNext,4=AllDone
        public bool missionVisible;          // ミッションUIの表示ON/OFF
        public string missionText;           // ミッションUIの現在文言

        // —— Hint進捗 ——
        public int hintProgressStage;        // HintText.ProgressStage

        // —— 初見パネル/その他フラグ（Tutorialの内部フラグの鏡） ——
        public bool didStep4;                // 初めて幽霊を見たパネルを既に出したか
        public bool didStep5;                // 初めてstate=2パネルを既に出したか
        public bool didHidePanel;            // 隠れるパネルを既に出したか

        // —— ライトON状態（ミッション3後ONにしたか） ——
        public bool lightsActivatedAfterM3;
    }

    public State Data = new State();

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ===== 外部API =====

    public void MarkTutorialCleared()
    {
        Data.tutorialCleared = true;
        Data.basicDone = true; // 前段を明示的に完了扱い
        SaveNow();
    }

    public void SaveNow()
    {
        string json = JsonUtility.ToJson(Data);
        PlayerPrefs.SetString(prefsKey, json);
        PlayerPrefs.Save();
#if UNITY_EDITOR
        Debug.Log($"[Save] Saved: {json}");
#endif
    }

    public void Load()
    {
        string json = PlayerPrefs.GetString(prefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            Data = JsonUtility.FromJson<State>(json);
#if UNITY_EDITOR
            Debug.Log($"[Save] Loaded: {json}");
#endif
        }
    }

    // 現在のシーン状態を吸い上げる（＝保存）
    public void CaptureFromScene(Tutorial tut, HintText hint)
    {
        if (tut)
        {
            Data.basicDone = tut.IsBasicDonePublic();
            Data.doorMissionStage = tut.GetDoorMissionStagePublic(); // enum→int
            Data.missionVisible = tut.GetMissionVisiblePublic();
            Data.missionText = tut.GetMissionTextPublic();
            Data.didStep4 = tut.GetDidStep4Public();
            Data.didStep5 = tut.GetDidStep5Public();
            Data.didHidePanel = tut.GetDidHidePanelPublic();
            Data.lightsActivatedAfterM3 = tut.GetLightsActivatedPublic();
        }
        if (hint)
        {
            Data.hintProgressStage = hint ? hint.ProgressStage : Data.hintProgressStage;
        }
        SaveNow();
    }

    // 保存していた状態をシーンへ適用（＝復元）
    public void ApplyToScene(Tutorial tut, HintText hint)
    {
        if (tut)
        {
            // 前段スキップ（確実に OnEnable の前に反映したい場合は Tut.Awake でもやる）
            if (Data.basicDone || Data.tutorialCleared) tut.ForceSkipBasicTutorialPublic();

            // 内部フラグ系（順序大事：ミッション→ライト）
            tut.SetDoorMissionStagePublic(Data.doorMissionStage);
            tut.SetMissionUIStatePublic(Data.missionVisible, Data.missionText);

            tut.SetDidStepFlagsPublic(Data.didStep4, Data.didStep5, Data.didHidePanel);

            if (Data.lightsActivatedAfterM3)
                tut.SetLightsActivatedAfterM3Public(true);
        }

        if (hint)
        {
            hint.SetProgress(Mathf.Max(0, Data.hintProgressStage));
        }
    }
}
