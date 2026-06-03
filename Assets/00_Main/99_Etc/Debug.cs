using System;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

public class Debug : MonoBehaviour
{
    private const string DEBUG_SYMBOL = "DEBUG_ENABLE";

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void Log(object message, Object context = null) 
        => UnityEngine.Debug.Log(message, context);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void LogWarning(object message, Object context = null) 
        => UnityEngine.Debug.LogWarning(message, context);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void LogError(object message, Object context = null) 
        => UnityEngine.Debug.LogError(message, context);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void LogException(Exception e, Object context = null) 
        => UnityEngine.Debug.LogException(e, context);
    
    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void Assert(bool condition) 
        => UnityEngine.Debug.Assert(condition);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void Assert(bool condition, object message) 
        => UnityEngine.Debug.Assert(condition, message);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void Assert(bool condition, object message, Object context) 
        => UnityEngine.Debug.Assert(condition, message, context);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void DrawLine(Vector3 start, Vector3 end, Color color = default, float duration = 0f, bool depthTest = true)
        => UnityEngine.Debug.DrawLine(start, end, color, duration, depthTest);

    [Conditional(DEBUG_SYMBOL), HideInCallstack]
    public static void DrawRay(Vector3 start, Vector3 direction, Color color = default, float duration = 0f, bool depthTest = true)
        => UnityEngine.Debug.DrawRay(start, direction, color, duration, depthTest);
}
