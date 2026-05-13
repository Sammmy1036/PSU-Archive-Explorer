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
    /// Embeddable DAT sound preview panel. Decodes via DatConverter.DecodeToWav
    /// (for the self-loading constructors) or accepts already-decoded WAV bytes
    /// pushed in from outside (for the async-archive-load scenario).
    ///
    /// Three construction modes:
    ///   1. (string filePath, string infoText)
    ///        — panel reads the .dat from disk on a background thread,
    ///          decodes, then plays.
    ///   2. (byte[] datBytes, string infoText)
    ///        — panel decodes the in-memory .dat bytes on a background thread,
    ///          then plays.
    ///   3. (string infoText, bool showAsDecoding)
    ///        — panel renders immediately in "Decoding..." state and waits for
    ///          an external caller to push results via SetDecodedWav() or
    ///          SetDecodeError(). Used when the caller is already running its
    ///          own background task (e.g. extracting a DAT out of an archive
    ///          and decoding it in one pass, off the UI thread).
    ///
    /// In all modes playback is stopped and NAudio resources released on
    /// dispose.
    /// </summary>
    public class DatPreviewPanel : UserControl
    {
        // ---- Audio state ----
        private WaveOutEvent outputDevice;
        private WaveFileReader waveReader;
        private MemoryStream wavStream;

        // ---- UI ----
        private readonly Label lblInfo;
        private readonly Panel playerPanel;
        private readonly Button btnPlayPause;
        private readonly Button btnStop;
        private readonly TrackBar trackBarProgress;
        private readonly Label lblTime;
        private readonly Label lblStatus;

        private readonly System.Windows.Forms.Timer progressTimer = new System.Windows.Forms.Timer();
        private bool isUserSeeking = false;
        private bool audioReady = false;

        // Display name of the .dat file currently loaded (e.g. "se_evt_001.dat").
        // Appended to the Playing / Paused / Stopped status strings so the user
        // can see what's actually being previewed — mirrors what SfdPreviewPanel
        // does with its _displayName field.
        private string _displayName;

        // Input source — at most one of these is populated. For the
        // "external provider" mode both are null and we just wait.
        private readonly byte[] datBytes;
        private readonly string filePath;

        private readonly CancellationTokenSource decodeCts = new CancellationTokenSource();

        /// <summary>Cancellation token external callers can observe.</summary>
        public CancellationToken DecodeCancellationToken => decodeCts.Token;

        /// <summary>
        /// True once the panel has been disposed; external async callers should
        /// check this before marshalling a SetDecodedWav call.
        /// </summary>
        public bool IsCancelledOrDisposed =>
            IsDisposed || Disposing || decodeCts.IsCancellationRequested;

        /// <summary>Create a preview panel for a standalone .dat file on disk.</summary>
        public DatPreviewPanel(string filePath, string infoText)
            : this(infoText)
        {
            this.filePath = filePath;
            // For the on-disk case we already have the filename — use it for
            // the playback status strings without forcing the caller to pass
            // it in separately.
            try { _displayName = Path.GetFileName(filePath); } catch { }
            BeginLoadDatAsync();
        }

        /// <summary>Create a preview panel from DAT bytes already in memory.</summary>
        public DatPreviewPanel(byte[] datBytes, string infoText)
            : this(infoText)
        {
            this.datBytes = datBytes;
            BeginLoadDatAsync();
        }

        /// <summary>
        /// Create a preview panel that shows the "Decoding..." state immediately
        /// but expects the caller to push decoded WAV bytes (or an error) via
        /// <see cref="SetDecodedWav"/> / <see cref="SetDecodeError"/>.
        /// </summary>
        public DatPreviewPanel(string infoText, bool externalProvider)
            : this(infoText)
        {
            // No decode started — we wait for the caller.
        }

        private DatPreviewPanel(string infoText)
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(229, 229, 229);

            lblInfo = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5f),
                Text = infoText,
                Padding = new Padding(10)
            };

            playerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                BackColor = Color.FromArgb(255, 255, 255),
                Padding = new Padding(10)
            };

            btnPlayPause = new Button
            {
                Text = "▶ Play",
                Location = new Point(10, 10),
                Size = new Size(100, 32),
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            btnPlayPause.Click += (s, e) => TogglePlayPause();

            btnStop = new Button
            {
                Text = "⏹ Stop",
                Location = new Point(120, 10),
                Size = new Size(80, 32),
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            btnStop.Click += (s, e) => StopPlayback();

            lblStatus = new Label
            {
                Location = new Point(210, 17),
                AutoSize = true,
                ForeColor = Color.DarkGray,
                Font = new Font("Segoe UI", 9F),
                Text = "Decoding..."
            };

            trackBarProgress = new TrackBar
            {
                Location = new Point(10, 50),
                Minimum = 0,
                Maximum = 1000,
                TickFrequency = 50,
                TickStyle = TickStyle.None,
                Enabled = false
            };
            trackBarProgress.Scroll += (s, e) => UpdateTimeLabel();
            trackBarProgress.MouseDown += (s, e) => isUserSeeking = true;
            trackBarProgress.MouseUp += (s, e) => { isUserSeeking = false; SeekToPosition(); };

            lblTime = new Label
            {
                Location = new Point(10, 105),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Text = "0:00 / 0:00"
            };

            playerPanel.Controls.Add(btnPlayPause);
            playerPanel.Controls.Add(btnStop);
            playerPanel.Controls.Add(lblStatus);
            playerPanel.Controls.Add(trackBarProgress);
            playerPanel.Controls.Add(lblTime);

            playerPanel.Resize += (s, e) =>
            {
                trackBarProgress.Width = playerPanel.ClientSize.Width - 20;
            };

            this.Controls.Add(lblInfo);
            this.Controls.Add(playerPanel);

            progressTimer.Interval = 200;
            progressTimer.Tick += ProgressTimer_Tick;
        }

        private void BeginLoadDatAsync()
        {
            CancellationToken ct = decodeCts.Token;

            Task.Run(() =>
            {
                try
                {
                    byte[] raw = datBytes ?? File.ReadAllBytes(filePath);
                    ct.ThrowIfCancellationRequested();
                    byte[] wavBytes = DatConverter.DecodeToWav(raw);
                    return new DecodeResult { Success = true, WavBytes = wavBytes };
                }
                catch (OperationCanceledException)
                {
                    return new DecodeResult { Canceled = true };
                }
                catch (Exception ex)
                {
                    return new DecodeResult { Success = false, ErrorMessage = $"Preview failed: {ex.Message}" };
                }
            }, ct).ContinueWith(t =>
            {
                if (ct.IsCancellationRequested) return;
                if (IsDisposed || Disposing) return;

                DecodeResult result = t.Result;
                if (result.Canceled) return;

                try
                {
                    if (InvokeRequired)
                        Invoke((Action)(() => OnDecodeComplete(result)));
                    else
                        OnDecodeComplete(result);
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Called by an external async producer to push decoded WAV bytes into
        /// this panel. Safe to call from any thread; marshals to the UI thread
        /// internally. No-op if the panel has been disposed or cancelled.
        /// </summary>
        public void SetDecodedWav(byte[] wavBytes)
        {
            if (IsCancelledOrDisposed) return;
            var result = new DecodeResult { Success = true, WavBytes = wavBytes };
            TryInvokeComplete(result);
        }

        /// <summary>
        /// Called by an external async producer to report a decode error.
        /// Safe to call from any thread.
        /// </summary>
        public void SetDecodeError(string message)
        {
            if (IsCancelledOrDisposed) return;
            var result = new DecodeResult { Success = false, ErrorMessage = message };
            TryInvokeComplete(result);
        }

        private void TryInvokeComplete(DecodeResult result)
        {
            try
            {
                if (InvokeRequired)
                    Invoke((Action)(() => OnDecodeComplete(result)));
                else
                    OnDecodeComplete(result);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// Update the info label shown above the player controls. Safe to call from
        /// any thread; marshals to the UI thread internally. No-op if the panel has
        /// been disposed or cancelled.
        /// </summary>
        public void SetInfoText(string infoText)
        {
            if (IsCancelledOrDisposed) return;

            try
            {
                if (InvokeRequired)
                    Invoke((Action)(() => { if (!IsDisposed) lblInfo.Text = infoText; }));
                else
                    lblInfo.Text = infoText;
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// Set the display name used in the playback status strings (e.g.
        /// "Playing foo.dat"). Used by the external-provider construction path
        /// where the caller knows the filename but it isn't available through
        /// the panel itself (archive-extracted DATs have no file on disk).
        /// Safe to call from any thread.
        /// </summary>
        public void SetDisplayName(string displayName)
        {
            if (IsCancelledOrDisposed) return;

            try
            {
                if (InvokeRequired)
                    Invoke((Action)(() => { if (!IsDisposed) _displayName = displayName; }));
                else
                    _displayName = displayName;
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnDecodeComplete(DecodeResult result)
        {
            if (IsDisposed || Disposing) return;

            if (!result.Success)
            {
                DisablePlayer(result.ErrorMessage);
                return;
            }

            try
            {
                wavStream = new MemoryStream(result.WavBytes);
                waveReader = new WaveFileReader(wavStream);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(waveReader);
                outputDevice.PlaybackStopped += PlaybackStoppedHandler;

                trackBarProgress.Maximum = (int)Math.Min(waveReader.Length, int.MaxValue);

                audioReady = true;
                btnPlayPause.Enabled = true;
                btnStop.Enabled = true;
                trackBarProgress.Enabled = true;
                lblStatus.Text = "Ready to play";
                lblStatus.ForeColor = Color.DarkGray;
                UpdateTimeLabel();
            }
            catch (Exception ex)
            {
                DisablePlayer($"Preview failed: {ex.Message}");
            }
        }

        private void DisablePlayer(string reason)
        {
            btnPlayPause.Enabled = false;
            btnStop.Enabled = false;
            trackBarProgress.Enabled = false;
            lblStatus.Text = reason;
            lblStatus.ForeColor = Color.Firebrick;
        }

        private void TogglePlayPause()
        {
            if (!audioReady || outputDevice == null || waveReader == null) return;

            // Trailing space + name so "Playing" stays clean when no name is
            // known. Matches the pattern used by SfdPreviewPanel.
            string nameSuffix = string.IsNullOrEmpty(_displayName) ? "" : " " + _displayName;

            try
            {
                if (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    outputDevice.Pause();
                    btnPlayPause.Text = "▶ Play";
                    lblStatus.Text = "⏸ Paused" + nameSuffix;
                    progressTimer.Stop();
                }
                else
                {
                    if (waveReader.Position >= waveReader.Length)
                        waveReader.Position = 0;

                    outputDevice.Play();
                    btnPlayPause.Text = "⏸ Pause";
                    lblStatus.Text = "▶ Playing" + nameSuffix;
                    progressTimer.Start();
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Playback error: {ex.Message}";
            }
        }

        private void StopPlayback()
        {
            if (outputDevice == null || waveReader == null) return;

            try
            {
                outputDevice.Stop();
                waveReader.Position = 0;
                trackBarProgress.Value = 0;
                btnPlayPause.Text = "▶ Play";
                lblStatus.Text = string.IsNullOrEmpty(_displayName)
                    ? "Stopped"
                    : "Stopped " + _displayName;
                UpdateTimeLabel();
                progressTimer.Stop();
            }
            catch { }
        }

        private void PlaybackStoppedHandler(object sender, StoppedEventArgs args)
        {
            if (IsDisposed || Disposing) return;

            if (InvokeRequired)
            {
                try { Invoke((Action)OnPlaybackFinished); } catch { }
            }
            else
            {
                OnPlaybackFinished();
            }
        }

        private void OnPlaybackFinished()
        {
            progressTimer.Stop();
            btnPlayPause.Text = "▶ Play";
            if (waveReader != null && waveReader.Position >= waveReader.Length)
            {
                lblStatus.Text = "Playback finished";
                trackBarProgress.Value = 0;
                waveReader.Position = 0;
                UpdateTimeLabel();
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            if (isUserSeeking || waveReader == null) return;

            try
            {
                long pos = waveReader.Position;
                if (pos < 0) pos = 0;
                if (pos > trackBarProgress.Maximum) pos = trackBarProgress.Maximum;
                trackBarProgress.Value = (int)pos;
                UpdateTimeLabel();
            }
            catch { }
        }

        private void SeekToPosition()
        {
            if (waveReader == null) return;

            try
            {
                long target = trackBarProgress.Value;
                if (target > waveReader.Length) target = waveReader.Length;
                waveReader.Position = target;
                UpdateTimeLabel();
            }
            catch { }
        }

        private void UpdateTimeLabel()
        {
            if (waveReader == null)
            {
                lblTime.Text = "0:00 / 0:00";
                return;
            }

            TimeSpan cur = waveReader.CurrentTime;
            TimeSpan tot = waveReader.TotalTime;
            lblTime.Text = $"{(int)cur.TotalMinutes:D1}:{cur.Seconds:D2} / " +
                           $"{(int)tot.TotalMinutes:D1}:{tot.Seconds:D2}";
        }

        private void CleanupAudio()
        {
            try { progressTimer.Stop(); } catch { }

            try
            {
                if (outputDevice != null)
                {
                    outputDevice.PlaybackStopped -= PlaybackStoppedHandler;
                    outputDevice.Stop();
                    outputDevice.Dispose();
                }
            }
            catch { }
            outputDevice = null;

            try { if (waveReader != null) waveReader.Dispose(); } catch { }
            waveReader = null;

            try { if (wavStream != null) wavStream.Dispose(); } catch { }
            wavStream = null;

            audioReady = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { decodeCts.Cancel(); } catch { }
                try { decodeCts.Dispose(); } catch { }

                CleanupAudio();
                progressTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private class DecodeResult
        {
            public bool Success;
            public bool Canceled;
            public byte[] WavBytes;
            public string ErrorMessage;
        }
    }
}