using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The prepared songs: imported on a worker thread, checked on the main thread.</summary>
    /// <remarks>
    /// <para>
    /// Reading the folders — walking them, parsing the <c>if</c> and <c>.falsum</c> files, decompressing
    /// their assets — is slow and touches nothing the game owns, so it runs on a worker thread. Checking
    /// a chart calls the game's own decoder and therefore cannot: the songs are checked a few difficulties
    /// at a time from the frame loop instead, so a song select that opens mid-check finishes the rest
    /// synchronously rather than waiting for the whole folder to be read.
    /// </para>
    /// <para>
    /// Nothing here writes to the game's tables. Until the caller takes <see cref="Packs"/>, the songs
    /// exist only in this module.
    /// </para>
    /// </remarks>
    internal static class Prepare
    {
        /// <summary>How long the main thread may spend on the check in one frame.</summary>
        internal const long DefaultBudgetMs = 8;

        /// <summary>One background round's result, waiting for the main thread.</summary>
        private sealed class Round
        {
            /// <summary>The <see cref="Start"/> that opened this round; a reset one is dropped.</summary>
            internal int Number;

            /// <summary>The folders' fingerprint, or null when it could not be taken.</summary>
            internal string Fingerprint;

            /// <summary>The imported packs, or null when the folders could not be listed at all.</summary>
            internal List<ChartPack> Packs;

            /// <summary>What stopped the worker, or null when it finished.</summary>
            internal Exception Failure;

            /// <summary>The log lines the worker held back, in the order they were written.</summary>
            internal List<string> Lines;
        }

        /// <summary>One song's check: how far it has got, and whether a difficulty was refused.</summary>
        private sealed class SongCheck
        {
            /// <summary>The song being checked.</summary>
            internal ChartInfo Info;

            /// <summary>The difficulty the check is up to.</summary>
            internal int Difficulty;

            /// <summary>Whether one of the song's difficulties has been refused.</summary>
            internal bool Failed;
        }

        /// <summary>What the worker produced when the folders could not be read as one state.</summary>
        /// <remarks>
        /// A type rather than a message on purpose: <see cref="Diagnostics.Describe"/> keeps only the
        /// type's name in a Release build, so a message would put a diagnostic string in the artifact
        /// that no Release log ever shows. The name is what reaches the player instead.
        /// </remarks>
        private sealed class FolderChangedWhileReading : Exception
        {
        }

        /// <summary>Guards the two handovers: the worker's result, and the snapshot's identity.</summary>
        private static readonly object Gate = new object();

        /// <summary>The finished rounds, waiting for <see cref="Collect"/> to take them.</summary>
        private static readonly List<Round> Pending = new List<Round>();

        /// <summary>The worker, or null before the first <see cref="Start"/>.</summary>
        private static Task _worker;

        /// <summary>The round <see cref="_worker"/> was started for.</summary>
        private static int _workerRound;

        /// <summary>Which round is current. Every reset bumps it, so an older result is dropped.</summary>
        private static int _round;

        /// <summary>Whether a worker's result has been taken as the snapshot.</summary>
        private static bool _hasSnapshot;

        /// <summary>The fingerprint the snapshot was imported from, or null.</summary>
        private static string _fingerprint;

        /// <summary>The snapshot's packs, in folder order.</summary>
        private static List<ChartPack> _packs;

        /// <summary>The songs still to check, or null when there is no snapshot.</summary>
        private static List<SongCheck> _queue;

        /// <summary>How many of <see cref="_queue"/> have been checked.</summary>
        private static int _at;

        /// <summary>Whether the check has finished and the failed songs have been pruned.</summary>
        private static bool _ready;

        /// <summary>The decoder the check uses; set when a round is started.</summary>
        private static ChartDecoder _decode;

        /// <summary>Whether a worker is still reading the folders for the current round.</summary>
        internal static bool Running
        {
            get
            {
                lock (Gate) return Reading;
            }
        }

        /// <summary>Whether the current round has a worker that has not finished yet.</summary>
        /// <remarks>
        /// Called under <see cref="Gate"/>. A worker from a round a reset has superseded is still
        /// running, but it is not reading for the current round: its result is dropped when it lands.
        /// Counting it here would let <see cref="Start"/> refuse a request whose round nobody is
        /// working on, and the folder would then never be read — a snapshot that silently never appears.
        /// </remarks>
        private static bool Reading => _worker != null && !_worker.IsCompleted && _workerRound == _round;

        /// <summary>Whether a snapshot has been taken, checked or not.</summary>
        internal static bool HasSnapshot
        {
            get
            {
                lock (Gate) return _hasSnapshot;
            }
        }

        /// <summary>Whether the snapshot's songs have all been checked and the failed ones pruned.</summary>
        internal static bool Ready
        {
            get
            {
                lock (Gate) return _hasSnapshot && _ready;
            }
        }

        /// <summary>The fingerprint the snapshot was imported from, or null when there is none.</summary>
        internal static string Fingerprint
        {
            get
            {
                lock (Gate) return _fingerprint;
            }
        }

        /// <summary>The snapshot's packs, or null when there is no snapshot. Main thread only.</summary>
        /// <remarks>
        /// These are the objects the import produced, not a copy: a caller that is allowed to change
        /// them must take a copy first. The commit prunes the songs it is given, and a commit that
        /// failed is retried on the next visit.
        /// </remarks>
        internal static List<ChartPack> Packs
        {
            get
            {
                lock (Gate) return _packs;
            }
        }

        /// <summary>Starts reading the folders in the background; a round already being read is left alone.</summary>
        /// <remarks>
        /// The reading and the checking are handed in rather than reached for, so that this module
        /// depends on nothing but the mod's own managed types: the rule tests can then drive a
        /// whole round — adopt, check, prune, reset — without a game and without a charts folder.
        /// </remarks>
        internal static void Start(string root, Func<string, List<ChartPack>> import, ChartDecoder decode)
        {
            lock (Gate)
            {
                // Only a worker reading the round this call would continue counts. One left over from a
                // round a reset has discarded must not block a fresh read: its result will be dropped
                // when it lands, and the round asked for here would then never be read by anyone.
                if (Reading) return;

                ResetLocked();
                int number = _round;
                _workerRound = number;
                _decode = decode;
                _worker = Task.Run(() => Import(root, number, import));
            }
        }

        /// <summary>Takes a finished round and writes the log lines it held back. Main thread only.</summary>
        internal static void Collect()
        {
            Round[] finished;
            int current;
            lock (Gate)
            {
                if (Pending.Count == 0) return;

                finished = Pending.ToArray();
                Pending.Clear();
                current = _round;
            }

            foreach (Round round in finished)
            {
                // The worker must never write to the log itself: it is flushed here, on the main thread.
                if (round.Lines != null)
                    foreach (string line in round.Lines) Diagnostics.Emit(line);

                if (round.Number != current) continue;      // a reset happened while it ran: not this round

                if (round.Failure != null)
                {
                    Diagnostics.Error($"chart loading failed: {Diagnostics.Describe(round.Failure)}");
                    continue;                               // this round is spent; the next visit reads again
                }

                if (round.Packs == null) continue;          // the folders could not be listed

                Adopt(round);
            }
        }

        /// <summary>Checks a little more of the prepared songs, within <paramref name="budgetMs"/>.</summary>
        /// <remarks>
        /// Main thread only, and it must not be called while the game builds a page from the tables.
        /// This decodes and prunes only: the pictures are built by the commit, not here, because the
        /// commit resets the picture catalogs before it rebuilds them.
        /// </remarks>
        /// <returns>True when every song has been checked and the failed ones pruned.</returns>
        internal static bool ValidateSlice(long budgetMs)
        {
            List<SongCheck> queue;
            lock (Gate)
            {
                if (!_hasSnapshot) return false;            // nothing has been prepared yet
                if (_ready) return true;                    // already done, and pruned exactly once

                queue = _queue;
            }

            var watch = Stopwatch.StartNew();
            while (true)
            {
                if (_at >= queue.Count)
                {
                    Prune();
                    return true;
                }

                SongCheck check = queue[_at];

                if (check.Failed || check.Difficulty >= ChartInfo.Difficulties)
                {
                    // Deliberately build nothing here: Reload.Commit resets the jacket and background
                    // catalogs before ModCore.Register rebuilds the pictures, so anything built during
                    // the check would be thrown away and its texture work paid twice. The check's job is
                    // the decode; the pictures stay with Register, the one place that also knows which
                    // songs survived name and audio admission.
                    _at++;
                }
                else
                {
                    // One difficulty is one slice's worth of work: the decoder is the game's own, and
                    // a refusal ends the song exactly as it does while importing.
                    if (!Decode(check.Info, check.Difficulty)) check.Failed = true;

                    check.Difficulty++;
                }

                // The budget is measured after a piece of work, so one frame always makes progress.
                if (watch.ElapsedMilliseconds >= budgetMs) return false;
            }
        }

        /// <summary>Finishes the check now, for a song select that arrived before it was done.</summary>
        internal static void ValidateAll()
        {
            // Without a snapshot there is nothing to finish, and ValidateSlice would keep saying so.
            if (!HasSnapshot) return;

            while (!ValidateSlice(long.MaxValue)) { }
        }

        /// <summary>Drops the snapshot and the queue; a round still running will be discarded.</summary>
        /// <remarks>
        /// Main thread only, as with every member that touches the snapshot: the queue is walked
        /// without the lock, and a reset from another thread would race with that walk.
        /// </remarks>
        internal static void Reset()
        {
            lock (Gate) ResetLocked();
        }

        /// <summary>Forgets everything the main thread derived from the last round.</summary>
        /// <remarks>
        /// The finished rounds already waiting in <see cref="Pending"/> are deliberately left there: their
        /// log lines still have to be written, and <see cref="Collect"/> drops them by their number.
        /// </remarks>
        private static void ResetLocked()
        {
            _round++;
            _hasSnapshot = false;
            _fingerprint = null;
            _packs = null;
            _queue = null;
            _at = 0;
            _ready = false;
        }

        /// <summary>Reads the folders, on a worker thread. Never touches the game, never writes the log.</summary>
        private static void Import(string root, int number, Func<string, List<ChartPack>> import)
        {
            var round = new Round { Number = number };

            Diagnostics.BeginDefer();
            try
            {
                // The folders are read over seconds, so they can change underneath the read: the pack
                // list is then neither the old contents nor the new ones. Two prints bracket the read,
                // and equal prints mean the read did not span a change.
                string before = FingerprintOf(root);

                round.Packs = import(root);

                string after = FingerprintOf(root);

                if (before == null || after == null || !string.Equals(before, after, StringComparison.Ordinal))
                {
                    // Two unreadable reads are no evidence that nothing changed, so a missing print is
                    // refused exactly like two that disagree. The print with the time stamps in it is
                    // what makes a change visible at all: a revert is itself a write.
                    Diagnostics.Info(before == null || after == null
                        ? "prepare: the charts folder could not be read before and after the import"
                        : "prepare: the charts folder changed while it was being read");

                    round.Failure = new FolderChangedWhileReading();
                    return;
                }

                round.Fingerprint = after;
            }
            catch (Exception e)
            {
                round.Failure = e;      // reported by Collect, on the main thread
            }
            finally
            {
                round.Lines = Diagnostics.EndDefer();
                lock (Gate) Pending.Add(round);
            }
        }

        /// <summary>The folders' fingerprint, or null when they cannot be read at all.</summary>
        /// <remarks>
        /// An empty folder that can be read gets the empty string, not null: "nothing is there" is a
        /// state, and it is a different answer from "this could not be read".
        /// </remarks>
        private static string FingerprintOf(string root) =>
            ReloadPlan.TryFingerprint(root, out string print) ? print : null;

        /// <summary>Accepts every song folder, so that checking can happen on the main thread.</summary>
        /// <remarks>
        /// The background read only reads: a folder is not refused here, because refusing it means
        /// calling the game's decoder, and a game call on a worker thread would crash or freeze the
        /// game in a way that is very hard to reproduce. Rejection happens in the check instead, on
        /// the main thread.
        /// </remarks>
        internal static string NeverFails(ChartInfo info) => null;

        /// <summary>Turns one round's result into the snapshot and the queue of songs to check.</summary>
        private static void Adopt(Round round)
        {
            var queue = new List<SongCheck>();
            foreach (ChartPack pack in round.Packs)
                foreach (ChartInfo info in pack.Charts)
                    queue.Add(new SongCheck { Info = info });

            lock (Gate)
            {
                _hasSnapshot = true;
                _fingerprint = round.Fingerprint;
                _packs = round.Packs;
                _queue = queue;
                _at = 0;
                _ready = false;
            }

            Diagnostics.Info($"prepared: {queue.Count} song(s) in {round.Packs.Count} pack(s) to check");
        }

        /// <summary>Drops the songs that failed and the packs they leave empty. Runs once, on the way out.</summary>
        private static void Prune()
        {
            List<ChartPack> packs;
            List<SongCheck> queue;
            lock (Gate)
            {
                packs = _packs;
                queue = _queue;
            }

            var failed = new HashSet<ChartInfo>();
            if (queue != null)
                foreach (SongCheck check in queue)
                    if (check.Failed) failed.Add(check.Info);

            if (packs != null && failed.Count > 0)
            {
                foreach (ChartPack pack in packs)
                    pack.Charts.RemoveAll(chart => failed.Contains(chart));

                // A pack with nothing valid left is the same as one whose songs never imported.
                packs.RemoveAll(pack => pack.Charts.Count == 0);
            }

            lock (Gate) _ready = true;

            Diagnostics.Info($"prepared: {failed.Count} song(s) failed the check");
        }

        /// <summary>Decodes one difficulty with the game's own decoder. Main thread only.</summary>
        /// <remarks>
        /// This is the import's check, one difficulty at a time: <see cref="ChartCheck"/> is the same
        /// code the import uses, so a chart that loads through one of them loads through the other.
        /// The two places must keep agreeing, or a song would be admitted by one and refused by the
        /// other.
        /// </remarks>
        private static bool Decode(ChartInfo info, int difficulty)
        {
            if (ChartCheck.Try(info, difficulty, _decode, out string reason)) return true;

            Diagnostics.Warn($"'{info.FolderName}' was refused: {reason}");
            return false;
        }
    }
}
