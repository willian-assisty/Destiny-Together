namespace DestinyTogether.Core
{
    /// <summary>Log injetavel — permite que a simulacao logue sem conhecer UnityEngine.Debug.</summary>
    public interface ILogSink
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>Descarta tudo. Padrao em testes.</summary>
    public sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new NullLogSink();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }
}
