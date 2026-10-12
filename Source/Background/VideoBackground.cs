using System;

namespace InFalsusChartLoader
{
    /// <summary>
    /// A clip on the in-play background: one `VideoPlayer` of this mod's own, playing a file into a
    /// render texture that a material shows.
    ///
    /// <para>
    /// The game has video players of its own — `C2DVideoPlayer` and `GenericMeshVideoPlayer`, both of
    /// which draw a clip onto a `Constrained2D` — but both are fed an `AssetReferenceT&lt;VideoClip&gt;`,
    /// which is an Addressables lookup, and a file in a chart folder is not an Addressables asset.
    /// What the game's own code does show is that the machinery is present and how it is arranged:
    /// a `UnityEngine.Video.VideoPlayer` rendering into a `RenderTexture` that a material carries.
    /// This builds the same arrangement with the player's <c>url</c> source — the one input a file on
    /// disk can use — instead of its clip source.
    /// </para>
    /// <para>
    /// One player for the whole session, reused: the object it lives on is created on first use and
    /// marked `DontDestroyOnLoad`, and the texture and the material are built beside it. Being
    /// <i>playing</i> is the chart's business rather than the setup's: a clip named for a chart is
    /// held still while the scene is still being put together, and <see cref="Restart"/> — called
    /// from the hook the game's own starting sequence ends on, the instant play is handed over to the
    /// chart — places it back at its first frame and sets it running. Every play of the chart, a
    /// retry included, therefore begins the clip at its first frame; without that, the clip is still
    /// running from the last play by the time the next chart begins, which is the shape "the video
    /// plays before the chart does, and a restart does not restart it" has.
    /// </para>
    /// <para>
    /// What the material <i>shows</i> is the clip's own texture, from the moment the clip is
    /// prepared: the background behind the game's starting sequence is the clip's first frame — a
    /// still — and not a black panel. That still has to be held rather than left to the player,
    /// because "prepared and idle" is not a state this build keeps a clip in: a prepared player here
    /// begins playing on its own, so it is stopped again the moment it does — see the watch in
    /// <see cref="Follow"/> — and parked on its first frame until the song begins
    /// (<see cref="Park"/>).
    /// </para>
    /// <para>
    /// The clip is played once and not looped: when its end arrives the last frame stays on the
    /// background for the rest of the play, and the next play of the chart — a retry — begins it
    /// again from the first frame. See <see cref="FilmOver"/>.
    /// </para>
    /// <para>
    /// Where the clip <i>stands</i> is not left to itself either. The picture is placed on the game's
    /// own chart clock, read every frame while a chart of this mod's is running, so it starts at the
    /// song's zero rather than where the game hands the chart over — there are seconds of lead-in
    /// between the two, and a clip started at the handover plays through them and sits that far ahead
    /// of the chart for the rest of the play (three seconds, measured on this build) — and a pause
    /// leaves it no further out than <see cref="ClockSlack"/>.
    /// </para>
    /// <para>
    /// And the pause screen: while the game's own music is held, the clip is held with it — the two
    /// hooks on `_Dg._hiA` / `_Dg._KiA`, the call the pause entry makes and the call the music comes
    /// back on — see <see cref="Hold"/> and <see cref="Resume"/>. The resume puts the song back by
    /// the game's own lead-in and the picture waits that out where it stopped, playing on when the
    /// song is level with it again: the countdown runs behind the menu with the picture still, and
    /// nothing has to jump or be seeked when it ends. See <see cref="Follow"/>.
    /// </para>
    /// <para>
    /// Every one of these objects is this mod's own and nothing in the game points at any of them,
    /// so each is held by a GC handle of its own — see `Root`.
    /// </para>
    /// <para>
    /// The audio track is deliberately not played: a chart already has its music, and the game routes
    /// every other sound through its own managers. A background clip is a picture.
    /// </para>
    /// </summary>
    internal static unsafe class VideoBackground
    {
        /// <summary>
        /// The size of the render texture the clip is played into.
        ///
        /// A rendering choice, not a property of anything: the texture has to exist before the player
        /// is prepared, and the player cannot say how big the clip is until it is. 1920×1080 is what
        /// the game's own video assets are, and a clip of another shape is fitted into it by the
        /// player's own aspect handling rather than stretched by this mod.
        /// </summary>
        private const int TextureWidth = 1920;

        private const int TextureHeight = 1080;

        /// <summary>Where the video types live. Both are Unity's own; neither is generated by the game.</summary>
        private const string CoreAssembly = "UnityEngine.CoreModule.dll";

        private const string VideoAssembly = "UnityEngine.VideoModule.dll";

        private const string CoreNamespace = "UnityEngine";

        private const string VideoNamespace = "UnityEngine.Video";

        private static IntPtr _object;      // the GameObject the player lives on
        private static IntPtr _player;      // the VideoPlayer component
        private static IntPtr _texture;     // the RenderTexture it renders into
        private static IntPtr _material;    // the material that shows the texture
        private static string _playing;     // the file the player was last pointed at

        // Where the clip is in its life: `_armed` from the moment the game hands the chart over
        // (`Restart`) until `Follow` has started the clip with the song, `_running` once that has
        // happened, `_ended` when the clip has played out and its last frame is the background, and
        // `_held` while the game's own music — and therefore the clip — is paused.
        private static bool _armed;
        private static bool _running;
        private static bool _held;
        private static bool _ended;

        // The chart clock when the pause came, and whether it has since been seen to move back — which
        // is the game answering a resume. Both are the pause's and the resume's, and `Follow` is where
        // they are read.
        private static double _heldClock = double.NaN;
        private static bool _rewound;

        // The game's chart clock, resolved by name the first time it is wanted: `GameScene` keeps the
        // live engine in a static (`_DN`) and the engine keeps the seconds the notes are judged
        // against in `_VD._WEA`. `_clockMissing` latches a miss — the names do not appear mid-run,
        // and a picture that cannot be placed is the thing this class exists to avoid.
        private static IntPtr _gameSceneClass;
        private static int _engineField = Offsets.Unresolved;   // GameScene._DN — the live _VD
        private static int _clockField = Offsets.Unresolved;    // _VD._WEA — seconds on the chart
        private static bool _clockMissing;

        // The collector is conservative and scans thread stacks, but none of these ever lives in a
        // local for the whole run — they are in static fields, which it does not scan. See `Root`.
        private static IntPtr _objectHandle, _playerHandle, _textureHandle, _materialHandle;

        private static bool _unavailable;   // asked once; the answer cannot change

        /// <summary>
        /// Prepares the clip <paramref name="path"/> names and answers with the material that shows
        /// it, or zero with a reason.
        ///
        /// Called once per play, while the gameplay scene is being put together. The clip is left not
        /// playing: what sets it running is the chart itself — see <see cref="Restart"/> — so the
        /// picture begins when the chart does, not while the scene is still loading. What the
        /// background shows meanwhile is the clip's own texture, stopped on its first frame; a second
        /// play of the same file (a retry) holds the previous playback still on its way through, and
        /// the frame is put back when that chart is handed over.
        /// </summary>
        internal static IntPtr Material(string path, out string reason)
        {
            reason = null;

            if (_unavailable) return IntPtr.Zero;

            if (_playing == path && _material != IntPtr.Zero)
            {
                // The chart this clip belongs to is being set up again — a retry, or the song entered
                // a second time — so the previous playback is held still here and `Park` will put the
                // clip back to its first frame when the chart is handed over. `Pause` rather than
                // `Stop` for that: `Stop` sets the time to zero but also drops the player's decoder
                // and makes it unprepared, which is work for nothing when the same clip is about to
                // run.
                if (!Call("Pause", 0, _player, out reason)) return IntPtr.Zero;

                // The clip's own frame until the chart is handed over — see the class remarks. It is
                // parked a moment later, in `Restart`, where the first frame becomes definite.
                if (!Show(_texture, out reason)) return IntPtr.Zero;

                _armed = false;
                _running = false;
                _held = false;
                _ended = false;
                return _material;
            }

            try
            {
                if (!Ensure(out reason)) { _unavailable = true; return IntPtr.Zero; }

                if (_playing != null) Stop();

                if (!Set("set_source", 1, 1 /* VideoSource.Url */, out reason)) return IntPtr.Zero;   // a file
                if (!Set("set_url", 1, FileUrl(path), out reason)) return IntPtr.Zero;
                if (!Set("set_renderMode", 1, 2 /* VideoRenderMode.RenderTexture */, out reason)) return IntPtr.Zero;
                if (!Set("set_targetTexture", 1, _texture, out reason)) return IntPtr.Zero;

                // Played once, not looped: a clip shorter than its song holds its last frame on the
                // background instead of running back to the first one and playing it all again. See
                // `FilmOver`, which is what makes the hold final rather than a frame's reprieve.
                if (!Set("set_isLooping", 1, false, out reason)) return IntPtr.Zero;

                if (!Set("set_playOnAwake", 1, false, out reason)) return IntPtr.Zero;

                // No audio: see the summary. Set on every start rather than once, because Unity's
                // default is the mode that wants an AudioSource this mod has not given it.
                if (!Set("set_audioOutputMode", 1, 0 /* VideoAudioOutputMode.None */, out reason)) return IntPtr.Zero;

                if (!Call("Prepare", 0, _player, out reason)) return IntPtr.Zero;

                // The clip's own picture from here on: what the screen shows is the material's
                // texture, and that is the render texture from the moment the clip is prepared. A
                // prepared player on this build begins playing on its own, so the first frame is not
                // simply waited for — the watch in `Follow` catches that self-play and `Park` puts the
                // clip back on the frame. See the class remarks.
                if (!Show(_texture, out reason)) return IntPtr.Zero;

                _playing = path;
                _length = -1.0;                          // the previous clip's length is not this one's
                _armed = false;
                _running = false;
                _held = false;
                _ended = false;
                Diagnostics.Info($"background video '{System.IO.Path.GetFileName(path)}' ready; it starts with the song");
                return _material;
            }
            catch (Exception e)
            {
                // A failure here must not take the chart with it: the caller falls back to the game's
                // own background, which is what a song with no `background` gets anyway.
#if DEBUG
                reason = $"the background video could not be prepared: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return IntPtr.Zero;
            }
        }

        /// <summary>Whether this is the material this clip built and the claim handed over.</summary>
        internal static bool Owns(IntPtr material) => material != IntPtr.Zero && material == _material;

        /// <summary>
        /// Re-parents this clip onto a clone of the game's own background material, which the claim
        /// has just loaded — so the picture rides on everything the game authored on its backgrounds
        /// (see `BackgroundClone`) and every later `Show`/`Restart`/`Hold` drives the clone, because
        /// `_material` becomes it.
        ///
        /// <b>There is no way back to the material built here.</b> That material is the very thing
        /// that hid this build's mask, so the picture is put on the clone before it takes over rather
        /// than after — on any failure nothing has changed yet — and a failure is never answered by
        /// putting the old material back.
        /// </summary>
        internal static IntPtr Rebase(IntPtr gameMaterial, out string reason)
        {
            reason = null;
            if (_material == IntPtr.Zero)
            {
                reason = "no clip material is loaded";
                return IntPtr.Zero;
            }

            IntPtr clone = JacketFactory.CloneFrom(gameMaterial, out reason);
            if (clone == IntPtr.Zero) return IntPtr.Zero;

            // The clip's own picture — the state the material was built into.
            if (!Show(clone, _texture, out reason)) return IntPtr.Zero;

            _material = clone;
            return clone;
        }

        /// <summary>
        /// The game's starting sequence has handed the chart over (`GameScene._rk`, the call every
        /// path of that sequence ends on): the chart is about to run, and the clip is parked on its
        /// first frame and armed for it.
        ///
        /// Nothing is started here, and that is the point. Between this call and the music there are
        /// seconds of the game's own lead-in — the chart's clock starts at minus three and walks up
        /// to the song's zero — and a clip started here plays through them and sits that far ahead of
        /// the chart for the rest of the play. That was measured, not reasoned about: the clip read
        /// 2.9 seconds ahead of the song at the first pause of a run. The clip starts when the clock
        /// reaches zero, in <see cref="Follow"/>, and a retry runs this sequence again, which is what
        /// takes the clip back to its first frame for the new play.
        /// </summary>
        internal static void Restart()
        {
            if (_player == IntPtr.Zero || _playing == null) return;

            // Parked on its first frame here, and held there until the song begins. Nothing is shown
            // or hidden to do it — the material is already wearing the clip's texture — so this is
            // simply where the clip's position is set, and any projection the player has run on its
            // own before this point is undone by the seek. See `Park`.
            string reason = null;
            Park(out reason);

            _armed = true;
            _running = false;
            _held = false;
            _ended = false;
            Diagnostics.Info($"background video '{System.IO.Path.GetFileName(_playing)}' waits for the song");
        }

        /// <summary>
        /// Puts the clip back on its first frame and holds it there, paused. Called when the chart is
        /// handed over (<see cref="Restart"/>) and again just before the song begins
        /// (<see cref="Start"/>), so the background always goes into a play from the first frame.
        ///
        /// The placement is `Pause` then `set_time(0)` — the pair the game's own video players use to
        /// position a clip — and the seek is only asked of a prepared player, the state a seek is
        /// refused in being the one that has not opened at all. Both calls are made as early as they
        /// are because a seek leaves the render texture without the frame that was asked for: putting
        /// the clip here gives that blank the whole of the starting sequence to settle in, rather than
        /// running into it at the instant the song begins.
        /// </summary>
        private static void Park(out string reason)
        {
            if (Flag("get_isPrepared") && !Set("set_time", 1, 0.0, out reason))
                Diagnostics.Warn($"the background video could not be placed for the song: {reason}");

            if (!Call("Pause", 0, _player, out reason))
                Diagnostics.Warn($"the background video could not be held for the song: {reason}");
        }

        /// <summary>
        /// How far the picture may sit off the chart's clock before it is placed back onto it.
        ///
        /// A third of a second: an order under the three-second gap this exists to prevent, and wide
        /// enough that the landing of a placement — measured at about 0.18 s short on this build —
        /// does not start a correction loop of its own.
        /// </summary>
        private const double ClockSlack = 0.3;

        /// <summary>
        /// Beyond this the two are not the same picture — the clip has been left to loop past the
        /// song, or the clock has been somewhere else entirely — and placing the clip would be a
        /// guess. It has to leave room for the largest gap that is real: the resume puts the song
        /// back by the game's own lead-in, three seconds, and the picture is placed from there.
        /// Anything beyond the bound is left alone and shows up in the probe's `vidskew` reading.
        /// </summary>
        private const double Chase = 6.0;

        /// <summary>
        /// How long a placement is given to land before another is asked for. A seek is not
        /// instantaneous, and the player keeps answering with the position it is leaving; see
        /// <see cref="Place"/>, which is where the run that made this necessary is written down.
        /// </summary>
        private const long PlaceCooldownMs = 250;

        /// <summary>
        /// Said in every build when the clip cannot be placed where the song is: the picture then
        /// runs out of step with the chart, which is the one thing this class exists to prevent.
        /// </summary>
        private const string PlaceFailed =
            "the background video could not be placed on the chart clock; it will run out of step with the chart";

        private static bool _placeWarned;

        // The last clock the frame saw, and when the last placement was made. Both are `Place`'s:
        // the first tells a chart that is running from one that is over, the second keeps a seek
        // from being asked for again before the last one has landed.
        private static double _lastClock = double.NaN;
        private static long _lastPlace = long.MinValue;

        // The clip's own length, asked once per file — see `FilmOver` — and -1 until it is asked.
        private static double _length = -1.0;

        /// <summary>
        /// Said in every build when the clip cannot be started at all: the background keeps whatever
        /// frame the clip last showed — its first, when the failure is the very first play — and the
        /// picture then does not move for the song. The person playing is the one who would see that.
        /// </summary>
        private const string StartFailed =
            "the background video could not be started with the song; the background will not move with it";

        /// <summary>
        /// The frame: keeps the picture on the game's own chart clock.
        ///
        /// Called once per frame from the mod's own update, which is the one thing this mod has that
        /// runs while a chart does. Here, in order: the clip is started when the clock reaches the
        /// song's zero (see <see cref="Restart"/> for why not before it), nothing happens while the
        /// game's own music is held (<see cref="Hold"/> holds the clip too), and a picture that has
        /// slipped off the clock by more than <see cref="ClockSlack"/> is placed back onto it — the
        /// resume puts the song back by the countdown's remainder without telling anyone, and this is
        /// where that, and anything else, is made level again. Once the clip has played out, its last
        /// frame is left alone for the rest of the song (<see cref="FilmOver"/>).
        ///
        /// The park is the one thing that happens outside the armed part of the run: this build
        /// begins playing a prepared clip on its own, and a clip left doing that would open the scene
        /// on a frame that is already moving instead of on the still the background is meant to show.
        /// So a player that has started a clip of this mod's is caught and parked on the first frame,
        /// and the clock being before the song is what says the wait is still on — the self-play
        /// begins the moment the clip is prepared, seconds before the game hands the chart over.
        ///
        /// A run whose clock cannot be read is reported once and left off: a picture that cannot be
        /// placed on the chart is worse than no picture. A run with no clip of this mod's loaded — a
        /// shipped song, or a custom one that named no `background` — never gets here at all, because
        /// a parked clip is not playing one.
        /// </summary>
        internal static void Follow()
        {
            if (_player == IntPtr.Zero || _playing == null) return;

            double clock = ChartClock();
            if (double.IsNaN(clock))
            {
                // No clock, so the hold cannot be answered from this side any more: the `_KiA` road
                // is the one left, and `Resume` needs to know that (see its remarks).
                if (_held) _heldClock = double.NaN;
                return;                                  // -1 below is the lead-in; NaN is "no answer"
            }

            bool moved = clock != _lastClock;           // whether the chart is running at all; see `Place`
            _lastClock = clock;

            if (!_running && !_held && clock < 0.0 && Flag("get_isPlaying"))
            {
                // The player has started this clip on its own — see the summary. The frame is put back
                // and the clip held there; the seek is given the rest of the wait to settle, so what is
                // seen is a still and not the blank a seek leaves behind.
                string parked = null;
                Park(out parked);
                return;
            }

            if (!_armed) return;                        // nothing of this mod's is being set up or played

            if (_held)
            {
                // While the clip is held the game is paused. The resume is not announced to this mod,
                // and it does not have to be: the game answers it by putting the song back by its own
                // lead-in, so the clock drops — that is the first half, and it is what `_rewound`
                // records. The second half is the clock running forward again to where the picture
                // stopped and going on from there.
                //
                // The picture waits for that second half. It does not replay the lead-in with the
                // song, and it is not placed anywhere: it stands still until the song is back level
                // with it, and then plays on — which is the only way the pause ends with nothing to
                // see and nothing to seek. (An earlier build replayed the lead-in with the song, so
                // the picture moved through the countdown while the chart behind the menu had not
                // started yet; the one before that stayed frozen and then jumped forward to catch
                // up. Both were reported.)
                if (double.IsNaN(_heldClock)) return;    // nothing to compare against; `_KiA` will do it
                if (!_rewound)
                {
                    if (clock < _heldClock - ClockSlack) _rewound = true;
                    return;
                }

                double held = Time();                    // where the picture stopped
                if (held < 0.0) return;
                if (clock < held) return;                // the song is still coming back to it

                Resume();
                return;
            }

            if (_ended) return;                         // the clip played out; its last frame stays

            if (!_running)
            {
                if (clock >= 0.0) Start(clock);
                return;
            }

            if (FilmOver()) return;                     // the last frame stays; see `FilmOver`

            Place(clock, moved);
        }

        /// <summary>
        /// Whether the clip has played out — and if it has, the frame it stopped on stays on the
        /// background for the rest of the play.
        ///
        /// The clip is not looped, so its end arrives once and the player holds its last frame from
        /// there; the hold is made final by stopping the corrections that would otherwise keep
        /// pulling the picture toward a chart clock that has run past the film. Only a clock that has
        /// run past the film's own length counts: while the clip still has picture left, an end
        /// reported by the player is one this mod does not explain — a `Prepare` being overtaken by
        /// the play, a seek still landing — and it is placed back onto the clock it belongs to. The
        /// clip is a picture for the song, so the clock is the one reading that tells the two apart.
        ///
        /// The length is asked once per clip; a player that will not say answers with zero, which is
        /// never past, so a clip that cannot be measured keeps the correcting behaviour it had.
        /// </summary>
        private static bool FilmOver()
        {
            if (_ended) return true;
            if (_running && !Flag("get_isPlaying"))
            {
                double length = Length();
                double clock = ChartClock();
                if (length > 0.0 && !double.IsNaN(clock) && clock >= length)
                {
                    _ended = true;
                    _running = false;
                    Diagnostics.Info("background video played out; its last frame stays for the song");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How long the clip is, in seconds — or zero when the player will not say. Asked once per
        /// clip, because the answer cannot change while the file is the same; `get_length` answers
        /// with a double, boxed like every other value return.
        /// </summary>
        private static double Length()
        {
            if (_length >= 0.0) return _length;

            IntPtr method = Entry(_player, "get_length", 0);
            if (method == IntPtr.Zero) return 0.0;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return 0.0;

            return _length = *(double*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>
        /// The placing itself: the picture is put on the clock when it has slipped off it by more
        /// than <see cref="ClockSlack"/>, and left alone when the gap is not one a placement can
        /// answer.
        ///
        /// <paramref name="moved"/> is whether the clock has moved since the previous look. A clock
        /// that has not is a chart that is not running — the song is over, the results are up — and
        /// there is nothing left to place the picture on: chasing it there would be a seek a frame
        /// for as long as that screen lasts, which a run showed as minutes of them after the song.
        /// </summary>
        private static void Place(double clock, bool moved = true)
        {
            double at = Time();
            if (at < 0.0) return;

            double off = at - clock;
            if (off <= ClockSlack && off >= -ClockSlack) return;
            if (off > Chase || off < -Chase) return;     // not the same picture; see `Chase`
            if (!moved) return;

            // A placement is a seek, and a seek takes a moment to land: the player goes on answering
            // with the position it is leaving, so the very next frame finds the same gap and asks
            // again. Measured in a run: a seek every frame for twelve seconds after one resume, the
            // picture frozen for all of them and a log line each time. One placement is given time to
            // land before another is asked for, and the wait is in milliseconds rather than frames —
            // this game runs anywhere between sixty and a few hundred a second.
            long now = System.Environment.TickCount64;
            if (now - _lastPlace < PlaceCooldownMs) return;

            string reason = null;
            if (!Set("set_time", 1, clock, out reason))
            {
                _lastPlace = now;
#if DEBUG
                Diagnostics.Warn($"the background video could not be placed on the chart clock: {reason}");
#endif
                if (!_placeWarned)
                {
                    _placeWarned = true;
                    Diagnostics.WarnRelease(PlaceFailed);
                }
                return;
            }

            _lastPlace = now;
            Diagnostics.Info($"background video placed {off:+0.000;-0.000} s onto the chart clock ({at:F3} -> {clock:F3} s)");
        }

        /// <summary>
        /// Starts the clip at its first frame, once per chart.
        ///
        /// The clip was parked on that frame in <see cref="Restart"/> and its armed wait is guarded
        /// against being broken by the player's own start, so the clip is already there — and the park
        /// is asked for again so that it certainly is: its seek has had that whole wait to land, which
        /// is what keeps the blank a seek leaves behind from being run into at the instant the song
        /// begins. The seek is only asked of a prepared player, the state a seek is refused in being
        /// the one that has not opened at all.
        /// </summary>
        private static bool Start(double clock)
        {
            string reason = null;

            Park(out reason);
            if (!Show(_texture, out reason) || !Call("Play", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be started with the song: {reason}");
                Diagnostics.WarnRelease(StartFailed);
                _armed = false;                          // said once per chart, not once a frame
                return false;
            }

            _running = true;
            Starts++;
            Diagnostics.Info($"background video started with the song ({clock:F3} s on the chart clock)");
            return true;
        }

        /// <summary>
        /// The game's chart clock — the seconds its notes are timed against — or NaN when it cannot
        /// be read, which is reported once and switches the feature off.
        ///
        /// `GameScene` keeps the live engine in a static and the engine keeps the clock in a field;
        /// both are asked by name (see the fields above). Between songs, and while no gameplay scene
        /// is up, the static is empty and the answer is "not yet" — which is not a miss and is not
        /// latched.
        /// </summary>
        private static double ChartClock()
        {
            if (_clockMissing) return double.NaN;

            if (_gameSceneClass == IntPtr.Zero)
            {
                _gameSceneClass = FieldResolver.ClassPointer("GameScene");
                if (_gameSceneClass == IntPtr.Zero) return double.NaN;
            }

            if (_engineField == Offsets.Unresolved)
            {
                _engineField = FieldResolver.Field("GameScene", "_DN");
                _clockField = FieldResolver.Field("_VD", "_WEA");
                if (_engineField < 0 || _clockField < 0)
                {
                    _clockMissing = true;
                    Diagnostics.Error("the chart's clock could not be read by name in this build; " +
                                      "the in-play background video will not play");
                    return double.NaN;
                }
            }

            IntPtr statics = FieldResolver.Statics(_gameSceneClass);
            if (statics == IntPtr.Zero) return double.NaN;

            IntPtr engine = Memory.Ptr(statics + _engineField);
            if (!Memory.LooksLikeObject(engine)) return double.NaN;

            return Memory.F64(engine + _clockField);
        }

#if DEBUG
        /// <summary>
        /// The probe's reading: how far the picture sits off the chart's clock, in seconds (positive
        /// is the picture ahead). NaN when there is nothing to compare — no clip, no clock, or a
        /// player that will not say where it is.
        /// </summary>
        internal static double Skew
        {
            get
            {
                if (_player == IntPtr.Zero || _playing == null) return double.NaN;

                double clock = ChartClock();
                double at = Time();
                return double.IsNaN(clock) || at < 0.0 ? double.NaN : at - clock;
            }
        }
#endif

        /// <summary>
        /// Holds the clip where it is, because the game has just paused its own music.
        ///
        /// Called from the detour on `_Dg._hiA` — the music's own pause, whose only caller is the
        /// pause entry `GameScene._Vk` — so the picture stops at the same instant the song does. A
        /// clip that has not been started (no chart of this mod's is running, or one is still being
        /// set up) is left alone; `Restart` clears the hold that guards the other direction.
        ///
        /// <paramref name="at"/> is the position the music was held at — `_hiA`'s own argument,
        /// which the pause entry reads off the clock the gameplay runs on. It is what the reading
        /// below compares the clip against, and the resume's own reading has the other half.
        /// </summary>
        internal static void Hold(double at)
        {
            if (_player == IntPtr.Zero || !_running || _held) return;

            string reason = null;
            if (!Call("Pause", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be held for the pause: {reason}");
                return;
            }

            _held = true;
            _heldClock = ChartClock();      // what the resume will move back; see `Follow`
            _rewound = false;
            Holds++;
            Diagnostics.Info("background video held for the pause");

#if DEBUG
            // A reading, not a decision: where the clip says it is against where the song was held.
            // The chart started the two together, so these are the same number — and a gap here,
            // rather than at the resume, is what a picture that was never level looks like.
            _songAtHold = at;
            double held = Time();
            Diagnostics.Info(held < 0.0
                ? $"the clip would not say where it was; the song was held at {at:F3} s"
                : $"the clip is at {held:F3} s while the song was held at {at:F3} s");
#endif
        }

#if DEBUG
        /// <summary>
        /// The song's own position when the pause came, for the reading `Hold` writes. Inside
        /// `#if DEBUG` with the reading it serves: a Release build places the picture by the clip's
        /// own measured position, so a field that would be written and never read does not exist in
        /// it — this project builds warnings as errors, and that one is CS0414.
        /// </summary>
        private static double _songAtHold;
#endif

        /// <summary>
        /// Sets the clip running again, at the end of a pause.
        ///
        /// Called from two places, and only one of them is the right moment for the picture:
        ///
        /// <list type="bullet">
        /// <item><description>the frame (<see cref="Follow"/>), when the chart clock — which the
        /// resume put back — has run forward again to where the picture stopped. That is the picture's
        /// moment: it is level with the song there, so it plays on with nothing to seek.</description></item>
        /// <item><description>the detour on `_Dg._KiA`, the call the music comes back on. That one
        /// arrives a second early (the music is faded back in over the last second of the countdown),
        /// so it is only used when the clock could not be read and the frame cannot know its moment —
        /// <paramref name="atMusicReturn"/> says which road this is.</description></item>
        /// </list>
        ///
        /// Either way it does nothing unless the clip was actually held: a resume that arrives before
        /// any chart of this mod's has started must not start one, a song-end fade must not restart a
        /// running clip, and one that arrives after `Restart` (which clears the hold) is not a resume.
        /// Where the picture is placed from is the clock, which is where the `_KiA` road lands a
        /// second out and the frame road is already level. A clip that has played out stays on its
        /// last frame: there is nothing left of it to take up again.
        /// </summary>
        internal static void Resume(bool atMusicReturn = false)
        {
            if (_player == IntPtr.Zero || !_running || !_held) return;
            if (_ended) return;
            if (atMusicReturn && !double.IsNaN(_heldClock)) return;   // the frame's road is the right one

            _held = false;

            double clock = ChartClock();
            if (!double.IsNaN(clock)) Place(clock);

            string reason = null;
            if (!Call("Play", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be resumed: {reason}");
                return;
            }

            Resumes++;
            Diagnostics.Info("background video resumed with the song");
        }

        /// <summary>How many times a chart's start set the clip running. Reported by the probe.</summary>
        internal static long Starts;

        /// <summary>How many times the game's own pause held the clip, and resumed it. By the probe.</summary>
        internal static long Holds, Resumes;

        /// <summary>
        /// What the player is doing, for the probe: `none` before anything was asked for, `load`
        /// while the clip is being opened, `ready` when it is prepared but not running, `play` while
        /// it runs, `end` when the clip has played out and its last frame is what the background
        /// shows — and `fail` when it was asked for and could not be built.
        /// </summary>
        internal static string State
        {
            get
            {
                if (_unavailable) return "fail";
                if (_player == IntPtr.Zero) return "none";
                if (_ended) return "end";

                if (Flag("get_isPlaying")) return "play";
                if (Flag("get_isPrepared")) return "ready";
                return "load";
            }
        }

        /// <summary>
        /// A boolean property of the player, or false — including when it could not be asked.
        ///
        /// The method pointer is checked before it is handed to the runtime: this is the probe's own
        /// path, and a probe that can take the process down is worse than one that says "not yet".
        /// </summary>
        private static bool Flag(string name)
        {
            IntPtr method = Entry(_player, name, 0);
            if (method == IntPtr.Zero) return false;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            return raised == IntPtr.Zero && boxed != IntPtr.Zero &&
                   *(byte*)(boxed + Offsets.Runtime.BoxedData) != 0;
        }

        /// <summary>
        /// Where the clip is in its own picture, in seconds — or -1 when the player will not say.
        ///
        /// `VideoPlayer.time` answers with a double, which the runtime boxes like every other value
        /// return, and the box's payload is read the way <see cref="Flag"/> reads a bool's. Zero is
        /// a real answer — a clip at its first frame — so every way of not answering (no method, a
        /// raised call, nothing boxed) has to come back as the -1 instead, and does.
        /// </summary>
        private static double Time()
        {
            IntPtr method = Entry(_player, "get_time", 0);
            if (method == IntPtr.Zero) return -1.0;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1.0;

            return *(double*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>Stops the player, if there is one. Called when a song without a clip is entered.</summary>
        internal static void Stop()
        {
            if (_player == IntPtr.Zero) return;

            string reason = null;
            Call("Stop", 0, _player, out reason);

            _playing = null;
            _armed = false;
            _running = false;
            _held = false;
            _ended = false;
        }

        /// <summary>
        /// Builds the object, the player, the texture and the material, once.
        ///
        /// The order is forced: the player is a component, a component needs a GameObject, and the
        /// material needs the texture. Everything is created at the first use rather than at import
        /// because a video is only ever wanted while a chart is being played.
        /// </summary>
        private static bool Ensure(out string reason)
        {
            reason = null;
            if (_player != IntPtr.Zero) return true;

            IntPtr gameObjectClass = Require(CoreAssembly, CoreNamespace, "GameObject", out reason);
            if (gameObjectClass == IntPtr.Zero) return false;

            IntPtr videoClass = Require(VideoAssembly, VideoNamespace, "VideoPlayer", out reason);
            if (videoClass == IntPtr.Zero) return false;

            IntPtr textureClass = Require(CoreAssembly, CoreNamespace, "RenderTexture", out reason);
            if (textureClass == IntPtr.Zero) return false;

            IntPtr objectClass = Require(CoreAssembly, CoreNamespace, "Object", out reason);
            if (objectClass == IntPtr.Zero) return false;

            // The GameObject, named so that it is recognisable in a scene dump.
            IntPtr go = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(gameObjectClass);
            if (go == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's object could not be created";
#endif
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            IntPtr name = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp("InFalsusChartLoader background video");
            args[0] = name;
            if (!Invoke(Entry(go, ".ctor", 1), go, args, 1, out reason)) return false;

            // Survives the chart it was made for: the player keeps its own scene-independent state,
            // and a background asked for by the next song should not have to build all of this again.
            args[0] = go;
            if (!Invoke(EntryOn(objectClass, "DontDestroyOnLoad", 1), IntPtr.Zero, args, 1, out reason)) return false;

            // The component. `AddComponent(Type)` is the only overload a mod can reach without a
            // generated assembly reference: the `Type` comes from the runtime through
            // `il2cpp_type_get_object`, which is the object the method wants.
            IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(videoClass);
            if (type == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's type could not be read";
#endif
                return false;
            }

            args[0] = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(type);
            IntPtr player;
            if (!Invoke(Entry(go, "AddComponent", 1), go, args, 1, out reason)) return false;
            player = LastResult;
            if (!Memory.LooksLikeObject(player))
            {
#if DEBUG
                reason = "the background video's player could not be added";
#endif
                return false;
            }

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(player);
            if (!IsVideoPlayer(klass, videoClass))
            {
#if DEBUG
                reason = "the component added was not a VideoPlayer";
#endif
                return false;
            }

            // The texture, and the material over it. The material is built by the jacket factory,
            // which is the code that knows how to make a material out of a texture here.
            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(textureClass);
            if (texture == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's texture could not be created";
#endif
                return false;
            }

            int width = TextureWidth, height = TextureHeight, depth = 0;
            IntPtr* textureArgs = stackalloc IntPtr[3];
            textureArgs[0] = (IntPtr)(&width);
            textureArgs[1] = (IntPtr)(&height);
            textureArgs[2] = (IntPtr)(&depth);
            if (!Invoke(Entry(texture, ".ctor", 3), texture, textureArgs, 3, out reason)) return false;

            // A constructor that returned is not a texture that exists — the lesson the jacket
            // factory paid for — so the texture is asked to create itself and then asked how wide it
            // is. `Create` on an already-created texture is a no-op, and the width is the only
            // question a RenderTexture answers about itself.
            if (!Call("Create", 0, texture, out reason)) return false;

            int built = WidthOf(texture);
            if (built != TextureWidth)
            {
#if DEBUG
                reason = $"the background video's texture reports {built} pixels wide instead of {TextureWidth}";
#endif
                return false;
            }

            IntPtr material = JacketFactory.BuildFromTexture(texture, "background", out reason);
            if (material == IntPtr.Zero) return false;

            // Everything here is the mod's own and nothing in the game points at any of it, so the
            // collector is the only thing that could take it — and it does not scan static fields.
            _object = go;
            _player = player;
            _texture = texture;
            _material = material;
            Root(ref _objectHandle, go);
            Root(ref _playerHandle, player);
            Root(ref _textureHandle, texture);
            Root(ref _materialHandle, material);

            Diagnostics.Info("the background video player is ready");
            return true;
        }

        /// <summary>
        /// How wide a `RenderTexture` says it is, or -1 when it will not say. The getter lives on the
        /// base `Texture`, so it is looked up through the instance's own class chain.
        /// </summary>
        private static int WidthOf(IntPtr texture)
        {
            IntPtr getter = Entry(texture, "get_width", 0);
            if (getter == IntPtr.Zero) return -1;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, texture, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1;

            return *(int*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>
        /// Whether the component the game handed back is the player this asked for.
        ///
        /// `AddComponent` is asked by `Type`, and a `Type` that resolved to the wrong class would give
        /// back a component of that class and no error — the same shape as every other "it returned,
        /// so it worked" in this mod, checked the same way.
        /// </summary>
        private static bool IsVideoPlayer(IntPtr klass, IntPtr wanted)
        {
            for (IntPtr at = klass; at != IntPtr.Zero; at = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(at))
            {
                if (at == wanted) return true;
            }
            return false;
        }

        /// <summary>
        /// Holds an object against the collector for the rest of the run.
        ///
        /// A raw `IntPtr` is not a reference: IL2CPP's collector frees anything only this mod points
        /// at (this repository has the crash that taught it). These objects live as long as the
        /// process, so the handles are never released — the alternative is tracking teardown order
        /// for no gain.
        /// </summary>
        private static void Root(ref IntPtr handle, IntPtr obj)
        {
            if (handle != IntPtr.Zero || obj == IntPtr.Zero) return;
            handle = Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(obj, false);
        }

        /// <summary>The last value an invoked call answered with — how `AddComponent` returns its component.</summary>
        private static IntPtr LastResult;

        /// <summary>Calls a no-argument method that answers with an object.</summary>
        private static bool Call(string name, int parameters, IntPtr instance, out string reason)
        {
            reason = null;

            IntPtr method = Entry(instance, name, parameters);
            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = $"VideoPlayer.{name} could not be found";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref raised);
            LastResult = result;
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"VideoPlayer.{name} raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        /// <summary>
        /// Sets one property of the player. What the invoker reads for that parameter is what is
        /// handed over verbatim: a <b>pointer to the value's own bytes</b> for a value type, and the
        /// <b>reference itself</b> for an object or a string — the convention `JacketFactory.Invoke`
        /// was measured against, which is why a number and a reference are built differently here.
        /// </summary>
        private static unsafe bool Set(string name, int parameters, int value, out string reason)
        {
            int storage = value;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static unsafe bool Set(string name, int parameters, double value, out string reason)
        {
            double storage = value;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static unsafe bool Set(string name, int parameters, bool value, out string reason)
        {
            byte storage = value ? (byte)1 : (byte)0;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static bool Set(string name, int parameters, IntPtr reference, out string reason) =>
            SetArgument(name, reference, out reason);

        private static bool Set(string name, int parameters, string value, out string reason) =>
            SetArgument(name, Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(value), out reason);

        private static unsafe bool SetArgument(string name, IntPtr argument, out string reason)
        {
            reason = null;

            if (_player == IntPtr.Zero)
            {
#if DEBUG
                reason = "there is no background video player";
#endif
                return false;
            }

            IntPtr method = Entry(_player, name, 1);
            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = $"VideoPlayer.{name} could not be found";
#endif
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = argument;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"VideoPlayer.{name} raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        /// <summary>
        /// The picture the clip shows, put on the material given: `_material` in the ordinary way,
        /// and a freshly made clone in <see cref="Rebase"/>, asked before the clone takes over. What
        /// the background draws is whatever texture the material wears at the time of the draw,
        /// which is what lets black stand in for the clip until the chart starts.
        ///
        /// The reasons are written whether or not this is a Debug build: `Rebase`'s failure walks
        /// out to one Release-visible Error, so the string has to exist in Release to be read there.
        /// </summary>
        private static bool Show(IntPtr material, IntPtr texture, out string reason)
        {
            reason = null;

            if (material == IntPtr.Zero) return false;

            IntPtr method = Entry(material, "set_mainTexture", 1);
            if (method == IntPtr.Zero)
            {
                reason = "Material.set_mainTexture could not be found";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = texture;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, material, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

            reason = $"Material.set_mainTexture raised: {Raised.Text(raised)}";
            return false;
        }

        private static bool Show(IntPtr texture, out string reason) => Show(_material, texture, out reason);

        /// <summary>An absolute Windows path, as the URL a `VideoPlayer` opens a local file by.</summary>
        private static string FileUrl(string path) => "file:///" + path.Replace('\\', '/');

        /// <summary>
        /// A method of an instance's own class, or zero.
        ///
        /// Asked on the instance rather than on the class the mod resolved, which is the same object
        /// only because `IsVideoPlayer` checked it is.
        /// </summary>
        private static IntPtr Entry(IntPtr instance, string name, int parameters)
        {
            if (!Memory.LooksLikeObject(instance)) return IntPtr.Zero;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance), name, parameters);
        }

        /// <summary>
        /// A method of a class the mod resolved itself, where there is no instance to ask — the
        /// static `DontDestroyOnLoad` is the only one.
        ///
        /// `Entry` asks an <b>instance's</b> class. Handed a class pointer it reads that class's first
        /// word as if it were the class, and has the runtime search <i>that</i> — which is not an
        /// `Il2CppClass` — down its parent chain until it faults inside
        /// `il2cpp_class_get_method_from_name`. That is how the video path's first Debug run died,
        /// so an instance lookup and a class lookup are kept apart rather than sharing one entry.
        /// </summary>
        private static IntPtr EntryOn(IntPtr klass, string name, int parameters)
        {
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);
        }

        /// <summary>A Unity class, by assembly and name — the route `JacketFactory.Require` uses.</summary>
        private static IntPtr Require(string assembly, string namespaze, string typeName, out string reason)
        {
            reason = null;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(assembly, namespaze, typeName);
            if (klass == IntPtr.Zero)
            {
#if DEBUG
                reason = $"the game's {typeName} type could not be found";
#endif
                return IntPtr.Zero;
            }

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
            return klass;
        }

        /// <summary>
        /// Calls a method the way the generated interop does: through `il2cpp_runtime_invoke`, with
        /// each argument either the reference itself or a pointer to the argument's own bytes — see
        /// `JacketFactory.Invoke`, which is where that convention was measured.
        /// </summary>
        private static unsafe bool Invoke(IntPtr method, IntPtr instance, IntPtr* args, int count, out string reason)
        {
            reason = null;

            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = "a background video method could not be found";
#endif
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (args[i] != IntPtr.Zero) continue;
#if DEBUG
                reason = $"argument {i} could not be built, so the call was not made";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            LastResult = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"a call into the game's video types raised: {Raised.Text(raised)}";
#endif
            return false;
        }
    }
}
