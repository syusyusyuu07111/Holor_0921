using UnityEngine;

// ======================================================
// GameSettings
//
// 役割:
// ゲーム全体で共有する設定値の管理クラス
//
// 特徴:
// ・staticクラスなのでシーンをまたいでも1つだけ存在する
// ・PlayerPrefsに保存される
// ・どこからでも GameSettings.xxx で取得できる
//
// 流れ:
// 1 最初にLoad()でPlayerPrefsから読み込む
// 2 Set系メソッドで値を更新し、同時に保存データを書き換える
// 3 Save()で明示的にディスクへ書き込む
// ======================================================
public static class GameSettings
{
    // PlayerPrefsに保存するキー名
    // ここを変えると保存先が変わる
    const string KEY_INVERT_X = "InvertX";
    const string KEY_INVERT_Y = "InvertY";
    const string KEY_FULLSCREEN = "FullScreenOption";
    const string KEY_SENSITIVITY = "Sensitivity01";

    // すでにLoad済みかどうか
    // 二重読み込みを防ぐためのフラグ
    static bool _initialized = false;

    // ================================
    // 現在メモリ上に保持している設定値
    // 外部からはgetのみ可能（直接書き換え禁止）
    // ================================
    public static bool InvertX { get; private set; }
    public static bool InvertY { get; private set; }

    // true  = ウィンドウ表示にしたい
    // false = フルスクリーンにしたい
    public static bool FullScreenOption { get; private set; }

    // 0〜1の範囲で管理する感度値
    public static float Sensitivity01 { get; private set; }

    // ==================================================
    // Load
    //
    // PlayerPrefsから設定を読み込む
    // すでに読み込み済みなら何もしない
    //
    // これにより
    // 「まだ読み込んでいない状態で値を使う」
    // ことを防いでいる
    // ==================================================
    public static void Load()
    {
        if (_initialized) return;

        InvertX = PlayerPrefs.GetInt(KEY_INVERT_X, 0) == 1;
        InvertY = PlayerPrefs.GetInt(KEY_INVERT_Y, 0) == 1;
        FullScreenOption = PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;
        Sensitivity01 = PlayerPrefs.GetFloat(KEY_SENSITIVITY, 0.5f);

        _initialized = true;
    }

    // ==================================================
    // SetInvertX
    //
    // X軸反転設定を更新する
    // 1 メモリ上の値を変更
    // 2 PlayerPrefsにも即反映
    // ==================================================
    public static void SetInvertX(bool value)
    {
        Load();
        InvertX = value;
        PlayerPrefs.SetInt(KEY_INVERT_X, value ? 1 : 0);
    }

    // ==================================================
    // SetInvertY
    //
    // Y軸反転設定を更新する
    // ==================================================
    public static void SetInvertY(bool value)
    {
        Load();
        InvertY = value;
        PlayerPrefs.SetInt(KEY_INVERT_Y, value ? 1 : 0);
    }

    // ==================================================
    // SetFullScreenOption
    //
    // フルスクリーン設定を更新する
    // trueならウィンドウ表示
    // falseならフルスクリーン
    // ==================================================
    public static void SetFullScreenOption(bool value)
    {
        Load();
        FullScreenOption = value;
        PlayerPrefs.SetInt(KEY_FULLSCREEN, value ? 1 : 0);
    }

    // ==================================================
    // SetSensitivity01
    //
    // マウス感度を更新する
    // 0〜1の範囲に強制的に丸める
    // ==================================================
    public static void SetSensitivity01(float value)
    {
        Load();
        Sensitivity01 = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, Sensitivity01);
    }

    // ==================================================
    // Save
    //
    // PlayerPrefsをディスクへ書き込む
    // 設定画面を閉じるタイミングなどで呼ぶ想定
    // ==================================================
    public static void Save()
    {
        PlayerPrefs.Save();
    }
}