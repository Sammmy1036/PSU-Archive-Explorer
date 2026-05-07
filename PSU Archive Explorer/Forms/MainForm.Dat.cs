using Microsoft.WindowsAPICodePack.Dialogs;
using psu_archive_explorer.FileViewers;
using psu_archive_explorer.Forms;
using psu_archive_explorer.Forms.FileViewers;
using psu_archive_explorer.Forms.FileViewers.Enemies;
using psu_archive_explorer;
using PSULib;
using PSULib.FileClasses.Archives;
using PSULib.FileClasses.Bosses;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Enemies;
using PSULib.FileClasses.General;
using PSULib.FileClasses.General.Scripts;
using PSULib.FileClasses.Items;
using PSULib.FileClasses.Maps;
using PSULib.FileClasses.Missions;
using PSULib.FileClasses.Models;
using PSULib.FileClasses.Textures;
using PSULib.Support;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainForm : Form
    {
        // ====================== Display DAT Sound Preview in right panel ======================
        private void LoadDatSoundIntoRightPanel(string filePath, TreeNode node)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                string filename = Path.GetFileName(filePath);

                if (currentFileHexForm != null && !currentFileHexForm.IsDisposed)
                    currentFileHexForm.Close();

                // ClearRightPanel disposes the previous preview panel (if any),
                // which in turn stops playback and releases NAudio resources.
                ClearRightPanel();

                string infoText =
                    "DAT sound file detected.\n\n" +
                    "This is a raw PCM sound container used by PSU.\n" +
                    "You can preview playback below, or use Extract Selected\n" +
                    "to save it as either the raw .dat or a converted .wav.\n\n" +
                    $"File name: {filename}";

                var previewPanel = new DatPreviewPanel(filePath, infoText);
                splitContainer1.Panel2.Controls.Add(previewPanel);

                byte[] header = new byte[4];
                if (data.Length >= 4)
                {
                    Array.Copy(data, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, data, header);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load DAT file:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentRight = null;
            }
        }

        /// <summary>
        /// Loads an archive-embedded .dat file without blocking the UI thread. Shows
        /// a DatPreviewPanel in its "Decoding..." state immediately, then extracts
        /// and decodes the file on a background thread. If the .dat turns out to be
        /// non-sound, falls back to the normal parsed-file viewer path.
        /// </summary>
        private void LoadArchiveDatAsync(ContainerFile parent, int index, string fileName)
        {
            // Capture the selected TreeNode and container so we can detect stale
            // completions (user clicked away before we finished).
            TreeNode nodeAtStart = treeView1.SelectedNode;

            // Show a placeholder panel immediately in "Decoding..." state.
            string infoText =
                "DAT file detected. Checking for sound data...\n\n" +
                $"File name: {fileName}";

            var panel = new DatPreviewPanel(infoText, externalProvider: true);
            splitContainer1.Panel2.Controls.Add(panel);

            // If the panel gets disposed (e.g. user clicks another node), its
            // cancellation token flips. We check it before touching UI.
            CancellationToken panelCt = panel.DecodeCancellationToken;

            Task.Run(() =>
            {
                try
                {
                    // Step 1: extract/parse (the slow part for archive DATs)
                    PsuFile parsed = parent.getFileParsed(index);
                    panelCt.ThrowIfCancellationRequested();

                    byte[] raw = null;
                    if (parsed is UnpointeredFile unpointed && unpointed.theData != null)
                    {
                        raw = unpointed.theData;
                    }
                    else
                    {
                        RawFile rf = parent.getFileRaw(index);
                        raw = rf?.fileContents ?? rf?.WriteToBytes(false);
                    }

                    panelCt.ThrowIfCancellationRequested();

                    // Step 2: quick 8KB signature scan
                    if (raw == null || !DatConverter.IsSoundDat(raw))
                    {
                        return new ArchiveDatResult { IsSound = false, Parsed = parsed };
                    }

                    // As soon as we know it's a sound DAT, update the hint text so the
                    // user isn't staring at "Checking for sound data..." while the decode
                    // finishes. SetInfoText marshals to the UI thread internally.
                    string soundInfo =
                        "DAT sound file detected.\n\n" +
                        "This is a raw PCM sound container used by PSU.\n" +
                        "You can preview playback below, or use Extract Selected\n" +
                        "to save it as either the raw .dat or a converted .wav.\n\n" +
                        $"File name: {fileName}";
                    panel.SetInfoText(soundInfo);

                    // Step 3: the actual decode (also slow, but now the user already
                    // sees the correct hint and a "Decoding..." status).
                    panelCt.ThrowIfCancellationRequested();
                    byte[] wav = DatConverter.DecodeToWav(raw);
                    return new ArchiveDatResult
                    {
                        IsSound = true,
                        Parsed = parsed,
                        WavBytes = wav,
                        RawBytes = raw
                    };
                }
                catch (OperationCanceledException)
                {
                    return new ArchiveDatResult { Canceled = true };
                }
                catch (Exception ex)
                {
                    return new ArchiveDatResult { Error = ex.Message };
                }
            }, panelCt).ContinueWith(t =>
            {
                if (panel.IsCancelledOrDisposed) return;
                if (this.IsDisposed) return;

                // Also bail if the user has already navigated to a different node.
                if (!ReferenceEquals(treeView1.SelectedNode, nodeAtStart)) return;

                ArchiveDatResult result = t.Result;
                if (result.Canceled) return;

                try
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (panel.IsCancelledOrDisposed) return;
                        if (!ReferenceEquals(treeView1.SelectedNode, nodeAtStart)) return;

                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            panel.SetDecodeError($"Preview failed: {result.Error}");
                            return;
                        }

                        if (result.IsSound)
                        {
                            currentRight = result.Parsed;
                            panel.SetDecodedWav(result.WavBytes);
                        }
                        else
                        {
                            // Non-sound .dat — swap the placeholder panel out for the
                            // normal viewer path.
                            ClearRightPanel();
                            setRightPanel(result.Parsed);
                        }
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }, TaskScheduler.Default);
        }

        // Small DTO for marshaling background results back to the UI thread.
        private class ArchiveDatResult
        {
            public bool IsSound;
            public bool Canceled;
            public PsuFile Parsed;
            public byte[] RawBytes;
            public byte[] WavBytes;
            public string Error;
        }
    }
}