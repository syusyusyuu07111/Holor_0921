using System.Diagnostics;
using UnityEngine;
using CriWare;

/// <summary>
/// CriAtomSource.Stop() を呼ぶときに、
/// 「どのスクリプトのどの行から呼ばれたか」をログに出すための拡張メソッド。
/// </summary>
public static class CriAtomSourceStopDebugger
{
    /// <summary>
    /// Stop() のデバッグ版。
    /// 既存の src.Stop() を src.DebugStop() に置き換えて使う。
    /// </summary>
    [Conditional("UNITY_EDITOR")] // ビルドには含めたくないならこのままでOK
    public static void DebugStop(this CriAtomSource src)
    {
        if (src == null)
        {
            UnityEngine.Debug.LogWarning("[AUDIO][DebugStop] CriAtomSource is null");
            return;
        }

        // スタックトレース取得（trueでファイル名・行番号付き）
        StackTrace st = new StackTrace(true);

        // 0: このメソッド自身
        // 1: DebugStop を呼んだ箇所
        StackFrame caller = st.FrameCount > 1 ? st.GetFrame(1) : null;

        string scriptName = caller?.GetFileName() ?? "(unknown file)";
        int line = caller?.GetFileLineNumber() ?? 0;
        string methodName = caller?.GetMethod()?.DeclaringType?.FullName + "." + caller?.GetMethod()?.Name;

        string objPath = GetHierarchyPath(src.gameObject);

        UnityEngine.Debug.Log(
            $"[AUDIO][DebugStop] Stop() called\n" +
            $"  Obj   : {objPath}\n" +
            $"  Cue   : {src.cueName} (sheet={src.cueSheet})\n" +
            $"  Method: {methodName}\n" +
            $"  File  : {scriptName}:{line}\n" +
            $"  3D    : {src.use3dPositioning}",
            src
        );

        // 実際の Stop を呼ぶ
        src.Stop();
    }

    // 階層パス（デバッグ用）
    private static string GetHierarchyPath(GameObject obj)
    {
        if (obj == null) return "null";

        string path = obj.name;
        Transform t = obj.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
