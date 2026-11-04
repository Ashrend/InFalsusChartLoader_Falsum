using System.Collections.Generic;
using System.Diagnostics;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The mod's logging: Debug-only narration, and the lines a Release keeps.</summary>
    internal static class Diagnostics
    {
        /// <summary>The loader's log sink.</summary>
        private static class Sink
        {
#if MELONLOADER
            internal static void Msg(string message) => MelonLoader.MelonLogger.Msg(message);
            internal static void Error(string message) => MelonLoader.MelonLogger.Error(message);
            internal static void Warning(string message) => MelonLoader.MelonLogger.Warning(message);
#elif BEPINEX
            private static readonly BepInEx.Logging.ManualLogSource Source =
                BepInEx.Logging.Logger.CreateLogSource("InFalsusChartLoader_falsum");

            internal static void Msg(string message) => Source.LogInfo(message);
            internal static void Error(string message) => Source.LogError(message);
            internal static void Warning(string message) => Source.LogWarning(message);
#elif TESTS
            internal static void Msg(string message) { }
            internal static void Error(string message) { }
            internal static void Warning(string message) { }
#else
#error Define MELONLOADER or BEPINEX.
#endif
        }

        /// <summary>Whether this thread's messages are being held back.</summary>
        [ThreadStatic] private static bool _deferring;

        /// <summary>The messages this thread has held back.</summary>
        [ThreadStatic] private static List<string> _held;

        /// <summary>Starts holding this thread's messages back.</summary>
        internal static void BeginDefer()
        {
            _held = new List<string>();
            _deferring = true;
        }

        /// <summary>Takes the held messages and stops holding them back.</summary>
        internal static List<string> EndDefer()
        {
            List<string> held = _held ?? new List<string>();
            _held = null;
            _deferring = false;
            return held;
        }

        /// <summary>Writes a held message out.</summary>
        internal static void Emit(string message) => Sink.Msg(message);

        /// <summary>Holds a message back, or writes it out.</summary>
        private static void Route(string message)
        {
            if (!_deferring)
            {
                Sink.Msg(message);
                return;
            }

            if (_held == null) _held = new List<string>();
            _held.Add(message);
        }

        /// <summary>Debug-only narration about a run.</summary>
        [Conditional("DEBUG")]
        internal static void Info(string message)
        {
#if DEBUG
            Route(message);
#endif
        }

        /// <summary>The startup state every build reports.</summary>
        internal static void Load(string message) => Route(message);

        /// <summary>A failure the mod has stopped doing its work for.</summary>
        internal static void Error(string message) => Route(message);

        /// <summary>A warning a Release build keeps for the player.</summary>
        internal static void WarnRelease(string message) => Route(message);

        /// <summary>Debug-only warning about a failure the mod works around.</summary>
        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
#if DEBUG
            Route(message);
#endif
        }

        /// <summary>An exception chain flattened into one line.</summary>
        internal static string Describe(System.Exception e)
        {
            if (e == null) return "<null>";

#if DEBUG
            var sb = new System.Text.StringBuilder();
            for (System.Exception x = e; x != null; x = x.InnerException)
            {
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(x.GetType().Name).Append(": ").Append(x.Message);
            }
            return sb.ToString();
#else
            return e.GetType().Name;
#endif
        }
    }
}
