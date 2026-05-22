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
        private void exportBlob_Click(object sender, EventArgs e)
        {
            // Guard: nothing loaded at all.
            if (loadedContainer == null)
            {
                MessageBox.Show(
                    "No archive is currently loaded. Open an NBL archive or an AFS that " +
                    "contains NBLs first, then use Export Blob to export data blobs.",
                    "Export Blob",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Two supported cases (mirrors Export All Weapons):
            //   1) loadedContainer is itself an NBL  → export its blob directly
            //      into the picked folder (back-compat with original behavior).
            //   2) loadedContainer holds NBLs inside (e.g. an AFS hash file)
            //      → walk each child NBL and write each one's blob into its
            //        own subfolder named after the NBL entry.
            // The "what is a blob?" explanation is part of the prompt below
            // so users who aren't sure what they're about to extract have
            // some context before picking a destination.
            bool isMultiNblContainer = !(loadedContainer is NblLoader);

            string promptMessage = isMultiNblContainer
                ? "Exporting Data Blobs\r\n\r\n" +
                  "What is a 'blob'?  Each NBL stores its files inside one big packed data " +
                  "region. Exporting it gives you the raw, contiguous bytes " +
                  " before the parser slices them into individual entries.\r\n\r\n" +
                  "Each NBL's blob will be saved as <nbl_name>.dat directly in the chosen " +
                  "folder (e.g. ob_000_mh_0.dat). \r\n\r\n" +
                  "Continue?"
                : "Exporting Data Blobs\r\n\r\n" +
                  "What is a 'blob'?  Each NBL stores its files inside one big packed data " +
                  "region. Exporting it gives you the raw, contiguous bytes " +
                  " before the parser slices them into individual entries.\r\n\r\n" +
                  "Each NBL's blob will be saved as <nbl_name>.dat directly in the chosen " +
                  "folder (e.g. ob_000_mh_0.dat). \r\n\r\n" +
                  "Continue?";

            var confirm = MessageBox.Show(promptMessage, "Export Blob",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (confirm != DialogResult.OK)
                return;

            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;
            goodOpenFileDialog.Title = "Choose a destination folder for the data blob";
            if (goodOpenFileDialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            string destFolder = goodOpenFileDialog.FileName;

            if (!isMultiNblContainer)
            {
                // Case 1: single NBL. Original behavior, with a confirmation
                // dialog at the end so the user gets feedback instead of the
                // operation silently completing.
                try
                {
                    ((NblLoader)loadedContainer).exportDataBlob(destFolder);
                    MessageBox.Show(
                        $"Exported data blob to:\r\n\r\n{destFolder}",
                        "Export Blob Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export blob:\r\n\r\n{ex.Message}",
                        "Export Blob Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            // Case 2: walk each child entry; for each that parses as an NBL,
            // export its blob and rename the resulting file to <nbl_name>.dat
            // directly in the chosen destination folder. The underlying
            // exportDataBlob writes "extracted.dat" into the folder it's given,
            // so we route it through a per-NBL temp subfolder and then move
            // the file up with the right name. The temp folder is removed
            // afterward so the user sees just the .dat files, not folders.
            int blobsExported = 0;
            var errors = new List<string>();
            // Track names we've already produced so two NBLs that happen to
            // share a base name (extremely rare in practice) don't clobber
            // each other. The second one becomes name_1.dat, third name_2.dat.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try { Directory.CreateDirectory(destFolder); } catch { /* picked via dialog, should exist */ }

            var childNames = loadedContainer.getFilenames();
            for (int i = 0; i < childNames.Count; i++)
            {
                string childName = childNames[i];
                NblLoader childNbl = null;
                try
                {
                    childNbl = loadedContainer.getFileParsed(i) as NblLoader;
                }
                catch (Exception ex)
                {
                    errors.Add($"{childName}: failed to parse ({ex.Message})");
                    continue;
                }
                if (childNbl == null)
                    continue; // not an NBL, skip silently

                // Build the final filename: <nbl_name>.dat, sanitized for the
                // file system. Strip the .nbl extension off the child name if
                // present so we don't end up with "ob_000_mh_0.nbl.dat".
                string baseName = Path.GetFileNameWithoutExtension(childName);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = $"nbl_{i}";
                var invalid = Path.GetInvalidFileNameChars();
                var sanitized = baseName.ToCharArray();
                for (int c = 0; c < sanitized.Length; c++)
                {
                    if (Array.IndexOf(invalid, sanitized[c]) >= 0) sanitized[c] = '_';
                }
                string finalBaseName = new string(sanitized);

                // Collision guard: if a previous NBL already produced this
                // exact name, suffix with _1, _2, etc. so neither blob is lost.
                string uniqueBaseName = finalBaseName;
                int suffix = 1;
                while (!usedNames.Add(uniqueBaseName))
                {
                    uniqueBaseName = finalBaseName + "_" + suffix;
                    suffix++;
                }
                string finalPath = Path.Combine(destFolder, uniqueBaseName + ".dat");

                // Temp folder for this NBL's export. exportDataBlob writes
                // "extracted.dat" into the folder it's given, so each NBL
                // needs its own scratch directory. Stash it under destFolder
                // (rather than %TEMP%) so the work stays on the same volume —
                // makes the final File.Move a rename rather than a copy.
                string tempFolder = Path.Combine(destFolder, "__psuae_blob_tmp_" + i);

                try
                {
                    if (Directory.Exists(tempFolder))
                        Directory.Delete(tempFolder, recursive: true);
                    Directory.CreateDirectory(tempFolder);

                    childNbl.exportDataBlob(tempFolder);

                    // Find what got written. Expecting exactly one file
                    // ("extracted.dat") but be defensive: if there's more than
                    // one, that's a contract change worth surfacing.
                    var produced = Directory.GetFiles(tempFolder);
                    if (produced.Length == 0)
                    {
                        errors.Add($"{childName}: exportDataBlob produced no file");
                        continue;
                    }
                    if (produced.Length > 1)
                    {
                        // Multiple files unexpectedly  log and move the
                        // first one anyway so the user at least gets something.
                        errors.Add($"{childName}: exportDataBlob produced {produced.Length} files, " +
                                   $"using '{Path.GetFileName(produced[0])}'");
                    }

                    // Overwrite any existing file at the destination path —
                    // File.Move(string, string, bool) is the .NET Core/5+ form;
                    // for broad framework compat we delete-then-move.
                    if (File.Exists(finalPath))
                    {
                        try { File.Delete(finalPath); }
                        catch (Exception ex)
                        {
                            errors.Add($"{childName}: couldn't overwrite existing '{Path.GetFileName(finalPath)}' ({ex.Message})");
                            continue;
                        }
                    }
                    File.Move(produced[0], finalPath);
                    blobsExported++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{childName}: blob export failed ({ex.Message})");
                }
                finally
                {
                    // Best-effort cleanup of the temp folder. If this fails
                    // (file lock, AV scan, whatever), the blob has already
                    // been moved out, so leaving the empty folder behind is
                    // harmless — don't escalate it to a user-visible error.
                    try
                    {
                        if (Directory.Exists(tempFolder))
                            Directory.Delete(tempFolder, recursive: true);
                    }
                    catch { /* intentionally ignored */ }
                }
            }

            // Outcome summary.
            if (blobsExported == 0 && errors.Count == 0)
            {
                MessageBox.Show(
                    "No NBL archives were found inside this container, so no blobs were exported.\r\n\r\n" +
                    "Export Blob walks every child entry and exports the data blob of each one " +
                    "that's an NBL. The currently loaded container doesn't contain any NBLs.",
                    "Export Blob — No NBLs Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (blobsExported == 0)
            {
                var sb = new StringBuilder();
                sb.Append("No data blobs were exported successfully.\r\n\r\n");
                sb.Append($"{errors.Count} problem{(errors.Count == 1 ? "" : "s")} encountered:\r\n  ");
                sb.Append(string.Join("\r\n  ", errors.Take(5)));
                if (errors.Count > 5)
                    sb.Append($"\r\n  ... ({errors.Count - 5} more)");
                MessageBox.Show(sb.ToString(),
                    "Export Blob — Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append($"Exported {blobsExported} data blob{(blobsExported == 1 ? "" : "s")} to:\r\n\r\n{destFolder}");
                if (errors.Count > 0)
                {
                    sb.Append($"\r\n\r\n{errors.Count} child{(errors.Count == 1 ? "" : "ren")} couldn't be exported:\r\n  ");
                    sb.Append(string.Join("\r\n  ", errors.Take(5)));
                    if (errors.Count > 5)
                        sb.Append($"\r\n  ... ({errors.Count - 5} more)");
                }
                MessageBox.Show(sb.ToString(),
                    "Export Blob — Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer == null)
            {
                TreeNode node = treeView1.SelectedNode;
                if (node != null &&
                    node.Tag is FileTreeNodeTag tag &&
                    tag.OwnerContainer == null &&
                    tag.FileName != null &&
                    tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tag.FullPath))
                {
                    byte[] bytesToSave = pendingAdxReplacementBytes;
                    if (bytesToSave == null)
                    {
                        try { bytesToSave = File.ReadAllBytes(tag.FullPath); }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Could not read the original file:\n{ex.Message}",
                                "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    string hashName = Path.GetFileNameWithoutExtension(tag.FullPath);
                    saveFileDialog1.FileName = hashName;
                    saveFileDialog1.Filter = "All files (*.*)|*.*";
                    if (saveFileDialog1.ShowDialog() != DialogResult.OK) return;
                    string destPath = saveFileDialog1.FileName;
                    if (destPath.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
                        destPath = destPath.Substring(0, destPath.Length - 4);
                    try { File.WriteAllBytes(destPath, bytesToSave); }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not save file:\n{ex.Message}",
                            "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    MessageBox.Show(
                        $"Saved to:\n{destPath}\n\n" +
                        $"(The .adx extension was stripped so the filename matches the hash.)",
                        "Save Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    pendingAdxReplacementBytes = null;
                    return;
                }
            }

            if (loadedContainer != null)
            {
                saveFileDialog1.FileName = fileDialog.FileName;
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string destPath = saveFileDialog1.FileName;
                    var containerToSave = loadedContainer;

                    // Lock down anything that could mutate the model mid-serialize.
                    // The form itself stays enabled — user can drag the window,
                    // scroll the tree, click around to view things.
                    arbitraryFileContextMenuStrip.Enabled = false;
                    nblChunkContextMenuStrip.Enabled = false;
                    splitContainer1.Panel2.Enabled = false;

                    // Indeterminate progress bar — PRS compression doesn't report progress,
                    // so we use the bouncing/marquee style to show "still working".
                    var prevStyle = actionProgressBar.Style;
                    actionProgressBar.Style = ProgressBarStyle.Marquee;
                    actionProgressBar.MarqueeAnimationSpeed = 30;
                    string prevStatus = progressStatusLabel.Text;
                    progressStatusLabel.Text = "Saving... (compressing archive, please wait)";

                    try
                    {
                        byte[] savedContainer = await Task.Run(() => containerToSave.ToRaw());
                        await Task.Run(() => File.WriteAllBytes(destPath, savedContainer));

                        this.Text = "PSU Archive Explorer " + Path.GetFileName(destPath);
                        fileDialog.FileName = destPath;
                        progressStatusLabel.Text = $"Saved {Path.GetFileName(destPath)} ({savedContainer.Length:N0} bytes)";

                        // Clear the message after 4 seconds, but only if it hasn't been replaced by something else
                        string savedText = progressStatusLabel.Text;
                        _ = Task.Delay(4000).ContinueWith(_ =>
                        {
                            if (progressStatusLabel.Text == savedText)
                                progressStatusLabel.Text = "Progress:";
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                    catch (ScriptValidationException exc)
                    {
                        string joinedErrors = String.Join("\r\n", exc.ScriptValidationErrors.Select(error =>
                            error.LineNumber != -1
                                ? $"{error.FunctionName}, line {error.LineNumber}: {error.Description}"
                                : $"{error.FunctionName}: {error.Description}"));
                        MessageBox.Show($"Could not save archive. \r\nFile \"{exc.FileName}\" failed to validate for the following reasons: \r\n{joinedErrors}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Save failed:\n{ex.Message}",
                            "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        progressStatusLabel.Text = prevStatus;
                    }
                    finally
                    {
                        actionProgressBar.Style = prevStyle;
                        actionProgressBar.MarqueeAnimationSpeed = 0;
                        actionProgressBar.Value = 0;
                        arbitraryFileContextMenuStrip.Enabled = true;
                        nblChunkContextMenuStrip.Enabled = true;
                        splitContainer1.Panel2.Enabled = true;
                    }
                }
            }
        }

        private void exportSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // There are two valid states for Export Selected:
            //   1. An archive is loaded AND a file inside it is selected
            //      (currentRight is set by tree-selection logic).
            //   2. A loose hashed file (e.g. an .adx with the extension stripped)
            //      was opened directly, so loadedContainer is null but the tree
            //      shows that file with FullPath pointing at it on disk.
            // exportSelected() handles both; we just need guards that don't
            // mistake case 2 for "nothing to export".

            bool standaloneFileSelected =
                loadedContainer == null
                && treeView1.SelectedNode?.Tag is FileTreeNodeTag standaloneTag
                && standaloneTag.OwnerContainer == null
                && !string.IsNullOrEmpty(standaloneTag.FullPath);

            // Guard: nothing useful is loaded at all (no archive AND no
            // standalone file open).
            if (loadedContainer == null && !standaloneFileSelected)
            {
                MessageBox.Show(
                    "No archive is currently loaded. Open an archive first, then select " +
                    "a file in the tree to use Export Selected.",
                    "Export Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Guard: archive is loaded but the user hasn't picked a file. The
            // existing exportSelected() relies on currentRight, which is set
            // when the user clicks a tree node; if it's null, the function
            // would silently no-op. Tell the user what they need to do.
            // (The standalone-file path doesn't need currentRight, so we
            // skip this check when that's the case.)
            if (loadedContainer != null && currentRight == null)
            {
                MessageBox.Show(
                    "No file is selected. Click a file in the tree on the left, then try " +
                    "Export Selected again.",
                    "Export Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            exportSelected();
        }

        private void extractFileTreeContextItem_Click(object sender, EventArgs e)
        {
            exportSelected();
        }

        // Tracks the in-progress "Export All" run (if any) so the user can
        // cancel by re-clicking, and so we can prevent two runs at once.
        private CancellationTokenSource exportAllCts;

        private async void exportAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Re-clicking the menu item while a run is in progress cancels it.
            if (exportAllCts != null)
            {
                exportAllCts.Cancel();
                return;
            }

            // Guard: we need an archive loaded with at least one node in the
            // tree, otherwise there's literally nothing to export.
            if (loadedContainer == null || treeView1.Nodes.Count == 0)
            {
                // Special case: a standalone file is open (e.g. a hashed .adx
                // we wrapped in a fake container). Strictly speaking there's
                // no archive, but "extract everything" with one file means
                // exactly the same thing as "extract the selected file" — so
                // just do that instead of asking the user to navigate menus.
                // We accept either "standalone node currently selected" or
                // "tree has exactly one node and it's a standalone file" — the
                // second covers the case where the user clicked Export All
                // without first clicking the (single) tree node.
                bool standaloneFileSelected =
                    loadedContainer == null
                    && treeView1.SelectedNode?.Tag is FileTreeNodeTag selTag
                    && selTag.OwnerContainer == null
                    && !string.IsNullOrEmpty(selTag.FullPath);

                bool singleStandaloneNode =
                    loadedContainer == null
                    && treeView1.Nodes.Count == 1
                    && treeView1.Nodes[0].Tag is FileTreeNodeTag onlyTag
                    && onlyTag.OwnerContainer == null
                    && !string.IsNullOrEmpty(onlyTag.FullPath);

                if (standaloneFileSelected || singleStandaloneNode)
                {
                    // Make sure the standalone node is selected so exportSelected()
                    // sees it via treeView1.SelectedNode.
                    if (!standaloneFileSelected && singleStandaloneNode)
                    {
                        treeView1.SelectedNode = treeView1.Nodes[0];
                    }
                    exportSelected();
                    return;
                }

                MessageBox.Show(
                    "No archive is currently loaded. Open an archive first, then use " +
                    "Export Container to extract its contents.",
                    "Export Container",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;
            goodOpenFileDialog.Title = "Choose a destination folder for the extracted contents";

            if (goodOpenFileDialog.ShowDialog() != CommonFileDialogResult.Ok) return;

            string destinationFolder = goodOpenFileDialog.FileName;

            // Build the export plan ON THE UI THREAD. Walking TreeNodes off the
            // UI thread is undefined behavior — tree controls have thread affinity.
            // This is fast (just walking nodes, no I/O), so doing it sync is fine.
            List<ExportPlanEntry> plan = new List<ExportPlanEntry>();
            try
            {
                Directory.CreateDirectory(destinationFolder);
                foreach (TreeNode node in treeView1.Nodes)
                {
                    BuildExportPlan(node, destinationFolder, plan);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to prepare the export:\n" + ex.Message,
                    "Export All",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (plan.Count == 0)
            {
                MessageBox.Show(
                    "Nothing to export — the loaded archive contains no extractable files.",
                    "Export All",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Set up the run.
            exportAllCts = new CancellationTokenSource();
            CancellationToken token = exportAllCts.Token;

            actionProgressBar.Value = 0;
            actionProgressBar.Maximum = plan.Count;
            progressStatusLabel.Text = $"Exporting: 0/{plan.Count} files. Click Export All again to cancel.";
            progressStatusLabel.Refresh();

            var progress = new Progress<int>(done =>
            {
                actionProgressBar.Value = done;
                progressStatusLabel.Text = $"Exporting: {done}/{plan.Count} files. Click Export All again to cancel.";
            });

            int filesWritten = 0;
            int filesSkipped = 0;
            bool wasCancelled = false;

            try
            {
                var result = await Task.Run(() =>
                {
                    int written = 0;
                    int skipped = 0;
                    int processed = 0;

                    foreach (var entry in plan)
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            // Make sure the destination folder for this entry exists.
                            // Multiple entries may share a folder; CreateDirectory is
                            // a no-op if it already exists, so this is safe to call.
                            string folder = Path.GetDirectoryName(entry.DestinationPath);
                            if (!string.IsNullOrEmpty(folder))
                            {
                                Directory.CreateDirectory(folder);
                            }

                            File.WriteAllBytes(entry.DestinationPath, entry.Bytes);
                            written++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Export failed for {entry.DestinationPath}: {ex.Message}");
                            skipped++;
                        }

                        processed++;
                        ((IProgress<int>)progress).Report(processed);
                    }

                    return new { Written = written, Skipped = skipped };
                }, token);

                filesWritten = result.Written;
                filesSkipped = result.Skipped;
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }
            finally
            {
                exportAllCts.Dispose();
                exportAllCts = null;

                actionProgressBar.Value = 0;
                progressStatusLabel.Text = "";
                progressStatusLabel.Refresh();
            }

            if (wasCancelled)
            {
                MessageBox.Show(
                    "Export cancelled. Files written before the cancel are still in:\n\n" + destinationFolder,
                    "Export All",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string summary = filesSkipped == 0
                ? $"Exported {filesWritten} file(s) to:\n\n{destinationFolder}"
                : $"Exported {filesWritten} file(s) to:\n\n{destinationFolder}\n\n" +
                  $"({filesSkipped} file(s) could not be written — see console for details.)";

            MessageBox.Show(
                summary,
                "Export All",
                MessageBoxButtons.OK,
                filesSkipped == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        /// <summary>
        /// One file's worth of work in an Export All run: where to write it
        /// and what bytes to write. Built up-front on the UI thread so the
        /// worker thread never has to touch TreeNodes.
        /// </summary>
        private class ExportPlanEntry
        {
            public string DestinationPath;
            public byte[] Bytes;
        }

        /// <summary>
        /// Walks a tree node and appends ExportPlanEntry items to <paramref name="plan"/>
        /// for everything that should be written. This mirrors the logic in
        /// exportNode but materializes the bytes (via PsuFile.ToRawFile) rather
        /// than writing them, so the actual disk I/O can happen on a worker thread.
        /// MUST be called on the UI thread because it walks TreeNodes.
        /// </summary>
        private void BuildExportPlan(TreeNode node, string fileDirectory, List<ExportPlanEntry> plan)
        {
            string originalFilename = node.Text;

            // Standalone hashed-ADX nodes use a FileTreeNodeTag with FullPath
            // pointing at a real file on disk. We currently don't include
            // those in Export All since the existing exportNode skips them
            // when there's no OwnerContainer; preserving that behavior here.
            if (node.Tag is FileTreeNodeTag standaloneTag && standaloneTag.OwnerContainer == null)
            {
                return;
            }

            ContainerFile parent = (node.Tag is FileTreeNodeTag fttag) ? fttag.OwnerContainer : null;
            if (parent == null)
            {
                // Top-level container nodes (the chunks) have no parent
                // container themselves, but their child nodes do. Recurse.
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    BuildExportPlan(nodeChild, fileDirectory, plan);
                }
                return;
            }

            int fileIndex = node.Index;
            List<string> parentFilenames = parent.getFilenames();
            PsuFile file = parent.getFileParsed(fileIndex);

            // NBLs only have "NML(B/L)" or "TML(B/L)" chunks as children, so
            // we don't write anything at THIS level for them — we just recurse.
            if (!(parent is NblLoader))
            {
                if (file is ITextureFile texFile && batchPngExport)
                {
                    // Texture-as-PNG. Save to a MemoryStream so we can hand
                    // raw bytes to the worker thread.
                    string filename = Path.Combine(fileDirectory, Path.GetFileName(originalFilename + ".png"));
                    using (var ms = new MemoryStream())
                    {
                        texFile.mipMaps[0].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        plan.Add(new ExportPlanEntry { DestinationPath = filename, Bytes = ms.ToArray() });
                    }
                }
                else if (batchWavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile adxFile)
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);
                    try
                    {
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxFile.theData);
                        plan.Add(new ExportPlanEntry { DestinationPath = wavPath, Bytes = wavBytes });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, uniqueName);
                        AddRawFileToPlan(file, adxPath, plan);
                    }
                }
                else if (batchDat2WavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile datUnpointed
                         && DatConverter.IsSoundDat(datUnpointed.theData))
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);
                    try
                    {
                        byte[] wavBytes = DatConverter.DecodeToWav(datUnpointed.theData);
                        plan.Add(new ExportPlanEntry { DestinationPath = wavPath, Bytes = wavBytes });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, uniqueName);
                        AddRawFileToPlan(file, datPath, plan);
                    }
                }
                else
                {
                    if (batchExportSubArchiveFiles || !(file is ContainerFile))
                    {
                        string filename = Path.Combine(fileDirectory, getUniqueFilename(originalFilename, fileIndex, parentFilenames));
                        AddRawFileToPlan(file, filename, plan);
                    }
                }
            }

            if (file is ContainerFile)
            {
                string newFolder = Path.Combine(fileDirectory, getUniqueFilename(originalFilename, fileIndex, parentFilenames) + "_ext");
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    BuildExportPlan(nodeChild, newFolder, plan);
                }
            }
            else
            {
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    BuildExportPlan(nodeChild, fileDirectory, plan);
                }
            }
        }

        /// <summary>
        /// Pulls bytes out of a PsuFile via its RawFile representation and
        /// queues them for writing. Mirrors what extractFile() does, but
        /// without doing the I/O up-front.
        /// </summary>
        private void AddRawFileToPlan(PsuFile psuFile, string destinationPath, List<ExportPlanEntry> plan)
        {
            try
            {
                RawFile raw = psuFile.ToRawFile(0);
                byte[] bytes = raw.WriteToBytes(exportMetaData);
                plan.Add(new ExportPlanEntry { DestinationPath = destinationPath, Bytes = bytes });
            }
            catch (Exception ex)
            {
                // If we can't even get the bytes, we can't queue the file.
                // Log it and skip — the run as a whole will continue.
                Console.WriteLine($"Could not prepare {destinationPath} for export: {ex.Message}");
            }
        }

        private void exportAll(TreeNodeCollection treeNodes, string folderName)
        {
            Directory.CreateDirectory(folderName);
            foreach (TreeNode node in treeNodes)
            {
                exportNode(node, folderName);
            }
        }

        private void exportSelected()
        {
            SaveFileDialog exportFileDialog = new SaveFileDialog();

            if (currentRight != null)
            {
                string suggestedName = currentRight.filename;

                if (treeView1.SelectedNode?.Tag is FileTreeNodeTag tag && !string.IsNullOrEmpty(tag.FileName))
                {
                    suggestedName = tag.FileName;
                }

                exportFileDialog.FileName = suggestedName;

                // Special handling for ADX files (works for both standalone hashed ADX and normal .adx)
                bool isAdxFile = suggestedName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                // Special handling for DAT sound files (xobxDDNS / xobxKPTD)
                bool isDatFile = suggestedName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
                // Special handling for Sofdec video files
                bool isSfdFile = suggestedName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);

                if (currentRight is ITextureFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".xvr", ".png");
                    exportFileDialog.Filter = "Portable Network Graphics (*.png)|*.png|Xbox PowerVR Texture (*.xvr)|*.xvr";
                }
                else if (currentRight is TextFile)
                {
                    exportFileDialog.FileName = exportFileDialog.FileName.Replace(".bin", ".txt");
                    exportFileDialog.Filter = "Text (*.txt)|*.txt|Binary File (*.bin)|*.bin";
                }
                else if (isAdxFile)
                {
                    if (batchWavExport)
                    {
                        // Default to .wav when the ADX to WAV setting is on. User can still
                        // switch back to .adx via the filter dropdown.
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                        exportFileDialog.Filter = "ADX Audio (*.adx)|*.adx|All Files (*.*)|*.*";
                    }
                }
                else if (isDatFile)
                {
                    // Only suggest .wav if the in memory bytes look like a real sound DAT.
                    // Non sound .dat files fall through to raw extraction.
                    bool datIsSound = currentRight is UnpointeredFile unpointedCheck
                                      && DatConverter.IsSoundDat(unpointedCheck.theData);

                    if (batchDat2WavExport && datIsSound)
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".wav");
                        exportFileDialog.Filter = "WAV Audio (*.wav)|*.wav|DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                    else
                    {
                        exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                        exportFileDialog.Filter = "DAT File (*.dat)|*.dat|All Files (*.*)|*.*";
                    }
                }
                else if (isSfdFile)
                {
                    // Default to MP4 (filter index 1). The user can switch to
                    // MKV (lossless MPEG-1 passthrough) or raw SFD via the dropdown.
                    exportFileDialog.FileName = Path.ChangeExtension(exportFileDialog.FileName, ".sfd");
                    exportFileDialog.Filter = "SFD File (*.sfd)|*.sfd|MP4 Video (*.mp4)|*.mp4|MKV Video (*.mkv)|*.mkv";
                    exportFileDialog.FilterIndex = 1;
                }

                if (exportFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (currentRight is ITextureFile &&
                        Path.GetExtension(exportFileDialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        ((ITextureFile)currentRight).mipMaps[0].Save(exportFileDialog.FileName);
                    }
                    else if (currentRight is TextFile &&
                             Path.GetExtension(exportFileDialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ((TextFile)currentRight).saveToTextFile(exportFileDialog.OpenFile());
                    }
                    else if (isAdxFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointed)
                        {
                            try
                            {
                                // theData holds the raw ADX bytes exactly as loaded no
                                // metadata wrapper, which is what AdxDecoder expects.
                                byte[] adxBytes = unpointed.theData;
                                byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // AdxDecoder rejects any non-PSU-standard ADX variant.
                                // Offer the user a raw .adx fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"ADX → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .adx file instead?",
                                    "ADX Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string adxPath = Path.ChangeExtension(exportFileDialog.FileName, ".adx");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, adxPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // User picked .adx or any non .wav) from the filter dropdown,
                            // or currentRight isn't an UnpointeredFile for some reason
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else if (isDatFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName);
                        bool saveAsWav = chosenExt.Equals(".wav", StringComparison.OrdinalIgnoreCase);

                        if (saveAsWav && currentRight is UnpointeredFile unpointedDat)
                        {
                            try
                            {
                                byte[] wavBytes = DatConverter.DecodeToWav(unpointedDat.theData);
                                File.WriteAllBytes(exportFileDialog.FileName, wavBytes);
                            }
                            catch (Exception ex)
                            {
                                // Either a non-sound .dat or a corrupt sound .dat offer
                                // the user a raw .dat fallback rather than silently failing.
                                DialogResult fallback = MessageBox.Show(
                                    $"DAT → WAV conversion failed:\n\n{ex.Message}\n\n" +
                                    "Would you like to save the raw .dat file instead?",
                                    "DAT Decode Failed",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                                if (fallback == DialogResult.Yes)
                                {
                                    string datPath = Path.ChangeExtension(exportFileDialog.FileName, ".dat");
                                    bool originalExportMetaData = exportMetaData;
                                    exportMetaData = false;
                                    extractFile(currentRight, datPath);
                                    exportMetaData = originalExportMetaData;
                                }
                            }
                        }
                        else
                        {
                            // If user picked .dat or any non .wav from the filter dropdown
                            // write raw via the existing extract path.
                            bool originalExportMetaData = exportMetaData;
                            exportMetaData = false;
                            extractFile(currentRight, exportFileDialog.FileName);
                            exportMetaData = originalExportMetaData;
                        }
                    }
                    else if (isSfdFile)
                    {
                        string chosenExt = Path.GetExtension(exportFileDialog.FileName).ToLowerInvariant();
                        bool saveAsVideo = chosenExt == ".mp4" || chosenExt == ".mkv";

                        if (saveAsVideo && currentRight is UnpointeredFile sfdUnpointed)
                        {
                            string outputPath = exportFileDialog.FileName;
                            progressStatusLabel.Text = $"Exporting SFD to {chosenExt.ToUpperInvariant()}...";
                            menuStrip1.Enabled = false;

                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    SfdExporter.ExportToVideo(sfdUnpointed.theData, outputPath);
                                    this.Invoke((Action)(() =>
                                    {
                                        SetStatusTemporary($"Exported {Path.GetFileName(outputPath)}");
                                        menuStrip1.Enabled = true;
                                        MessageBox.Show(
                                            $"Exported to:\n{outputPath}",
                                            "SFD Export Complete",
                                            MessageBoxButtons.OK,
                                            MessageBoxIcon.Information);
                                    }));
                                }
                                catch (Exception ex)
                                {
                                    this.Invoke((Action)(() =>
                                    {
                                        SetStatusTemporary("SFD export failed.");
                                        menuStrip1.Enabled = true;

                                        DialogResult fallback = MessageBox.Show(
                                            $"SFD → {chosenExt.ToUpperInvariant()} export failed:\n\n{ex.Message}\n\n" +
                                            "Would you like to save the raw .sfd file instead?",
                                            "SFD Export Failed",
                                            MessageBoxButtons.YesNo,
                                            MessageBoxIcon.Warning);

                                        if (fallback == DialogResult.Yes)
                                        {
                                            string sfdPath = Path.ChangeExtension(outputPath, ".sfd");
                                            bool originalExportMetaData = exportMetaData;
                                            exportMetaData = false;
                                            extractFile(currentRight, sfdPath);
                                            exportMetaData = originalExportMetaData;
                                        }
                                    }));
                                }
                            });
                            // Return immediately — async export is in flight.
                            return;
                        }
                        else
                        {
                            // User picked .sfd from the filter dropdown — raw extract.
                            extractFile(currentRight, exportFileDialog.FileName);
                        }
                    }
                    else
                    {
                        extractFile(currentRight, exportFileDialog.FileName);
                    }
                }
            }
        }

        private void exportNode(TreeNode node, string fileDirectory)
        {
            if (!(node.Tag is FileTreeNodeTag tag))
                return;

            string originalFilename = tag.FileName;

            // ---- Standalone file case (fake container: OwnerContainer is null,
            // bytes live on disk at tag.FullPath). Used for single-file opens like
            // a raw .adx. There's nothing to recurse into, so we handle it here
            // and return.
            if (tag.OwnerContainer == null)
            {
                if (string.IsNullOrEmpty(tag.FullPath) || !File.Exists(tag.FullPath))
                    return;

                bool isAdx = originalFilename != null &&
                             originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDat = originalFilename != null &&
                             originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                if (isAdx && batchWavExport)
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] adxBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, adxPath, overwrite: true);
                    }
                }
                else if (isDat && batchDat2WavExport && DatConverter.IsSoundDat(tag.FullPath))
                {
                    string wavName = Path.ChangeExtension(originalFilename, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] datBytes = File.ReadAllBytes(tag.FullPath);
                        byte[] wavBytes = DatConverter.DecodeToWav(datBytes);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, originalFilename);
                        File.Copy(tag.FullPath, datPath, overwrite: true);
                    }
                }
                else
                {
                    // Not an audio file (or conversion setting off, or non-sound .dat)
                    // just copy the file through.
                    string destPath = Path.Combine(fileDirectory, originalFilename);
                    try { File.Copy(tag.FullPath, destPath, overwrite: true); }
                    catch (Exception ex) { Console.WriteLine($"Copy failed for {originalFilename}: {ex.Message}"); }
                }

                return;
            }

            // ---- Normal container case ----
            ContainerFile parent = tag.OwnerContainer;
            int fileIndex = node.Index;
            List<string> parentFilenames = parent.getFilenames();
            PsuFile file = parent.getFileParsed(fileIndex);

            //NBLs only have "NML(B/L)" or "TML(B/L)" chunks as children.
            if (!(parent is NblLoader))
            {
                if (file is ITextureFile && batchPngExport)
                {
                    string filename = Path.Combine(fileDirectory, Path.GetFileName(originalFilename + ".png"));
                    ((ITextureFile)file).mipMaps[0].Save(filename);
                }
                else if (batchWavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".adx", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile adxFile)
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = AdxDecoder.DecodeToWav(adxFile.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"ADX->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .adx instead.");
                        string adxPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, adxPath);
                    }
                }
                else if (batchDat2WavExport
                         && originalFilename != null
                         && originalFilename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                         && file is UnpointeredFile datUnpointed
                         && DatConverter.IsSoundDat(datUnpointed.theData))
                {
                    string uniqueName = getUniqueFilename(originalFilename, fileIndex, parentFilenames);
                    string wavName = Path.ChangeExtension(uniqueName, ".wav");
                    string wavPath = Path.Combine(fileDirectory, wavName);

                    try
                    {
                        byte[] wavBytes = DatConverter.DecodeToWav(datUnpointed.theData);
                        File.WriteAllBytes(wavPath, wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DAT->WAV conversion failed for {originalFilename}: {ex.Message}. " +
                            "Writing raw .dat instead.");
                        string datPath = Path.Combine(fileDirectory, uniqueName);
                        extractFile(file, datPath);
                    }
                }
                else
                {
                    if (batchExportSubArchiveFiles || !(file is ContainerFile))
                    {
                        string filename = Path.Combine(fileDirectory, getUniqueFilename(originalFilename, fileIndex, parentFilenames));
                        extractFile(file, filename);
                    }
                }
            }

            if (file is ContainerFile)
            {
                string newFolder = fileDirectory + @"\" + getUniqueFilename(originalFilename, fileIndex, parentFilenames) + "_ext";
                exportAll(node.Nodes, newFolder);
            }
            else
            {
                foreach (TreeNode nodeChild in node.Nodes)
                {
                    exportNode(nodeChild, fileDirectory);
                }
            }
        }

        private string getUniqueFilename(string originalFilename, int fileIndex, List<string> parentFilenames)
        {
            string usedFilename;
            if (parentFilenames.Count(filename => filename == originalFilename) > 1)
            {
                usedFilename = Path.GetFileName(originalFilename) + "_" + (fileIndex - parentFilenames.FindIndex(name => name == originalFilename)) + Path.GetExtension(originalFilename);
            }
            else
            {
                usedFilename = originalFilename;
            }
            return usedFilename;
        }

        private void extractFile(PsuFile psuFile, string filename)
        {
            RawFile file = psuFile.ToRawFile(0);
            byte[] bytes = file.WriteToBytes(exportMetaData);
            try
            {
                File.WriteAllBytes(filename, bytes);
            }
            catch
            {
                MessageBox.Show("Unable to extract " + filename + ". The file may be in use or otherwise inaccessible. Skipping.");
            }
        }

        private void extractAllInFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            string rootFolder = goodOpenFileDialog.FileName;

            string[] fileNames = batchRecursive
                ? Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                : Directory.GetFiles(rootFolder);

            actionProgressBar.Value = 0;
            actionProgressBar.Maximum = fileNames.Length;
            progressStatusLabel.Text = $"Progress: 0/{fileNames.Length} Files. Please wait, this can take time.";

            menuStrip1.Enabled = false;

            var worker = new System.ComponentModel.BackgroundWorker
            {
                WorkerReportsProgress = true
            };

            worker.DoWork += (s, args) =>
            {
                var files = (string[])args.Argument;
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = files[i];
                    string newFolder = Path.GetDirectoryName(fileName);

                    try
                    {
                        extractPSUArchive(fileName, newFolder);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to extract {fileName}: {ex.Message}");
                    }

                    worker.ReportProgress(i + 1, fileName);
                }
            };

            worker.ProgressChanged += (s, args) =>
            {
                actionProgressBar.Value = args.ProgressPercentage;
                progressStatusLabel.Text =
                    $"Progress: {args.ProgressPercentage}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                actionProgressBar.Value = 0;
                progressStatusLabel.Text = args.Error != null
                    ? "Progress: Failed — " + args.Error.Message
                    : "Progress: Done!";
                menuStrip1.Enabled = true;
                worker.Dispose();
            };

            worker.RunWorkerAsync(fileNames);
        }

        // ----------------------------------------------------------------
        // SFD Import
        // ----------------------------------------------------------------

        /// <summary>
        /// Called from the right-click context menu "Import video as SFD..."
        /// item, which is only shown when the selected file is an .sfd.
        /// The user picks an MP4 or MKV, chooses an output path, and
        /// SfdImporter handles the rest on a background thread.
        /// </summary>
        private void ImportVideoAsSfd()
        {
            // We need the original SFD bytes to extract resolution / audio params.
            byte[] originalSfdBytes = null;
            if (currentRight is UnpointeredFile sfdFile)
                originalSfdBytes = sfdFile.theData;

            if (originalSfdBytes == null || originalSfdBytes.Length == 0)
            {
                MessageBox.Show(
                    "Could not read the original SFD data.\n\n" +
                    "Make sure an SFD file is selected in the tree before importing.",
                    "SFD Import",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Pick the source video file.
            string inputPath;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select video to import as SFD";
                dlg.Filter = "Video Files (*.mp4;*.mkv)|*.mp4;*.mkv|All Files (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                inputPath = dlg.FileName;
            }

            // Pick the output SFD path.
            string outputPath;
            string suggestedName = currentRight?.filename ?? "output.sfd";
            if (!suggestedName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase))
                suggestedName = Path.ChangeExtension(suggestedName, ".sfd");

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save imported SFD as...";
                dlg.Filter = "SFD File (*.sfd)|*.sfd";
                dlg.FileName = suggestedName;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                outputPath = dlg.FileName;
            }

            // Capture locals for the lambda.
            byte[] sfdSnapshot = originalSfdBytes;
            progressStatusLabel.Text = "Importing video as SFD...";
            menuStrip1.Enabled = false;

            _ = Task.Run(() =>
            {
                try
                {
                    SfdImporter.ImportToSfd(inputPath, sfdSnapshot, outputPath);
                    this.Invoke((Action)(() =>
                    {
                        SetStatusTemporary("SFD import complete.");
                        menuStrip1.Enabled = true;
                        MessageBox.Show(
                            $"Saved to:\n{outputPath}",
                            "SFD Import Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() =>
                    {
                        SetStatusTemporary("SFD import failed.");
                        menuStrip1.Enabled = true;
                        MessageBox.Show(
                            $"SFD import failed:\n\n{ex.Message}",
                            "SFD Import Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }));
                }
            });
        }

        // ----------------------------------------------------------------
        // Status label helper
        // ----------------------------------------------------------------

        /// <summary>
        /// Sets the progress status label text and automatically clears it
        /// after <paramref name="delayMs"/> milliseconds (default 4 seconds).
        /// Safe to call from any thread.
        /// </summary>
        private void SetStatusTemporary(string message, int delayMs = 4000)
        {
            // Set immediately on the UI thread.
            if (progressStatusLabel.InvokeRequired)
                progressStatusLabel.Invoke((Action)(() => progressStatusLabel.Text = message));
            else
                progressStatusLabel.Text = message;

            // Clear after the delay. The label is only cleared if it still
            // holds the same message we set, so a subsequent status update
            // won't be wiped out early.
            string snapshot = message;
            Task.Delay(delayMs).ContinueWith(_ =>
            {
                if (progressStatusLabel.IsDisposed) return;
                progressStatusLabel.Invoke((Action)(() =>
                {
                    if (progressStatusLabel.Text == snapshot)
                        progressStatusLabel.Text = string.Empty;
                }));
            }, TaskScheduler.Default);
        }
    }
}