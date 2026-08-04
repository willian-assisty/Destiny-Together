using DestinyTogether.Core;
using UnityEngine;

namespace DestinyTogether.App
{
    /// <summary>Liga o ILogSink puro da simulacao ao console do Unity.</summary>
    public sealed class UnityLogSink : ILogSink
    {
        public void Info(string message) => Debug.Log($"[Sim] {message}");
        public void Warn(string message) => Debug.LogWarning($"[Sim] {message}");
        public void Error(string message) => Debug.LogError($"[Sim] {message}");
    }
}
