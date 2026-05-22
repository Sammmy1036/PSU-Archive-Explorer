using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace psu_archive_explorer
{
    /// <summary>
    /// Embeddable SFD video preview panel for the right pane of MainForm.
    /// Mirrors the pattern used by the ADX preview: Play / Stop buttons,
    /// a status label, and a progress bar with "mm:ss / mm:ss" readout.
    ///
    /// Takes already-extracted .sfd bytes (archive-embedded case) or a file
    /// path (standalone on-disk case). Decodes video via Mpeg1Decoder
    /// (pl_mpeg.dll) and audio via NAudio, exactly the same pipeline as the
    /// standalone SFDPlayer form.
    /// </summary>
    public class SfdPreviewPanel : UserControl
    {
        // ---- UI ----
        private PictureBox _pictureBox;
        private Panel _controlBar;
        private Button _btnPlay;
        private Button _btnStop;
        private Label _statusLabel;
        private TrackBar _seekBar;
        private Label _timeLabel;

        // ---- Playback state ----
        private CancellationTokenSource _playCts;
        private WaveOutEvent _waveOut;
        private WaveFileReader _waveReader;
        private string _tempWavPath;

        private volatile bool _isPaused = true;
        private volatile bool _stopped = false;
        private volatile bool _seekPending = false;
        private volatile int _seekTargetFrame = 0;

        private int _totalFrames = 0;
        private double _framerate = 30000.0 / 1001.0;
        private byte[] _sfdBytes;
        private string _displayName;

        // Guard against re-entrant seekbar updates while we set the value
        // programmatically from the decode thread.
        private bool _suppressSeekEvent = false;

        public SfdPreviewPanel()
        {
            InitializeControls();
        }

        // ==================================================================
        // UI construction
        // ==================================================================
        private void InitializeControls()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(229, 229, 229);

            // Video surface
            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            // Bottom control bar — fixed height, hosts buttons + seek + time
            _controlBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                BackColor = Color.FromArgb(229, 229, 229)
            };

            _btnPlay = new Button
            {
                Text = "▶ Play",
                Size = new Size(80, 28),
                Location = new Point(12, 10),
                Enabled = false
            };
            _btnPlay.Click += (s, e) => TogglePlayPause();

            _btnStop = new Button
            {
                Text = "■ Stop",
                Size = new Size(80, 28),
                Location = new Point(100, 10),
                Enabled = false
            };
            _btnStop.Click += (s, e) => Stop();

            _statusLabel = new Label
            {
                Text = "Decoding...",
                Location = new Point(196, 14),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _seekBar = new TrackBar
            {
                Location = new Point(12, 44),
                Width = 500,
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Enabled = false
            };
            _seekBar.Scroll += OnSeekBarScroll;

            _timeLabel = new Label
            {
                Text = "0:00 / 0:00",
                Location = new Point(12, 70),
                AutoSize = true,
                ForeColor = Color.Black
            };

            _controlBar.Controls.Add(_btnPlay);
            _controlBar.Controls.Add(_btnStop);
            _controlBar.Controls.Add(_statusLabel);
            _controlBar.Controls.Add(_seekBar);
            _controlBar.Controls.Add(_timeLabel);

            this.Controls.Add(_pictureBox);
            this.Controls.Add(_controlBar);
        }

        // ==================================================================
        // Public entry points
        // ==================================================================

        /// <summary>
        /// Load SFD data from a byte buffer (archive-embedded case).
        /// Kicks off demux + decode on a background thread.
        /// </summary>
        public void LoadSfd(byte[] sfdBytes, string displayName)
        {
            _sfdBytes = sfdBytes;
            _displayName = displayName;

            _playCts?.Cancel();
            DisposeAudio();
            _isPaused = true;
            _stopped = false;

            SetStatus("Decoding " + (_displayName ?? "...") + "...");
            _btnPlay.Enabled = false;
            _btnStop.Enabled = false;
            _seekBar.Enabled = false;
            _seekBar.Value = 0;
            _timeLabel.Text = "0:00 / 0:00";

            _playCts = new CancellationTokenSource();
            var token = _playCts.Token;
            Task.Run(() => ProcessSfd(token));
        }

        /// <summary>
        /// Load SFD data from a file path (standalone on-disk case).
        /// </summary>
        public void LoadSfdFromFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                LoadSfd(bytes, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetStatus("Failed to read file: " + ex.Message);
            }
        }

        // ==================================================================
        // Decode pipeline (runs on background task)
        // ==================================================================
        private void ProcessSfd(CancellationToken ct)
        {
            Mpeg1Decoder decoder = null;
            try
            {
                var demux = new SofdecDemuxer();
                demux.Parse(_sfdBytes);

                byte[] videoEs = demux.GetVideoPayload();
                byte[] headerlessAdx = demux.GetAdxPayload();

                if (ct.IsCancellationRequested) return;

                // Build audio (NAudio WaveOutEvent, same as SFDPlayer)
                if (headerlessAdx.Length > 0)
                {
                    byte[] fullAdx = BuildAdxWithHeader(
                        headerlessAdx, demux.Channels, demux.SampleRate);
                    byte[] wavBytes = AdxDecoder.DecodeToWav(fullAdx);

                    // Capture the path into a local so the lambda below uses a
                    // stable value even if DisposeAudio() nulls _tempWavPath
                    // during the same InvokeSafe call, or another LoadSfd
                    // starts before the lambda runs.
                    string wavPath = Path.Combine(Path.GetTempPath(),
                        "sfdprev_" + Guid.NewGuid().ToString("N") + ".wav");
                    File.WriteAllBytes(wavPath, wavBytes);

                    if (ct.IsCancellationRequested)
                    {
                        try { File.Delete(wavPath); } catch { }
                        return;
                    }

                    InvokeSafe(() =>
                    {
                        // Dispose any prior audio first, then set the new path
                        // and open the reader. Order matters: DisposeAudio
                        // clears _tempWavPath, so we assign AFTER disposing.
                        DisposeAudio();
                        _tempWavPath = wavPath;
                        _waveReader = new WaveFileReader(wavPath);
                        _waveOut = new WaveOutEvent();
                        _waveOut.Init(_waveReader);
                    });
                }

                if (videoEs.Length < 12)
                {
                    InvokeSafe(() => SetStatus("No video stream found."));
                    return;
                }

                decoder = new Mpeg1Decoder(videoEs);
                _framerate = decoder.Framerate > 0 ? decoder.Framerate : 30000.0 / 1001.0;
                double frameIntervalMs = 1000.0 / _framerate;

                // Count MPEG-1 picture start codes (00 00 01 00) in the video
                // ES to get the exact frame count without decoding. This is a
                // fast byte scan — no YUV conversion, no bitmap allocation —
                // so even a 500 MB file completes in well under a second.
                // pl_mpeg exposes no duration/frame-count API, and estimating
                // from ADX audio length is unreliable (audio is often longer).
                _totalFrames = CountMpeg1Frames(videoEs);

                InvokeSafe(() =>
                {
                    _btnPlay.Enabled = true;
                    _btnStop.Enabled = true;
                    _seekBar.Enabled = true;
                    if (_totalFrames > 0) _seekBar.Maximum = _totalFrames;
                    SetStatus("Ready to play " + (_displayName ?? ""));
                    UpdateTimeLabel(0);
                });

                // Tracks the true number of frames decoded; used after the loop
                // to correct _totalFrames and the seekbar maximum to the real value.
                int actualFrames = 0;

                var enumerator = decoder.DecodeFrames().GetEnumerator();
                var sw = new System.Diagnostics.Stopwatch();

                int frameCount = 0;
                int framesDropped = 0;
                int errorCount = 0;
                bool audioStarted = false;
                long pauseElapsedMs = 0;

                while (!ct.IsCancellationRequested)
                {
                    Bitmap frame;
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        frame = enumerator.Current;
                    }
                    catch
                    {
                        errorCount++;
                        if (errorCount > 5) break;
                        continue;
                    }
                    if (frame == null) continue;
                    frameCount++;
                    actualFrames++;

                    // -- Pause loop: hold current frame visible --
                    bool showedWhilePaused = false;
                    long pauseStartMs = 0;
                    bool wasPaused = false;
                    while (_isPaused && !ct.IsCancellationRequested && !_stopped)
                    {
                        if (!showedWhilePaused)
                        {
                            ShowFrame(frame);
                            showedWhilePaused = true;
                        }
                        if (!wasPaused)
                        {
                            pauseStartMs = sw.ElapsedMilliseconds;
                            wasPaused = true;
                        }
                        Thread.Sleep(30);
                    }

                    if (ct.IsCancellationRequested || _stopped)
                    {
                        if (!showedWhilePaused) frame.Dispose();
                        return;
                    }

                    if (wasPaused && sw.IsRunning)
                        pauseElapsedMs += sw.ElapsedMilliseconds - pauseStartMs;

                    if (!audioStarted)
                    {
                        try { _waveOut?.Play(); } catch { }
                        audioStarted = true;
                        sw.Restart();
                        pauseElapsedMs = 0;
                    }

                    double targetMs = (frameCount - 1) * frameIntervalMs;
                    double nowMs = sw.Elapsed.TotalMilliseconds - pauseElapsedMs;
                    double deltaMs = targetMs - nowMs;

                    if (deltaMs > 1)
                    {
                        Thread.Sleep((int)Math.Min(deltaMs, 50));
                    }
                    else if (deltaMs < -frameIntervalMs * 2)
                    {
                        if (!showedWhilePaused) frame.Dispose();
                        framesDropped++;
                        UpdateProgress(frameCount);
                        continue;
                    }

                    if (!showedWhilePaused) ShowFrame(frame);
                    UpdateProgress(frameCount);
                }

                // End of stream: correct _totalFrames to the real decoded
                // frame count so the seekbar maximum reflects actual video
                // length rather than the (often longer) ADX audio duration.
                if (actualFrames > 0)
                {
                    _totalFrames = actualFrames;
                    InvokeSafe(() =>
                    {
                        _suppressSeekEvent = true;
                        try
                        {
                            _seekBar.Maximum = _totalFrames;
                            _seekBar.Value = _totalFrames;
                        }
                        finally { _suppressSeekEvent = false; }
                        UpdateTimeLabel(_totalFrames);
                    });
                }

                // End of stream — stop audio, reset UI
                InvokeSafe(() =>
                {
                    try { _waveOut?.Stop(); } catch { }
                    _isPaused = true;
                    _btnPlay.Text = "▶ Play";
                    SetStatus("Finished " + (_displayName ?? ""));
                });
            }
            catch (OutOfMemoryException)
            {
                InvokeSafe(() =>
                {
                    SetStatus("Out of memory — file too large to preview in this build.");
                    _btnPlay.Enabled = false;
                    _btnStop.Enabled = false;
                    _seekBar.Enabled = false;
                });
            }
            catch (DllNotFoundException)
            {
                InvokeSafe(() =>
                    SetStatus("pl_mpeg.dll not found — video preview unavailable."));
            }
            catch (Exception ex)
            {
                InvokeSafe(() =>
                    SetStatus("Decode error: " + ex.Message));
            }
            finally
            {
                try { decoder?.Dispose(); } catch { }
            }
        }

        // ==================================================================
        // UI helpers (thread-safe)
        // ==================================================================
        private void ShowFrame(Bitmap bmp)
        {
            try
            {
                InvokeSafe(() =>
                {
                    var old = _pictureBox.Image;
                    _pictureBox.Image = bmp;
                    old?.Dispose();
                });
            }
            catch { try { bmp.Dispose(); } catch { } }
        }

        private void UpdateProgress(int frameCount)
        {
            InvokeSafe(() =>
            {
                if (_seekBar.Maximum > 0)
                {
                    _suppressSeekEvent = true;
                    try
                    {
                        int val = Math.Min(frameCount, _seekBar.Maximum);
                        if (val >= _seekBar.Minimum) _seekBar.Value = val;
                    }
                    finally { _suppressSeekEvent = false; }
                }
                UpdateTimeLabel(frameCount);
            });
        }

        private void UpdateTimeLabel(int frameCount)
        {
            double curSec = frameCount / _framerate;
            double totalSec = _totalFrames > 0 ? _totalFrames / _framerate : 0;
            _timeLabel.Text = $"{FormatTime(curSec)} / {FormatTime(totalSec)}";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            int s = (int)Math.Round(seconds);
            return $"{s / 60}:{s % 60:D2}";
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void InvokeSafe(Action a)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(a);
            }
            catch (InvalidOperationException) { }
        }

        // ==================================================================
        // Transport controls
        // ==================================================================
        private void TogglePlayPause()
        {
            _isPaused = !_isPaused;
            _btnPlay.Text = _isPaused ? "▶ Play" : "❚❚ Pause";
            string name = _displayName ?? "";
            SetStatus(_isPaused ? ("Paused " + name) : ("Playing " + name));

            try
            {
                if (_waveOut != null)
                {
                    if (_isPaused) _waveOut.Pause();
                    else _waveOut.Play();
                }
            }
            catch { }
        }

        private void Stop()
        {
            // "Stop" in the ADX sense: halt playback and reset to the beginning.
            // Simplest reliable approach: cancel the current decode and reload
            // from the cached bytes.
            if (_sfdBytes == null) return;

            _playCts?.Cancel();
            _stopped = true;
            try { _waveOut?.Stop(); } catch { }
            _isPaused = true;
            _btnPlay.Text = "▶ Play";
            SetStatus("Stopped " + (_displayName ?? ""));

            // Reload from cached bytes so Play starts from frame 0 again.
            LoadSfd(_sfdBytes, _displayName);
        }

        private void OnSeekBarScroll(object sender, EventArgs e)
        {
            if (_suppressSeekEvent) return;
            // pl_mpeg doesn't expose random access through our wrapper, so real
            // seek isn't wired up here. Snap the thumb back to current play
            // position on user scroll to avoid misleading UX.
            _suppressSeekEvent = true;
            try { /* position is rewritten on next UpdateProgress tick */ }
            finally { _suppressSeekEvent = false; }
        }

        // ==================================================================
        // MPEG-1 frame counter
        // ==================================================================

        /// <summary>
        /// Counts MPEG-1 picture start codes (00 00 01 00) in a video
        /// elementary stream. Each picture start code corresponds to exactly
        /// one frame, so this gives an exact frame count with no decoding.
        /// </summary>
        private static int CountMpeg1Frames(byte[] es)
        {
            int count = 0;
            int end = es.Length - 3;
            for (int i = 0; i < end; i++)
            {
                // Picture start code: 00 00 01 00
                if (es[i] == 0x00 &&
                    es[i + 1] == 0x00 &&
                    es[i + 2] == 0x01 &&
                    es[i + 3] == 0x00)
                {
                    count++;
                    i += 3; // skip past this start code
                }
            }
            return count > 0 ? count : 1;
        }

        // ==================================================================
        // ADX header (copied from SFDPlayer — unchanged)
        // ==================================================================
        private static byte[] BuildAdxWithHeader(byte[] frames, int channels, int sampleRate)
        {
            const int BlockSize = 18;
            const int SamplesPerBlock = 32;

            int frameStride = BlockSize * channels;
            int completeFrames = frames.Length / frameStride;
            int totalSamples = completeFrames * SamplesPerBlock;
            int usableBytes = completeFrames * frameStride;

            const int CopyrightOffset = 0x001C;
            const int AudioStart = CopyrightOffset + 4;

            byte[] adx = new byte[AudioStart + usableBytes];
            adx[0] = 0x80; adx[1] = 0x00;
            adx[2] = (byte)(CopyrightOffset >> 8);
            adx[3] = (byte)(CopyrightOffset & 0xFF);
            adx[4] = 0x03;
            adx[5] = BlockSize;
            adx[6] = 4;
            adx[7] = (byte)channels;
            adx[8] = (byte)(sampleRate >> 24);
            adx[9] = (byte)(sampleRate >> 16);
            adx[10] = (byte)(sampleRate >> 8);
            adx[11] = (byte)(sampleRate & 0xFF);
            adx[12] = (byte)(totalSamples >> 24);
            adx[13] = (byte)(totalSamples >> 16);
            adx[14] = (byte)(totalSamples >> 8);
            adx[15] = (byte)(totalSamples & 0xFF);
            adx[16] = 0x01; adx[17] = 0xF4;
            adx[18] = 0x03; adx[19] = 0x00;
            byte[] cri = System.Text.Encoding.ASCII.GetBytes("(c)CRI");
            Array.Copy(cri, 0, adx, CopyrightOffset + 2, 6);
            Array.Copy(frames, 0, adx, AudioStart, usableBytes);
            return adx;
        }

        // ==================================================================
        // Cleanup
        // ==================================================================
        private void DisposeAudio()
        {
            try { _waveOut?.Stop(); } catch { }
            try { _waveOut?.Dispose(); } catch { }
            try { _waveReader?.Dispose(); } catch { }
            _waveOut = null;
            _waveReader = null;

            // Best-effort temp wav cleanup
            if (!string.IsNullOrEmpty(_tempWavPath))
            {
                try { if (File.Exists(_tempWavPath)) File.Delete(_tempWavPath); } catch { }
                _tempWavPath = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _playCts?.Cancel(); } catch { }
                _stopped = true;
                DisposeAudio();

                try
                {
                    var img = _pictureBox?.Image;
                    if (_pictureBox != null) _pictureBox.Image = null;
                    img?.Dispose();
                }
                catch { }

                _sfdBytes = null;
            }
            base.Dispose(disposing);
        }
    }
}