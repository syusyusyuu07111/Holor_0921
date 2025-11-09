using UnityEngine;

public static class GameSettings
{
    const string KEY_INVERT_X = "InvertX";
    const string KEY_INVERT_Y = "InvertY";
    const string KEY_FULLSCREEN = "FullScreenOption";
    const string KEY_SENSITIVITY = "Sensitivity01";

    static bool _initialized = false;

    public static bool InvertX { get; private set; }
    public static bool InvertY { get; private set; }

    // true  = ウィンドウ表示したい（UI の ON が赤）
    // false = フルスクリーンにしたい（UI の OFF が赤）
    public static bool FullScreenOption { get; private set; }

    // 0〜1 の感度
    public static float Sensitivity01 { get; private set; }

    public static void Load()
    {
        if (_initialized) return;

        InvertX = PlayerPrefs.GetInt(KEY_INVERT_X, 0) == 1;
        InvertY = PlayerPrefs.GetInt(KEY_INVERT_Y, 0) == 1;
        FullScreenOption = PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;
        Sensitivity01 = PlayerPrefs.GetFloat(KEY_SENSITIVITY, 0.5f);

        _initialized = true;
    }

    public static void SetInvertX(bool value)
    {
        Load();
        InvertX = value;
        PlayerPrefs.SetInt(KEY_INVERT_X, value ? 1 : 0);
    }

    public static void SetInvertY(bool value)
    {
        Load();
        InvertY = value;
        PlayerPrefs.SetInt(KEY_INVERT_Y, value ? 1 : 0);
    }

    public static void SetFullScreenOption(bool value)
    {
        Load();
        FullScreenOption = value;
        PlayerPrefs.SetInt(KEY_FULLSCREEN, value ? 1 : 0);
    }

    public static void SetSensitivity01(float value)
    {
        Load();
        Sensitivity01 = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, Sensitivity01);
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }
}
