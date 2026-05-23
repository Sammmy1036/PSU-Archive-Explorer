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
        private void setQuest_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setQuest(importDialog.OpenFile());
                }
            }
        }

        private void setZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).setZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void addZone_Click_1(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (importDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addZone((int)zoneUD.Value, importDialog.OpenFile());
                }
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            ClearRightPanel();

            // When the container search filter is active, nodes are rebuilt with
            // a FilteredNodeTag wrapper that carries the original container index.
            // Unwrap it here so all downstream code sees the plain FileTreeNodeTag,
            // and capture the original index before we lose it.
            int? filteredOriginalIndex = null;
            if (e.Node.Tag is FilteredNodeTag fnt)
            {
                filteredOriginalIndex = fnt.OriginalIndex;
                e.Node.Tag = fnt.OriginalTag;
            }

            if (!(e.Node.Tag is FileTreeNodeTag tag))
                return;

            // Standalone .adx on disk route to ADX preview
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                    LoadAdxIntoRightPanel(pathToUse, e.Node);
                return;
            }

            // Standalone .dat on disk if it's a sound DAT, route to DAT preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse) && DatConverter.IsSoundDat(pathToUse))
                {
                    LoadDatSoundIntoRightPanel(pathToUse, e.Node);
                    return;
                }
            }

            // Standalone .sfd on disk route to SFD preview.
            if (tag.OwnerContainer == null && tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true)
            {
                string pathToUse = tag.FullPath ?? fileDialog.FileName;
                if (File.Exists(pathToUse))
                {
                    long size = 0;
                    try { size = new FileInfo(pathToUse).Length; } catch { }
                    if (size > 0 && size <= MAX_SFD_PREVIEW_SIZE)
                    {
                        LoadSfdIntoRightPanelFromFile(pathToUse, e.Node);
                        return;
                    }
                    // Too big fall through to the large file warning panel below.
                }
            }

            if (tag.OwnerContainer != null)
            {
                ContainerFile parent = tag.OwnerContainer;

                // Use the original container index captured from FilteredNodeTag
                // when the filter is active; otherwise use the node's own Index.
                int index = filteredOriginalIndex ?? e.Node.Index;

                string fileName = tag.FileName ?? "Unknown";

                // Filename-based type detection. The .nbl check is supplemented
                // by a content check further down — AfsLoader and MiniAfsLoader
                // store filenames in 32-byte fixed-width slots, so any source
                // filename longer than 32 chars is truncated on disk and the
                // ".nbl" suffix is the first thing to go. We can't recover the
                // missing characters, but we can ask the parser whether the
                // entry's content is actually an NBL and treat it as one.
                bool isNblFile = fileName.EndsWith(".nbl", StringComparison.OrdinalIgnoreCase);
                bool isSfdVideo = fileName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                bool isAdxFile = fileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool isDatFile = fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                // Content fallback for the NBL case. Both AfsLoader and
                // MiniAfsLoader eagerly parse their children during load
                // (populateFile / the corresponding loop), so getFileParsed
                // here is a property access, not a re-parse — no measurable
                // cost. Guarded with try/catch because exotic entries may not
                // parse cleanly and we don't want to crash the tree select.
                if (!isNblFile)
                {
                    try
                    {
                        if (parent.getFileParsed(index) is NblLoader)
                        {
                            isNblFile = true;
                        }
                    }
                    catch
                    {
                        // Fall through — leave isNblFile false and let the
                        // normal generic-file path handle this entry.
                    }
                }

                if (isDatFile)
                {
                    LoadArchiveDatAsync(parent, index, fileName);
                    return;
                }

                bool shouldSkipSizeCheck = isNblFile ||
                                           fileName.IndexOf("NMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           fileName.IndexOf("TMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           isAdxFile;

                if (shouldSkipSizeCheck)
                {
                    // Selecting a chunk node previously left the right panel
                    // blank, since there's no viewer for the chunk itself.
                    // Show a short explanation of what each chunk is so the
                    // user has some context. Match the wording style of the
                    // ADX info panel.
                    bool isNmllChunk = fileName.IndexOf("NMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isTmllChunk = fileName.IndexOf("TMLL chunk", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isNmllChunk)
                    {
                        ShowChunkInfoPanel(
                            "NMLL chunk",
                            "The NMLL chunk holds the structured game data inside an NBL container.\r\n" +
                            "Things like models, parameter tables, animations, scripts, etc.\r\n" +
                            "Each entry in the chunk is a separately parseable file with its own format.\r\n\r\n" +
                            "Expand this node to browse its contents.");
                        return;
                    }

                    if (isTmllChunk)
                    {
                        ShowChunkInfoPanel(
                            "TMLL chunk",
                            "The TMLL chunk holds texture data for an NBL container.\r\n" +
                            "Things like texture files used by models and UI stored in the NMLL chunk.\r\n" +
                            "Archives without textures will only contain an NMLL.\r\n\r\n" +
                            "Expand this node to browse its contents.");
                        return;
                    }

                    if (isNblFile)
                    {
                        // Title: generic "NBL Archive" label rather than the
                        // file's name, since the body text below is a general
                        // description of what an NBL is. Matches the style of
                        // the NMLL/TMLL chunk info panels above.
                        ShowChunkInfoPanel(
                            "NBL Archive",
                            "NBL Archive (Nu2 Binary Library) is a container holding the structured data.\r\n" +
                            "This data is packed into an NMLL chunk and an optional TMLL chunk.\r\n" +
                            "NBLs are typically nested inside larger AFS containers with many sibling NBLs\r\n\r\n" +
                            "Expand this node to browse its chunks.");
                        return;
                    }

                    setRightPanel(parent.getFileParsed(index));
                    return;
                }

                const long MAX_SAFE_SIZE = 500 * 1024 * 1024; // 500 MB (generic preview cap)

                long fileSize = 0;
                string displayName = fileName;

                try
                {
                    RawFile raw = parent.getFileRaw(index);
                    if (raw != null)
                    {
                        displayName = raw.filename ?? fileName;

                        if (raw.fileContents != null && raw.fileContents.Length > 0)
                        {
                            fileSize = raw.fileContents.LongLength;
                        }
                        else
                        {
                            byte[] data = raw.WriteToBytes(false);
                            fileSize = data?.LongLength ?? 0;
                        }
                    }
                }
                catch
                {
                    fileSize = long.MaxValue;
                }

                // Archive-embedded .sfd route to the in-panel video preview
                // if it's under the SFD ceiling, otherwise drop to warning.
                if (isSfdVideo && fileSize <= MAX_SFD_PREVIEW_SIZE)
                {
                    LoadSfdIntoRightPanel(parent, index, displayName, e.Node);
                    return;
                }

                bool isLargeOrVideo = (fileSize > MAX_SAFE_SIZE) || isSfdVideo;

                if (isLargeOrVideo)
                {
                    try { currentRight = parent.getFileParsed(index); } catch (Exception ex) { Console.WriteLine("getFileParsed failed: " + ex); }

                    var warningPanel = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = Color.FromArgb(229, 229, 229)
                    };

                    var lbl = new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 10.5f),
                        Text = $"Preview unavailable due to file size.\n\n" +
                               "• Right click the file and Export Selected to save it.\n" +
                               "• .sfd videos can be opened with VLC Media Player.\n\n" +
                               $"File name: {displayName}"
                    };

                    warningPanel.Controls.Add(lbl);
                    splitContainer1.Panel2.Controls.Add(warningPanel);
                    return;
                }

                setRightPanel(parent.getFileParsed(index));
            }
        }

        private void addFile_Click(object sender, EventArgs e)
        {
            if (loadedContainer is AfsLoader)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    ((AfsLoader)loadedContainer).addFile(fileDialog.SafeFileName, fileDialog.OpenFile());
                }
            }
        }

        private void createAFSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Step 1: ask for the folder whose contents will be packed into
            // the AFS, using the modern Vista-style folder picker.
            if (!PromptForFolder("Select the folder whose contents will be packed into the AFS", out string folderToOpen))
                return;

            // Step 2: ask where to save the resulting AFS file.
            // SaveFileDialog already renders in the modern style on Vista+
            // (its AutoUpgradeEnabled default is true), so nothing to do here.
            saveFileDialog1.Title = "Save the new AFS file as";
            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            AfsLoader.createFromDirectory(folderToOpen, saveFileDialog1.FileName);
        }

        private void replaceFileTreeContextItem_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            if (node == null || !(node.Tag is FileTreeNodeTag tag))
                return;

            if (tag.OwnerContainer == null &&
                tag.FileName != null &&
                tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(tag.FullPath) &&
                File.Exists(tag.FullPath))
            {
                // Accept either a ready-made .adx or a .wav. A .wav is encoded
                // to ADX in-memory via AdxEncoder, using the file currently
                // being replaced (tag.FullPath) as the parameter template, so
                // channels / sample rate / highpass match the PSU original.
                OpenFileDialog adxReplaceDialog = new OpenFileDialog
                {
                    Filter = "Audio files (*.adx;*.wav)|*.adx;*.wav|" +
                             "ADX audio files (*.adx)|*.adx|" +
                             "WAV audio files (*.wav)|*.wav|" +
                             "All files (*.*)|*.*",
                    Title = "Select an ADX or WAV file to replace the hashed file with"
                };

                if (adxReplaceDialog.ShowDialog() != DialogResult.OK)
                    return;

                string sourcePath = adxReplaceDialog.FileName;
                bool sourceIsWav =
                    sourcePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

                byte[] replacementBytes;

                if (sourceIsWav)
                {
                    // ---- WAV path: encode to ADX using the original as template ----
                    // The encoder copies channels / sample rate / highpass from
                    // the template ADX and resamples the WAV to match, so any
                    // source rate is handled automatically with correct pitch.
                    byte[] wavBytes;
                    byte[] templateAdx;
                    try
                    {
                        wavBytes = File.ReadAllBytes(sourcePath);
                        templateAdx = File.ReadAllBytes(tag.FullPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not read the selected file:\n{ex.Message}",
                            "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    try
                    {
                        replacementBytes = AdxEncoder.EncodeFromWav(wavBytes, templateAdx);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Could not convert the WAV to ADX:\n{ex.Message}\n\n" +
                            "The WAV must be uncompressed 16-bit PCM (mono or stereo).",
                            "WAV Conversion Failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    // ---- ADX path: use the picked file as-is ----
                    if (!IsValidAdxFile(sourcePath))
                    {
                        MessageBox.Show(
                            "The selected file is not a valid ADX audio file.\n\n" +
                            "Please pick a file with a proper ADX header, or " +
                            "select a .wav to have it converted automatically.",
                            "Invalid ADX File",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        replacementBytes = File.ReadAllBytes(sourcePath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not read the selected ADX file:\n{ex.Message}",
                            "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                pendingAdxReplacementBytes = replacementBytes;

                string filename = Path.GetFileName(tag.FullPath);
                byte[] header = new byte[4];
                if (pendingAdxReplacementBytes.Length >= 4)
                {
                    Array.Copy(pendingAdxReplacementBytes, 0, header, 0, 4);
                }
                currentRight = new UnpointeredFile(filename, pendingAdxReplacementBytes, header);

                // Refresh the right panel so the preview reflects the NEW audio.
                // Re-selecting the node would re-run LoadAdxIntoRightPanel, which
                // reads from disk (File.ReadAllBytes) — but the replacement only
                // exists in memory and was never written, so that would just
                // reload the original. Instead, build the preview panel directly
                // from the in-memory bytes via AdxPreviewPanel's byte[] ctor.
                try
                {
                    ClearRightPanel();
                    string previewInfo =
                        "ADX audio file (pending replacement — not yet saved).\n\n" +
                        "Click File → Save As to save to hash.\n\n" +
                        $"File name: {filename}";
                    var pendingPreview = new AdxPreviewPanel(
                        pendingAdxReplacementBytes, previewInfo, filename);
                    splitContainer1.Panel2.Controls.Add(pendingPreview);
                }
                catch (Exception ex)
                {
                    // A preview failure must not lose the pending replacement;
                    // the bytes are already stored and Save As will still work.
                    MessageBox.Show(
                        $"The replacement was loaded, but the preview could not " +
                        $"be shown:\n{ex.Message}",
                        "Preview Unavailable",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                MessageBox.Show(
                    $"'{Path.GetFileName(sourcePath)}' loaded!\n\n" +
                    (sourceIsWav ? "The WAV was converted to ADX in memory.\n\n" : "") +
                    $"Click File → Save As to save to hash.\n\n" +
                    $"(The .adx extension will be stripped and match the hash name.)",
                    "ADX Replacement",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // NOM guard. The generic replace below injects the picked file as
            // raw bytes - correct for most file types, but for a .nom that
            // would drop a GLB (or anything) in verbatim with no conversion,
            // producing a file the game cannot use. Steer the user to the
            // proper importer, but let them proceed if they genuinely want a
            // raw byte replacement.
            if (tag.FileName != null &&
                tag.FileName.EndsWith(".nom", StringComparison.OrdinalIgnoreCase))
            {
                DialogResult nomChoice = MessageBox.Show(
                    "This is a NOM animation file.\n\n" +
                    "To replace it with an edited animation, select the NOM " +
                    "in the tree and use the \"Import Animation from GLB\" " +
                    "button in the preview panel. That converts your GLB into " +
                    "the NOM format the game expects.\n\n" +
                    "The generic Replace will instead inject the file you " +
                    "pick as raw bytes, with no conversion - only continue if " +
                    "you specifically want a raw byte replacement.\n\n" +
                    "Continue with raw replace anyway?",
                    "Replace NOM File",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2); // default to No

                if (nomChoice != DialogResult.Yes)
                    return;
            }

            ContainerFile owningFile = tag.OwnerContainer;
            OpenFileDialog replaceDialog = new OpenFileDialog
            {
                // Surface .wav alongside the usual filter so users can pick a
                // WAV when replacing an in-container .adx; the WAV intercept
                // below handles the conversion. Non-ADX entries still accept
                // any file (the All Files filter), preserving prior behavior.
                Filter = "All files (*.*)|*.*|" +
                         "Audio files (*.adx;*.wav)|*.adx;*.wav|" +
                         "Video files (*.mp4;*.mkv)|*.mp4;*.mkv"
            };
            replaceDialog.FileName = tag.FileName;
            if (replaceDialog.ShowDialog() == DialogResult.OK)
            {
                string pickedFilename = Path.GetFileName(replaceDialog.FileName);

                // ---- WAV-to-ADX intercept for in-container .adx entries ----
                // If the entry being replaced is a .adx AND the user picked a
                // .wav, encode the WAV first using the entry's CURRENT bytes
                // as the template (channels / sample rate / highpass). The
                // resulting RawFile slots into the existing replaceFile path
                // exactly like any other byte-payload replacement.
                bool intercepted = false;
                RawFile file = null;

                bool targetIsAdx = tag.FileName != null &&
                    tag.FileName.EndsWith(".adx", StringComparison.OrdinalIgnoreCase);
                bool pickedIsWav = pickedFilename.EndsWith(".wav",
                    StringComparison.OrdinalIgnoreCase);

                bool targetIsSfd = tag.FileName != null &&
                    tag.FileName.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase);
                bool pickedIsVideo =
                    pickedFilename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                    pickedFilename.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);

                // ---- MP4/MKV-to-SFD intercept for in-container .sfd entries ----
                // If the entry being replaced is an .sfd AND the user picked
                // an MP4 or MKV, convert it to SFD using SfdImporter, with the
                // entry's current bytes as the parameter template (resolution,
                // framerate, audio params). Runs on a background thread since
                // FFmpeg encoding can take several seconds.
                if (targetIsSfd && pickedIsVideo)
                {
                    // Read the SFD bytes to use as the parameter template
                    // (resolution, framerate, audio params).
                    // We always read from the container's raw file so we
                    // get the current in-memory version after any previous
                    // replacements, but fall back to currentRight if that
                    // produces invalid data.
                    byte[] originalSfdBytes = null;
                    try
                    {
                        RawFile rawEntry = owningFile.getFileRaw(node.Index);
                        if (rawEntry != null)
                            originalSfdBytes = rawEntry.fileContents
                                ?? rawEntry.WriteToBytes(false);
                    }
                    catch { }

                    // If the container read failed or returned non-SFD data,
                    // fall back to currentRight which is synced after each import.
                    if ((originalSfdBytes == null || originalSfdBytes.Length < 12
                         || originalSfdBytes[0] != 0x00)
                        && currentRight is UnpointeredFile ufTemplate
                        && ufTemplate.theData?.Length > 12)
                    {
                        originalSfdBytes = ufTemplate.theData;
                    }

                    if (originalSfdBytes == null || originalSfdBytes.Length < 12)
                    {
                        MessageBox.Show(
                            "Could not read a valid SFD from the selected entry.",
                            "Template Read Failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Check if the SFD template has valid audio — if the
                    // ADX payload is missing or corrupt (can happen after
                    // a previous import this session), warn the user.
                    {
                        var checkDemux = new SofdecDemuxer();
                        checkDemux.Parse(originalSfdBytes);
                        byte[] checkAdx = checkDemux.GetAdxPayload();
                        bool adxValid = checkAdx.Length > 4
                            && checkAdx[0] == 0x80 && checkAdx[1] == 0x00;
                        if (!adxValid)
                        {
                            MessageBox.Show(
                                "This SFD has already been replaced once this session.\n\n"
                                + "Please close and re-open the vanilla hash file"
                                + " then try again.",
                                "SFD Conversion Notice",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }




                    string inputVideoPath = replaceDialog.FileName;
                    string tmpSfd = Path.ChangeExtension(Path.GetTempFileName(), ".sfd");

                    progressStatusLabel.Text = "Converting video to SFD...";
                    menuStrip1.Enabled = false;

                    // Capture locals for the lambda.
                    byte[] sfdSnapshot = originalSfdBytes;
                    ContainerFile container = owningFile;
                    int nodeIndex = node.Index;
                    TreeNode treeNode = node;
                    FileTreeNodeTag nodeTag = tag;

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            SfdImporter.ImportToSfd(inputVideoPath, sfdSnapshot, tmpSfd);
                            byte[] sfdBytes = File.ReadAllBytes(tmpSfd);

                            this.Invoke((Action)(() =>
                            {
                                try
                                {
                                    var newFile = new RawFile(
                                        new MemoryStream(sfdBytes), nodeTag.FileName);
                                    newFile.filename = nodeTag.FileName;
                                    container.replaceFile(nodeIndex, newFile);

                                    // Sync the parsed cache so Save As writes
                                    // the new SFD bytes, not the original.
                                    PsuFile parsedAfter = container.getFileParsed(nodeIndex);
                                    if (parsedAfter is UnpointeredFile uf)
                                        uf.theData = sfdBytes;

                                    // Refresh the tree node and right panel.
                                    var selAfterSfd = treeView1.SelectedNode;
                                    treeView1.SelectedNode = null;
                                    treeView1.SelectedNode = selAfterSfd;

                                    SetStatusTemporary("SFD replacement ready.");
                                    menuStrip1.Enabled = true;
                                    MessageBox.Show(
                                        $"Video converted and loaded as SFD.\n\n" +
                                        "Click File \u2192 Save As to write the archive.",
                                        "SFD Replacement Ready",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                                }
                                catch (Exception ex)
                                {
                                    SetStatusTemporary("SFD replacement failed.");
                                    menuStrip1.Enabled = true;
                                    MessageBox.Show(
                                        $"Failed to apply SFD replacement:\n{ex.Message}",
                                        "SFD Replacement Failed",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            this.Invoke((Action)(() =>
                            {
                                SetStatusTemporary("SFD conversion failed.");
                                menuStrip1.Enabled = true;
                                MessageBox.Show(
                                    $"Video \u2192 SFD conversion failed:\n\n{ex.Message}\n\n"
                                    + $"Type: {ex.GetType().Name}\n"
                                    + $"Stack: {ex.StackTrace?.Split('\n')[0]}",
                                    "SFD Conversion Failed",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }));
                        }
                        finally
                        {
                            try { if (File.Exists(tmpSfd)) File.Delete(tmpSfd); } catch { }
                        }
                    });
                    return; // async path — don't fall through to generic replace
                }

                if (targetIsAdx && pickedIsWav)
                {
                    byte[] wavBytes;
                    try
                    {
                        wavBytes = File.ReadAllBytes(replaceDialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not read the selected WAV:\n{ex.Message}",
                            "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Fetch the entry's current bytes from the container to use
                    // as the encoder template. WriteToBytes(false) gives clean
                    // ADX without the export metadata header.
                    byte[] templateAdx;
                    try
                    {
                        RawFile original = owningFile.getFileRaw(node.Index);
                        templateAdx = original.WriteToBytes(false);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Could not read the original ADX entry as a template:\n{ex.Message}",
                            "Template Read Failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    byte[] encoded;
                    try
                    {
                        encoded = AdxEncoder.EncodeFromWav(wavBytes, templateAdx);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Could not convert the WAV to ADX:\n{ex.Message}\n\n" +
                            "The WAV must be uncompressed 16-bit PCM (mono or stereo).",
                            "WAV Conversion Failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Keep the entry's existing filename — the container's
                    // index/lookup is keyed off it, and the encoded bytes are
                    // a drop-in replacement so there's no reason to rename.
                    file = new RawFile(new MemoryStream(encoded), tag.FileName);
                    file.filename = tag.FileName;
                    intercepted = true;
                }

                if (!intercepted)
                {
                    file = new RawFile(replaceDialog.OpenFile(), pickedFilename);
                }

                // The filename-fixup and rename-validation below exist for the
                // generic raw-byte replace path (where RawFile's ctor may
                // override filename from an embedded metadata header, and the
                // user's picked filename may need validation against the
                // container's rules). The WAV intercept already chose the
                // correct filename (the existing entry's name) and produced
                // clean bytes with no embedded metadata, so neither check
                // applies — skip them to avoid renaming a valid .adx entry
                // after a successful encode.
                if (!intercepted)
                {
                    // RawFile's constructor parses the stream and may pull a
                    // filename out of an embedded metadata header (the same header
                    // we WRITE during extraction when exportMetaData is true).
                    // That means: if the user is feeding back a file that was
                    // previously extracted from a PSU archive — even one with a
                    // totally different on-disk name now — `file.filename` will
                    // end up as whatever the metadata header says, NOT what the
                    // user picked. That's how the tree node ends up displaying
                    // an unrelated filename like "charamake_back02.xnt" after the
                    // user picked "ep02m01.sfd" from disk.
                    //
                    // Always honor the user's choice instead. If they picked
                    // "ep02m01.sfd", that's the name we use, full stop.
                    if (!string.Equals(file.filename, pickedFilename, StringComparison.Ordinal))
                    {
                        file.filename = pickedFilename;
                    }

                    if (owningFile is FilenameAwareContainerFile awareContainerFile)
                    {
                        string filename = file.filename;
                        if (filename != tag.FileName && !awareContainerFile.ValidateFilename(filename))
                        {
                            FileRenameForm rename = new FileRenameForm(filename);
                            while (!awareContainerFile.ValidateFilename(filename))
                            {
                                if (rename.ShowDialog() == DialogResult.OK)
                                {
                                    filename = rename.FileName;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                        if (filename != file.filename)
                        {
                            file.filename = filename;
                        }
                    }
                }
                owningFile.replaceFile(node.Index, file);
                node.Text = file.filename;
                tag.FileName = file.filename;
                PsuFile parsedFile = owningFile.getFileParsed(node.Index);
                if (parsedFile is ContainerFile)
                {
                    node.Nodes.Clear();
                    addChildFiles(node.Nodes, (ContainerFile)parsedFile);
                }

                // ---- Parsed-layer cache sync for the WAV intercept ----
                // replaceFile updates the raw byte layer (getFileRaw sees the
                // new bytes immediately — confirmed earlier via diagnostic
                // readback), but the parsed object layer (getFileParsed) keeps
                // returning the pre-replacement cached object. That cache is
                // what the preview reads from, AND, based on the symptom that
                // Save As writes the original bytes after a successful
                // replaceFile, almost certainly what Save As reads from too.
                //
                // Force the parsed cache to hold the new bytes by overwriting
                // theData on the cached UnpointeredFile. If parsedFile is some
                // other type the sync is a no-op — Save As may or may not be
                // correct in that case, but it's no worse than today.
                if (intercepted && parsedFile is UnpointeredFile cachedUnpointered)
                {
                    cachedUnpointered.theData = file.fileContents;
                }

                var sel = treeView1.SelectedNode;
                treeView1.SelectedNode = null;
                treeView1.SelectedNode = sel;

                // ---- Preview refresh for the WAV intercept ----
                // The re-select above reaches treeView1_AfterSelect, which
                // builds the AdxPreviewPanel from a cached UnpointeredFile
                // returned by getFileParsed — that cache still references the
                // pre-replacement bytes, so the preview plays the OLD audio
                // even though the container actually holds the new bytes (the
                // earlier diagnostic confirmed this directly via getFileRaw).
                //
                // Sidestep the cache: build the preview directly from the
                // encoded bytes we just wrote, mirroring how the fake-container
                // path does it. Also rewrite the info text to match the
                // fake-container styling so both views look identical.
                if (intercepted)
                {
                    try
                    {
                        // Hash-key lookup, identical to MainForm_Archive.cs's
                        // in-container ADX preview branch, so a known sound
                        // title is shown in both contexts.
                        string hashKey = Path
                            .GetFileNameWithoutExtension(tag.FileName ?? "")
                            .TrimStart('-');
                        string mappedTitle = null;
                        if (hashKey.Length == 32
                            && hashKey.All(c => "0123456789abcdefABCDEF".Contains(c)))
                        {
                            AdxHashMap.TryGetValue(hashKey.ToLowerInvariant(), out mappedTitle);
                        }

                        string infoText =
                            "ADX audio file detected.\n\n" +
                            "If you wish to replace this file, convert a .wav to .adx and rename it\n" +
                            "to the hash name without the .adx extension, or replace the .adx in\n" +
                            "this software with your new .adx sound file and save the hash.\n\n" +
                            $"File name: {tag.FileName}";

                        if (mappedTitle != null)
                            infoText += $"\n\nADX Mapping: {mappedTitle}";

                        ClearRightPanel();
                        var pendingPreview = new AdxPreviewPanel(
                            file.fileContents, infoText, mappedTitle ?? tag.FileName);
                        splitContainer1.Panel2.Controls.Add(pendingPreview);
                    }
                    catch (Exception ex)
                    {
                        // The replacement is already in the container; a
                        // preview hiccup doesn't undo it, but warn so the user
                        // knows why the panel didn't update.
                        MessageBox.Show(
                            $"The WAV was encoded and stored in the container, " +
                            $"but the preview could not be refreshed:\n{ex.Message}\n\n" +
                            "Save As will still produce the replacement correctly.",
                            "Preview Refresh Failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // Right-click is handled entirely by the context menu strip;
            // we only need to ensure SelectedNode is set so the strip sees
            // the right node. Multi-selection is untouched.
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = e.Node;
            }
        }

        /// <summary>
        /// Called by MultiSelectTreeView.WndProc on WM_LBUTTONDOWN before the
        /// base control processes the click. Ctrl/Shift clicks are fully handled
        /// here; the subclass suppresses the native selection change in those cases
        /// so the tree does not clear our multi-selection highlight.
        /// Plain clicks just clear the set so the normal single-select path works.
        /// </summary>
        internal void OnMultiSelectMouseDown(TreeNode node, bool ctrl, bool shift)
        {
            if (!ctrl && !shift)
            {
                // Plain click: clear the extra selection set and let the tree
                // do its normal single-node selection.
                _multiSelectedNodes.Clear();
                _multiSelectAnchor = node;
            }
            else if (ctrl)
            {
                // Ctrl+Click: toggle this node in the set.
                if (_multiSelectedNodes.Contains(node))
                    _multiSelectedNodes.Remove(node);
                else
                    _multiSelectedNodes.Add(node);

                _multiSelectAnchor = node;
                // Move the primary selection to the clicked node so currentRight stays in sync.
                treeView1.SelectedNode = node;
            }
            else // Shift
            {
                // Shift+Click: select a contiguous range of siblings from the anchor.
                if (_multiSelectAnchor == null ||
                    _multiSelectAnchor.Parent != node.Parent)
                {
                    _multiSelectedNodes.Clear();
                    _multiSelectAnchor = node;
                }
                else
                {
                    TreeNodeCollection siblings = node.Parent != null
                        ? node.Parent.Nodes
                        : treeView1.Nodes;

                    int lo = Math.Min(_multiSelectAnchor.Index, node.Index);
                    int hi = Math.Max(_multiSelectAnchor.Index, node.Index);

                    _multiSelectedNodes.Clear();
                    for (int i = lo; i <= hi; i++)
                        _multiSelectedNodes.Add(siblings[i]);
                }

                treeView1.SelectedNode = node;
            }

            treeView1.Invalidate();
        }

        private void disableScriptParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            disableScriptParsingToolStripMenuItem.Checked = !disableScriptParsingToolStripMenuItem.Checked;
            PsuFiles.parseScripts = !disableScriptParsingToolStripMenuItem.Checked;
        }

        private void exportAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Need *something* loaded with files in it. We support two cases:
            //   1) The loaded container is itself an NBL  → look at its NMLL chunk directly.
            //   2) The loaded container holds NBLs inside (e.g. an AFS hash file)
            //      → walk each child NBL and export from each one's NMLL chunk
            //        into its own subfolder, so re-import can target the right NBL later.
            if (loadedContainer == null || loadedContainer.getFilenames().Count == 0)
            {
                MessageBox.Show(
                    "No archive is currently loaded.\r\n\r\n" +
                    "Weapon export reads itemWeaponParam_*.xnr files from the NMLL chunk " +
                    "of an NBL archive. Open an NBL or an AFS containing NBLs first.",
                    "Nothing to Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!PromptForFolder("Select the folder to export weapon param text files into", out string exportFolder))
                return;

            int exportedCount = 0;          // total weapon files written
            int nblsWithWeapons = 0;        // distinct NBLs that contributed at least one
            var errors = new List<string>(); // per-NBL load errors, surfaced at the end

            if (loadedContainer is NblLoader topNbl)
            {
                // Case 1: a single NBL is loaded directly.
                int wrote = ExportWeaponsFromNbl(topNbl, exportFolder, errors);
                exportedCount += wrote;
                if (wrote > 0) nblsWithWeapons++;
            }
            else
            {
                // Case 2: a container of containers (AFS-style). Walk children,
                // and for each one that turns out to be an NBL, export its
                // weapons into a subfolder named after the child entry so
                // results from different NBLs don't collide.
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

                    // Use the child entry name (sans .nbl) as the subfolder.
                    // Sanitize defensively — archive filenames are normally
                    // well-behaved but we don't want a stray character to
                    // blow up Path.Combine on someone.
                    string subfolderName = SanitizeFolderName(
                        Path.GetFileNameWithoutExtension(childName));
                    string nblFolder = Path.Combine(exportFolder, subfolderName);

                    // Only create the subfolder if this NBL actually has
                    // weapons — no empty folders left lying around for NBLs
                    // that don't contain any weapon params.
                    int wrote = ExportWeaponsFromNbl(childNbl, nblFolder, errors,
                        ensureFolderExists: true);
                    exportedCount += wrote;
                    if (wrote > 0) nblsWithWeapons++;
                }
            }

            // Report outcome. Previously the operation completed silently
            // which made the menu item look broken when nothing matched.
            if (exportedCount == 0)
            {
                string body =
                    "No weapon parameter files were found.\r\n\r\n" +
                    "Weapon export looks for files named 'itemWeaponParam_*.xnr' inside " +
                    "the NMLL chunk of any NBL archive in the loaded container.";
                if (errors.Count > 0)
                {
                    body += "\r\n\r\nSome children couldn't be inspected:\r\n  " +
                            string.Join("\r\n  ", errors.Take(5)) +
                            (errors.Count > 5 ? $"\r\n  ... ({errors.Count - 5} more)" : "");
                }
                MessageBox.Show(body, "No Weapons Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                string summary =
                    $"Exported {exportedCount} weapon parameter file{(exportedCount == 1 ? "" : "s")} " +
                    $"from {nblsWithWeapons} NBL archive{(nblsWithWeapons == 1 ? "" : "s")} to:\r\n\r\n{exportFolder}";
                if (errors.Count > 0)
                {
                    summary += $"\r\n\r\n{errors.Count} child{(errors.Count == 1 ? "" : "ren")} couldn't be inspected " +
                               "(likely not NBL archives or corrupted entries).";
                }
                MessageBox.Show(summary, "Export Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Export every itemWeaponParam_*.xnr from <paramref name="nbl"/>'s NMLL
        /// chunk into <paramref name="destFolder"/> as .txt files. Returns the
        /// number of files written. Errors are appended to <paramref name="errors"/>
        /// rather than thrown so a single bad NBL doesn't abort a multi-NBL export.
        /// </summary>
        /// <param name="ensureFolderExists">
        /// When true (used for per-NBL subfolders), the folder is created lazily
        /// on the first matching file. Avoids creating empty subfolders for
        /// NBLs that contain no weapons.
        /// </param>
        private int ExportWeaponsFromNbl(NblLoader nbl, string destFolder,
            List<string> errors, bool ensureFolderExists = false)
        {
            int wrote = 0;
            NblChunk nmllChunk;
            try
            {
                nmllChunk = (NblChunk)nbl.getFileParsed(0);
            }
            catch (Exception ex)
            {
                errors.Add($"{nbl.filename ?? "(unnamed NBL)"}: couldn't read NMLL chunk ({ex.Message})");
                return 0;
            }

            var nmllFilenames = nmllChunk.getFilenames();
            foreach (string filename in nmllFilenames)
            {
                if (!filename.Contains("itemWeaponParam"))
                    continue;
                if (!(nmllChunk.getFileParsed(filename) is WeaponParamFile weaponParamFile))
                    continue;

                if (ensureFolderExists && wrote == 0)
                {
                    try { Directory.CreateDirectory(destFolder); }
                    catch (Exception ex)
                    {
                        errors.Add($"{nbl.filename ?? "(unnamed NBL)"}: couldn't create folder '{destFolder}' ({ex.Message})");
                        return 0;
                    }
                }

                MemoryStream memStream = new MemoryStream();
                weaponParamFile.saveTextFile(memStream);
                File.WriteAllBytes(Path.Combine(destFolder, filename + ".txt"), memStream.ToArray());
                wrote++;
            }
            return wrote;
        }

        /// <summary>
        /// Replace any character that's invalid in a Windows folder name with '_'.
        /// Archive filenames are normally safe but we don't want a stray ':' or
        /// '*' (or an empty string) to crash Path.Combine.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private void importAllWeaponsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Mirrors the export contract:
            //   - If a single NBL is loaded, the folder is treated as a flat
            //     set of .txt files (back-compat with the original behavior).
            //   - If an AFS-style container is loaded, each subfolder in the
            //     picked folder corresponds to a child NBL by name. We match
            //     subfolder name → child entry name (with .nbl) and import
            //     the .txt files inside each subfolder into that NBL.
            if (loadedContainer == null || loadedContainer.getFilenames().Count == 0)
            {
                MessageBox.Show(
                    "No archive is currently loaded.\r\n\r\n" +
                    "Weapon import applies edits from text files back to itemWeaponParam_*.xnr " +
                    "entries in the NMLL chunk of an NBL archive. Open the target NBL (or AFS " +
                    "containing NBLs) first, then run import.",
                    "Nothing to Import Into",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!PromptForFolder("Select the folder containing weapon param text files to import", out string importFolder))
                return;

            int importedCount = 0;
            int nblsTouched = 0;
            var skippedFiles = new List<string>();
            var errors = new List<string>();

            if (loadedContainer is NblLoader topNbl)
            {
                // Case 1: a single NBL is loaded. Use the flat-folder layout
                // for back-compat: every weapon .txt sits directly inside the
                // chosen folder, no subfolders.
                int wrote = ImportWeaponsIntoNbl(topNbl, importFolder, skippedFiles, errors);
                importedCount += wrote;
                if (wrote > 0) nblsTouched++;
            }
            else
            {
                // Case 2: AFS-style container. Each direct subfolder corresponds
                // by name to a child NBL. Build a quick lookup of child NBL
                // entry names (lowercased, sans .nbl) so subfolder names like
                // "ob_009_mm_0" match the child entry "ob_009_mm_0.nbl".
                var childNames = loadedContainer.getFilenames();
                var childNblIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < childNames.Count; i++)
                {
                    string keyWithExt = childNames[i];
                    string keyNoExt = Path.GetFileNameWithoutExtension(keyWithExt);
                    // Register both with and without extension so either form works.
                    if (!childNblIndex.ContainsKey(keyWithExt))
                        childNblIndex[keyWithExt] = i;
                    if (!childNblIndex.ContainsKey(keyNoExt))
                        childNblIndex[keyNoExt] = i;
                }

                string[] subfolders;
                try
                {
                    subfolders = Directory.GetDirectories(importFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Couldn't read the selected folder:\r\n\r\n{ex.Message}",
                        "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (subfolders.Length == 0)
                {
                    MessageBox.Show(
                        "The selected folder doesn't contain any subfolders.\r\n\r\n" +
                        "When an AFS (or similar multi-NBL container) is loaded, weapon import " +
                        "expects one subfolder per target NBL of the same layout 'Export All " +
                        "Weapons' produces. Each subfolder name should match an NBL entry name " +
                        "in the archive (e.g. 'ob_009_mm_0' for 'ob_009_mm_0.nbl').",
                        "No Subfolders Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (string subfolder in subfolders)
                {
                    string subfolderName = Path.GetFileName(subfolder);
                    if (!childNblIndex.TryGetValue(subfolderName, out int childIdx))
                    {
                        errors.Add($"'{subfolderName}': no matching NBL in the loaded archive");
                        continue;
                    }

                    NblLoader targetNbl;
                    try
                    {
                        targetNbl = loadedContainer.getFileParsed(childIdx) as NblLoader;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"'{subfolderName}': failed to load target NBL ({ex.Message})");
                        continue;
                    }
                    if (targetNbl == null)
                    {
                        errors.Add($"'{subfolderName}': matching entry isn't an NBL");
                        continue;
                    }

                    int wrote = ImportWeaponsIntoNbl(targetNbl, subfolder, skippedFiles, errors);
                    importedCount += wrote;
                    if (wrote > 0) nblsTouched++;
                }
            }

            // Outcome summary. Three meaningful cases.
            if (importedCount == 0 && skippedFiles.Count == 0 && errors.Count == 0)
            {
                MessageBox.Show(
                    "No weapon parameter text files were found to import.\r\n\r\n" +
                    "Import looks for files whose names contain 'itemWeaponParam' " +
                    "(typically produced by 'Export All Weapons'). None were found, so " +
                    "nothing was imported.",
                    "No Files to Import",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (importedCount == 0)
            {
                var sb = new StringBuilder();
                sb.Append("No weapon parameter files were imported.\r\n\r\n");
                sb.Append("Check that text filenames correspond to existing " +
                          "itemWeaponParam_*.xnr entries");
                if (loadedContainer is NblLoader)
                {
                    sb.Append(" in the loaded NBL.");
                }
                else
                {
                    sb.Append(", and that subfolder names match NBL entry names in the " +
                              "loaded archive (e.g. 'ob_009_mm_0' for 'ob_009_mm_0.nbl').");
                }
                AppendDiagnostics(sb, skippedFiles, errors);
                MessageBox.Show(sb.ToString(), "No Matches Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append($"Imported {importedCount} weapon parameter file{(importedCount == 1 ? "" : "s")} ");
                sb.Append($"across {nblsTouched} NBL archive{(nblsTouched == 1 ? "" : "s")}.\r\n\r\n");
                sb.Append("Remember to save the archive to persist these changes.");
                AppendDiagnostics(sb, skippedFiles, errors);
                MessageBox.Show(sb.ToString(), "Import Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Import every itemWeaponParam_*.txt file in <paramref name="sourceFolder"/>
        /// into the NMLL chunk of <paramref name="nbl"/>. Returns the number of
        /// files successfully imported. Files that don't match an entry are
        /// appended to <paramref name="skippedFiles"/>; errors go to <paramref name="errors"/>.
        /// </summary>
        private int ImportWeaponsIntoNbl(NblLoader nbl, string sourceFolder,
            List<string> skippedFiles, List<string> errors)
        {
            int wrote = 0;
            string[] files;
            try
            {
                files = Directory.GetFiles(sourceFolder);
            }
            catch (Exception ex)
            {
                errors.Add($"'{Path.GetFileName(sourceFolder)}': couldn't read folder ({ex.Message})");
                return 0;
            }

            List<string> nmllFilenames;
            try
            {
                nmllFilenames = ((NblChunk)nbl.getFileParsed(0)).getFilenames();
            }
            catch (Exception ex)
            {
                errors.Add($"'{nbl.filename ?? Path.GetFileName(sourceFolder)}': couldn't read NMLL chunk ({ex.Message})");
                return 0;
            }

            foreach (string filename in files)
            {
                if (!filename.Contains("itemWeaponParam"))
                    continue;

                //try replacing .txt with nothing (e.g itemWeaponParam_01DKSword.xnr.txt)
                if (tryImportWeaponTextFile(nbl, nmllFilenames, filename,
                    Path.GetFileName(filename).Replace(".txt", "")))
                {
                    wrote++;
                }
                //try replacing .txt with .xnr (e.g itemWeaponParam_01DKSword.txt) -- parser doesn't do this, but other people may.
                else if (tryImportWeaponTextFile(nbl, nmllFilenames, filename,
                    Path.GetFileName(filename).Replace(".txt", ".xnr")))
                {
                    wrote++;
                }
                else
                {
                    skippedFiles.Add(Path.GetFileName(filename));
                }
            }
            return wrote;
        }

        /// <summary>
        /// Tack the skipped-file list and any error messages onto a result
        /// summary. Caps each section at 5 lines so a runaway count doesn't
        /// produce a MessageBox you can't read.
        /// </summary>
        private static void AppendDiagnostics(StringBuilder sb,
            List<string> skippedFiles, List<string> errors)
        {
            if (skippedFiles.Count > 0)
            {
                sb.Append($"\r\n\r\n{skippedFiles.Count} file{(skippedFiles.Count == 1 ? " was" : "s were")} skipped " +
                          "(no matching entry in the target NBL):\r\n  ");
                sb.Append(string.Join("\r\n  ", skippedFiles.Take(5)));
                if (skippedFiles.Count > 5)
                    sb.Append($"\r\n  ... ({skippedFiles.Count - 5} more)");
            }
            if (errors.Count > 0)
            {
                sb.Append($"\r\n\r\n{errors.Count} problem{(errors.Count == 1 ? "" : "s")} encountered:\r\n  ");
                sb.Append(string.Join("\r\n  ", errors.Take(5)));
                if (errors.Count > 5)
                    sb.Append($"\r\n  ... ({errors.Count - 5} more)");
            }
        }

        private bool tryImportWeaponTextFile(NblLoader nbl, List<string> nmllFilenames, string filepath, string attemptFilename)
        {
            if (nmllFilenames.Contains(attemptFilename) && (nbl.chunks[0].getFileParsed(attemptFilename) is WeaponParamFile))
            {
                WeaponParamFile paramFile = (WeaponParamFile)nbl.chunks[0].getFileParsed(attemptFilename);
                using (FileStream inStream = new FileStream(filepath, FileMode.Open))
                {
                    paramFile.loadTextFile(inStream);
                }
                return true;
            }
            return false;
        }

        // ====================== File → Open ======================
        // Attempts to open the picked file as a PSU archive; if that fails,
        // falls back to opening it as a single ADX. The destructive UI
        // changes (clearing the right panel, updating the title, dropping
        // pendingAdxReplacementBytes) are deferred until we know the open
        // will succeed — otherwise picking a non-PSU file blanks the screen
        // for the duration of the "Unknown File Format" dialog, even if the
        // welcome screen is later restored. By probing first and committing
        // only on success, a failed open leaves the previous view untouched.
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() != DialogResult.OK)
                return;

            string fileName = fileDialog.FileName;

            // Probe: can this file be opened at all? Try archive first, then
            // ADX. openPSUArchive populates treeView1.Nodes on success, so we
            // can't just call it speculatively — instead, route through a
            // throwaway TreeNodeCollection-equivalent by checking IsValidAdxFile
            // for the ADX side, and accepting that openPSUArchive itself is
            // the only way to know if archive parsing will succeed. To avoid
            // a half-loaded tree on archive failure, we let openPSUArchive run
            // normally but DON'T tear down the welcome screen first; on
            // failure we explicitly clean up whatever it added.
            bool wasShowingWelcome = welcomeVisible;
            string previousTitle = this.Text;
            string newTitle = "PSU Archive Explorer " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " " + Path.GetFileName(fileName);

            // Commit the destructive UI changes now, but remember the
            // pre-open state so we can roll back on total failure. The user
            // sees a brief title/panel change either way, but the rollback
            // happens BEFORE the "Unknown File Format" dialog appears, not
            // after — so they don't watch the welcome screen disappear,
            // dismiss a dialog, then watch it reappear.
            this.Text = newTitle;
            ClearRightPanel();
            pendingAdxReplacementBytes = null;

            bool success = openPSUArchive(fileName, treeView1.Nodes);

            if (!success)
            {
                // Roll back BEFORE the suggestion dialog, so the user sees
                // the welcome screen behind the dialog rather than a blank
                // panel. Whatever openPSUArchive added to the tree (if
                // anything) gets cleared here too.
                if (wasShowingWelcome)
                {
                    this.Text = previousTitle;
                    treeView1.Nodes.Clear();
                    _multiSelectedNodes.Clear();
                    _multiSelectAnchor = null;
                    ClearRightPanel();
                    ShowWelcomeScreen();
                }

                // Now run the ADX fallback. If it succeeds, OpenSingleFileAsAdx
                // will tear the welcome screen down again on its own path
                // (via ClearRightPanel in LoadAdxIntoRightPanel) — that's the
                // happy path. If it shows the "Unknown File Format" prompt,
                // the welcome screen we just restored stays up behind it.
                TryOpenAsAdx(fileName);

                // If TryOpenAsAdx didn't actually load anything (e.g. user
                // declined the rename, or the file failed ADX validation),
                // and the user wasn't at the welcome screen to begin with,
                // restore the previous title at least.
                if (treeView1.Nodes.Count == 0 && !wasShowingWelcome)
                {
                    this.Text = previousTitle;
                }
            }

            // Reset any active container search from the previous archive so
            // stale results don't carry over, then update the combo visibility.
            ResetContainerSearchIfActive();
            UpdateContainerModeVisibility(loadedContainer != null || treeView1.Nodes.Count > 0);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Launches a new, independent instance of PSU Archive Explorer so the user
        /// can have multiple sessions open at once (e.g. compare two archives side
        /// by side). The new process is fully independent and the current session
        /// is unaffected if it is closed.
        /// </summary>
        private void openNewSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
                {
                    // Fallback for unusual deployment scenarios (single-file publish, etc.)
                    exePath = Application.ExecutablePath;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not start a new session:\n" + ex.Message,
                    "New Session",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void insertNMLLFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loadedContainer is NblLoader && treeView1.SelectedNode != null && treeView1.SelectedNode.Level == 1)
            {
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    using (Stream inStream = fileDialog.OpenFile())
                    {
                        ((ContainerFile)loadedContainer.getFileParsed(0)).addFile(treeView1.SelectedNode.Index, new RawFile(inStream, Path.GetFileName(fileDialog.FileName)));
                    }
                    treeView1.Nodes.Clear();
                    _multiSelectedNodes.Clear();
                    _multiSelectAnchor = null;
                    addChildFiles(treeView1.Nodes, loadedContainer);
                }
            }
        }

        private void decryptNMLBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                fileStream.Seek(3, SeekOrigin.Begin);
                byte endian = (byte)fileStream.ReadByte();
                fileStream.Seek(0, SeekOrigin.Begin);
                BinaryReader fileLoader;
                bool bigEndian = false;
                if (endian == 0x42)
                {
                    fileLoader = new BigEndianBinaryReader(fileStream);
                    bigEndian = true;
                }
                else
                    fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey, bigEndian);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                }

                fileStream.Seek(nmllDataLoc, SeekOrigin.Begin);
                byte[] encryptedNmll = fileLoader.ReadBytes((int)size);
                byte[] decryptedNmll = fish.decryptBlock(encryptedNmll);
                byte[] decompressedNmll = compressedSize != 0 ? PrsCompDecomp.Decompress(decryptedNmll, uncompressedSize) : decryptedNmll;

                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
                File.WriteAllBytes(fileDialog.FileName + ".encryptNmll", encryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decryptNmll", decryptedNmll);
                File.WriteAllBytes(fileDialog.FileName + ".decompressNmll", decompressedNmll);
            }
        }

        private void decryptNMLLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                byte[] fileContents = File.ReadAllBytes(fileDialog.FileName);

                MemoryStream fileStream = new MemoryStream(fileContents);
                BinaryReader fileLoader = new BinaryReader(fileStream);

                string formatName = new String(fileLoader.ReadChars(4));
                ushort fileVersion = fileLoader.ReadUInt16();
                ushort chunkFilenameLength = fileLoader.ReadUInt16();
                uint headerSize = fileLoader.ReadUInt32();
                uint nmllCount = fileLoader.ReadUInt32();
                uint uncompressedSize = fileLoader.ReadUInt32();
                uint compressedSize = fileLoader.ReadUInt32();
                uint pointerLength = fileLoader.ReadUInt32() / 4;
                uint blowfishKey = fileLoader.ReadUInt32();
                uint tmllHeaderSize = fileLoader.ReadUInt32();
                uint tmllDataSizeUncomp = fileLoader.ReadUInt32();
                uint tmllDataSizeComp = fileLoader.ReadUInt32();
                uint tmllCount = fileLoader.ReadUInt32();
                uint tmllHeaderLoc = 0;

                uint pointerLoc = 0;

                uint size = compressedSize == 0 ? uncompressedSize : compressedSize;

                uint nmllDataLoc = (uint)((headerSize + 0x7FF) & 0xFFFFF800);
                pointerLoc = (uint)(nmllDataLoc + size + 0x7FF) & 0xFFFFF800;
                if (tmllCount > 0)
                    tmllHeaderLoc = (pointerLoc + pointerLength * 4 + 0x7FF) & 0xFFFFF800;

                BlewFish fish = new BlewFish(blowfishKey);

                for (int i = 0; i < nmllCount; i++)
                {
                    int headerLoc = 0x40 + i * 0x60;
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < tmllCount; i++)
                {
                    uint headerLoc = (uint)(tmllHeaderLoc + 0x30 + i * 0x60);
                    byte[] toDecrypt = new byte[0x30];
                    Array.Copy(fileContents, headerLoc, toDecrypt, 0, 0x30);
                    toDecrypt = fish.decryptBlock(toDecrypt);
                    Array.Copy(toDecrypt, 0, fileContents, headerLoc, 0x30);

                    sb.Append(Encoding.ASCII.GetString(toDecrypt, 0, 0x20).Split('\0')[0] + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4C)) + "\t");
                    sb.Append(BitConverter.ToUInt16(fileContents, (int)(headerLoc + 0x4E)) + "\n");
                }
                File.WriteAllText(fileDialog.FileName + ".tml.list", sb.ToString());
                File.WriteAllBytes(fileDialog.FileName + ".decrypt", fileContents);
            }
        }

        private void copyHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.Archive);
        }

        private void copyFilenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.FileName);
        }

        private void copyPathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedSearchResultField(hit => hit.InnerPath);
        }

        private void CopySelectedSearchResultField(Func<FileIndex.SearchResult, string> selector)
        {
            if (searchResults.SelectedItems.Count == 0) return;

            // Collect the requested field from every selected row.
            // Use a HashSet to dedupe — when the user has 20 rows selected
            // from the same archive, "Copy hash" should put one hash on the
            // clipboard, not 20 identical lines. We use an ordered list
            // alongside the set so the clipboard order matches selection
            // order rather than whatever HashSet enumeration happens to do.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var values = new List<string>(searchResults.SelectedItems.Count);

            foreach (ListViewItem item in searchResults.SelectedItems)
            {
                var hit = item.Tag as FileIndex.SearchResult;
                if (hit == null) continue;

                string value = selector(hit);
                if (string.IsNullOrEmpty(value)) continue;

                if (seen.Add(value))
                    values.Add(value);
            }

            if (values.Count == 0) return;

            string clipboardText = string.Join(Environment.NewLine, values);

            try
            {
                Clipboard.SetText(clipboardText);
                if (values.Count == 1)
                {
                    searchStatusLabel.Text = $"Copied: {values[0]}";
                }
                else
                {
                    searchStatusLabel.Text = $"Copied {values.Count} unique values";
                }
            }
            catch
            {
                // Clipboard occasionally fails not worth crashing over
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new MainSettings(this);
            }
            settings.Show();
            settings.BringToFront();
        }

        private void viewInHexButton_Click(object sender, EventArgs e)
        {
            if (currentRight != null)
            {
                PointeredFile pointeredFile = null;
                byte[] file = currentRight.ToRaw();
                if (currentRight.calculatedPointers != null)
                {
                    if (currentRight is PointeredFile)
                    {
                        pointeredFile = (PointeredFile)currentRight;
                    }
                    else
                    {
                        //For now, Big Endian files don't really need to be considered here since they'd be a PointeredFile already. Possibly add in further support if added elsewhere later
                        pointeredFile = new PointeredFile(currentRight.filename, file, currentRight.header, currentRight.calculatedPointers, 0, false);
                    }
                    pointeredFile.ToRaw();
                }

                string headingText = $"Selected File: {currentRight.filename}";

                if (currentFileHexForm != null)
                {
                    currentFileHexForm.Close();
                }
                if (pointeredFile != null)
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        pointeredFile);
                }
                else
                {
                    currentFileHexForm = new HexEditForm(file, headingText, true,
                        null);
                }

                currentFileHexForm.Show();
            }
        }

        private void compressChunkToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed = compressChunkToolStripMenuItem.Checked;
                if (compressChunkToolStripMenuItem.Checked)
                {
                    treeView1.SelectedNode.ForeColor = Color.Green;
                }
                else
                {
                    treeView1.SelectedNode.ForeColor = Color.Black;
                }
                if (node.Parent != null)
                {
                    if (parent.Compressed)
                    {
                        node.Parent.ForeColor = Color.Green;
                    }
                    else
                    {
                        node.Parent.ForeColor = Color.Black;
                    }
                }
            }
        }

        public void setNmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.NmllCompressionOverride = settings;
        }

        public void setTmllCompressOverride(NblLoader.CompressionOverride settings)
        {
            NblLoader.TmllCompressionOverride = settings;
        }

        private void nblChunkContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            FileTreeNodeTag tag = treeView1.SelectedNode.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                compressChunkToolStripMenuItem.Checked = ((NblChunk)parent.getFileParsed(treeView1.SelectedNode.Index)).Compressed;
            }
        }

        AnimationNameHashDialog dialog;

        private void calculateAnimationNameHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dialog == null || dialog.IsDisposed)
            {
                dialog = new AnimationNameHashDialog();
            }
            if (currentRight != null && !(currentRight is NblChunk))
            {
                dialog.SetFileName(currentRight.filename);
            }
            if (!dialog.Visible)
            {
                dialog.Show();
            }
        }

        private void addFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                ContainerFile parent = tag.OwnerContainer;
                OpenFileDialog replaceDialog = new OpenFileDialog();

                if (replaceDialog.ShowDialog() == DialogResult.OK)
                {
                    NblChunk chunk = parent.getFileParsed(node.Index) as NblChunk;
                    if (chunk == null) return;

                    RawFile file = new RawFile(replaceDialog.OpenFile(), Path.GetFileName(replaceDialog.FileName));
                    string filename = file.filename;
                    if (!chunk.ValidateFilename(filename))
                    {
                        while (!chunk.ValidateFilename(filename))
                        {
                            using (FileRenameForm rename = new FileRenameForm(filename))
                            {
                                if (rename.ShowDialog() == DialogResult.OK)
                                {
                                    filename = rename.FileName;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                    }
                    if (filename != file.filename)
                    {
                        file.filename = filename;
                    }

                    chunk.addFile(file);

                    TreeNode newNode = new TreeNode(file.filename);
                    FileTreeNodeTag newTag = new FileTreeNodeTag();
                    newTag.OwnerContainer = chunk;
                    newTag.FileName = file.filename;
                    newNode.Tag = newTag;
                    newNode.ContextMenuStrip = arbitraryFileContextMenuStrip;
                    node.Nodes.Add(newNode);

                    if (file.fileheader == "NMLL" || file.fileheader == "NMLB")
                    {
                        addChildFiles(newNode.Nodes, (ContainerFile)chunk.getFileParsed(newNode.Index));
                    }
                    treeView1.SelectedNode = newNode;
                }
            }
        }

        private void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag == null) return;
            bool isAdxFile = tag.FileName?.EndsWith(".adx", StringComparison.OrdinalIgnoreCase) == true;
            if (isAdxFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the ADX file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another ADX file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            bool isSfdFile = tag.FileName?.EndsWith(".sfd", StringComparison.OrdinalIgnoreCase) == true;
            if (isSfdFile && !(tag.OwnerContainer is NblChunk))
            {
                MessageBox.Show(
                    "Cannot delete the SFD file.\n\n" +
                    "The game requires this data to be present. You can replace " +
                    "this with another SFD file, but it cannot be removed from " +
                    "the hashed container.",
                    "PSU Archive Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (tag.OwnerContainer is NblChunk chunk)
            {
                chunk.removeFile(node.Index);
                node.Remove();
            }
        }

        private void renameFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null) return;
            node.BeginEdit();
        }

        private void treeView1_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
        }

        private void treeView1_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            var node = e.Node;
            if (node == null) return;
            FileTreeNodeTag tag = node.Tag as FileTreeNodeTag;
            if (tag != null && tag.OwnerContainer is NblLoader)
            {
                e.CancelEdit = true;
            }
            else if (tag != null && e.Label != null)
            {
                if (!(tag.OwnerContainer is FilenameAwareContainerFile facf) || facf.ValidateFilename(e.Label))
                {
                    tag.OwnerContainer.renameFile(node.Index, e.Label);
                }
                else
                {
                    e.CancelEdit = true;
                }
            }
        }

        private void splitContainer1_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        /// <summary>
        /// Renders an informational panel in the right side of the form,
        /// styled to match the ADX-detected info panel (centered text on a
        /// light grey background). Used when the user selects a tree node
        /// that's meaningful in the hierarchy but doesn't have its own
        /// viewer (e.g. NMLL/TMLL chunks).
        /// The title is bold; the body uses regular weight. Stacked vertically
        /// in the centre of the panel via TableLayoutPanel so they stay
        /// centred as the form resizes.
        /// </summary>
        private void ShowChunkInfoPanel(string title, string body)
        {
            ClearRightPanel();
            BuildCenteredInfoPanel(title, body);
        }

        // Tracks whether the archive overview is currently the thing being
        // shown in the right panel. Lets MaybeShowArchiveOverviewPanel skip
        // its work most idle ticks, and lets us know when the user has
        // navigated away from it (so we don't re-show it on every idle).
        private bool isShowingArchiveOverview = false;

        /// <summary>
        /// Called every Application.Idle tick. Shows the archive overview
        /// panel iff: we have a loaded archive, the tree has nodes, no node
        /// is selected, the welcome screen is gone, and the right panel is
        /// empty. This naturally fires once after a fresh archive load (when
        /// ClearRightPanel has just run) and never re-fires until the user
        /// clicks somewhere and comes back to a clean state, which is exactly
        /// the behavior we want.
        /// </summary>
        private void MaybeShowArchiveOverviewPanel(object sender, EventArgs e)
        {
            if (welcomeVisible) return;
            if (loadedContainer == null) return;
            if (treeView1.Nodes.Count == 0) return;
            if (treeView1.SelectedNode != null) return;
            if (splitContainer1.Panel2.Controls.Count > 0) return;

            try
            {
                ShowArchiveOverviewPanel();
            }
            catch (Exception ex)
            {
                // The overview is purely informational. If something throws
                // while building it, just swallow it — better an empty grey
                // panel than a crash on every idle tick.
                Console.WriteLine("Failed to build archive overview: " + ex);
            }
        }

        /// <summary>
        /// Renders an "archive overview" info panel in the right side of the
        /// form, summarising the currently-loaded archive: filename, type, and
        /// chunk/file counts. Shown automatically after a fresh archive load
        /// (when no node is selected) so the user has context instead of an
        /// empty grey panel.
        /// </summary>
        private void ShowArchiveOverviewPanel()
        {
            ClearRightPanel();

            string archiveFileName = string.IsNullOrEmpty(fileDialog.FileName)
                ? "(unknown)"
                : Path.GetFileName(fileDialog.FileName);

            string archiveType;
            string countsLine;

            if (loadedContainer is NblLoader nbl)
            {
                archiveType = "NBL Archive";
                countsLine = BuildNblCountsLine(nbl);
            }
            else if (loadedContainer is AfsLoader)
            {
                archiveType = "AFS Container";
                int total = SafeCountTopLevel();
                countsLine = total + " entr" + (total == 1 ? "y" : "ies");
            }
            else
            {
                archiveType = loadedContainer.GetType().Name;
                int total = SafeCountTopLevel();
                countsLine = total + " entr" + (total == 1 ? "y" : "ies");
            }

            string body = archiveType + "\r\n"
                        + countsLine + "\r\n\r\n"
                        + "Click any item in the tree on the left to view it.";

            BuildCenteredInfoPanel(archiveFileName, body);
            isShowingArchiveOverview = true;
        }

        /// <summary>
        /// Builds the per-chunk counts line for an NBL archive, e.g.
        /// "NMLL chunk: 47 files · TMLL chunk: 12 files". Falls back to a
        /// generic count if introspecting the chunks fails for any reason.
        /// </summary>
        private string BuildNblCountsLine(NblLoader nbl)
        {
            try
            {
                var topNames = nbl.getFilenames();
                var parts = new List<string>();

                for (int i = 0; i < topNames.Count; i++)
                {
                    string chunkLabel = topNames[i];
                    int innerCount = 0;
                    try
                    {
                        if (nbl.getFileParsed(i) is ContainerFile inner)
                        {
                            innerCount = inner.getFilenames().Count;
                        }
                    }
                    catch
                    {
                        innerCount = -1; // signal "couldn't count"
                    }

                    parts.Add(innerCount >= 0
                        ? chunkLabel + ": " + innerCount + " file" + (innerCount == 1 ? "" : "s")
                        : chunkLabel);
                }

                return string.Join("  ·  ", parts);
            }
            catch
            {
                int total = SafeCountTopLevel();
                return total + " chunk" + (total == 1 ? "" : "s");
            }
        }

        /// <summary>
        /// Returns the number of top-level entries in the loaded container,
        /// or 0 on any error. Used by the overview panel's fallback paths.
        /// </summary>
        private int SafeCountTopLevel()
        {
            try { return loadedContainer?.getFilenames()?.Count ?? 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Shared layout used by ShowChunkInfoPanel and ShowArchiveOverviewPanel:
        /// bold title, regular-weight body, both centred horizontally and
        /// vertically on a light-grey background. Mirrors the ADX info panel's
        /// look (minus the audio player).
        /// </summary>
        private void BuildCenteredInfoPanel(string title, string body)
        {
            var infoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(229, 229, 229),
            };

            // 4-row layout: top spacer, title, body, bottom spacer. The two
            // 50%-height spacers keep the title+body block vertically centred
            // as the form resizes, while the AutoSize rows in the middle let
            // the labels grow to fit their text.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Padding = new Padding(10),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 28,
            };

            var bodyLabel = new Label
            {
                Text = body,
                Font = new Font("Segoe UI", 10.5f),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 180,
            };

            layout.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 0);
            layout.Controls.Add(titleLabel, 0, 1);
            layout.Controls.Add(bodyLabel, 0, 2);
            layout.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 3);

            infoPanel.Controls.Add(layout);
            splitContainer1.Panel2.Controls.Add(infoPanel);
        }

        // ----------------------------------------------------------------
        // Multi-select TreeView initialisation
        // ----------------------------------------------------------------

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InitMultiSelectTree();
        }

        /// <summary>
        /// Called once from the form's Load event (wire up in the designer or
        /// constructor: this.Load += MainForm_Load).
        /// Swaps the designer-created treeView1 for a MultiSelectTreeView so
        /// that Ctrl/Shift clicks are intercepted at the WndProc level before
        /// the native control clears the selection highlight.
        /// </summary>
        private void InitMultiSelectTree()
        {
            var msTree = new MultiSelectTreeView(this);

            // Copy every property the designer sets on treeView1.
            msTree.Name = treeView1.Name;
            msTree.Dock = treeView1.Dock;
            msTree.Location = treeView1.Location;
            msTree.Size = treeView1.Size;
            msTree.TabIndex = treeView1.TabIndex;
            msTree.Font = treeView1.Font;
            msTree.ImageList = treeView1.ImageList;
            msTree.LabelEdit = treeView1.LabelEdit;
            msTree.HideSelection = false;   // keep highlight when focus moves
            msTree.FullRowSelect = treeView1.FullRowSelect;
            msTree.ShowLines = treeView1.ShowLines;
            msTree.ShowPlusMinus = treeView1.ShowPlusMinus;
            msTree.ShowRootLines = treeView1.ShowRootLines;
            msTree.Scrollable = treeView1.Scrollable;
            msTree.ContextMenuStrip = treeView1.ContextMenuStrip;

            // Re-wire all events from the old tree to the new one.
            msTree.AfterSelect += treeView1_AfterSelect;
            msTree.NodeMouseClick += treeView1_NodeMouseClick;
            msTree.BeforeLabelEdit += treeView1_BeforeLabelEdit;
            msTree.AfterLabelEdit += treeView1_AfterLabelEdit;

            // Replace in the parent container.
            var parent = treeView1.Parent;
            int idx = parent.Controls.GetChildIndex(treeView1);
            parent.Controls.Remove(treeView1);
            treeView1.Dispose();
            treeView1 = msTree;
            parent.Controls.Add(msTree);
            parent.Controls.SetChildIndex(msTree, idx);
        }
    }

    // ====================================================================
    // MultiSelectTreeView — subclass that intercepts WM_LBUTTONDOWN so
    // Ctrl/Shift clicks can update _multiSelectedNodes before the native
    // TreeView control changes the single selection.
    // ====================================================================

    /// <summary>
    /// A TreeView subclass that paints extra highlighted nodes for multi-
    /// selection and intercepts left-button-down messages so Ctrl/Shift
    /// modifiers are acted on before the base control clears the selection.
    /// </summary>
    internal sealed class MultiSelectTreeView : TreeView
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int TVM_SELECTITEM = 0x110B;
        private const int TVGN_CARET = 0x0009;

        private readonly MainForm _owner;
        private bool _suppressSelect = false;

        internal MultiSelectTreeView(MainForm owner)
        {
            _owner = owner;
            DrawMode = TreeViewDrawMode.OwnerDrawText;
            HideSelection = false;
            DrawNode += OnDrawNode;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;
                bool shift = (Control.ModifierKeys & Keys.Shift) != 0;

                if (ctrl || shift)
                {
                    // Hit-test to find which node was clicked.
                    int x = m.LParam.ToInt32() & 0xFFFF;
                    int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;
                    TreeViewHitTestInfo hit = HitTest(x, y);

                    if (hit?.Node != null)
                    {
                        // Tell the form to update _multiSelectedNodes first.
                        _owner.OnMultiSelectMouseDown(hit.Node, ctrl, shift);
                        // Suppress the native WM_LBUTTONDOWN so the tree does
                        // not call TVM_SELECTITEM and clear our highlight.
                        return;
                    }
                }
                else
                {
                    // Plain click — clear the multi-set, then let the tree handle it.
                    int x = m.LParam.ToInt32() & 0xFFFF;
                    int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;
                    TreeViewHitTestInfo hit = HitTest(x, y);
                    if (hit?.Node != null)
                        _owner.OnMultiSelectMouseDown(hit.Node, false, false);
                }
            }

            base.WndProc(ref m);
        }

        private void OnDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            bool isExtraSelected =
                _owner._multiSelectedNodes.Count > 1 &&
                _owner._multiSelectedNodes.Contains(e.Node) &&
                e.Node != SelectedNode;

            if (isExtraSelected)
            {
                // Fill the row with the system highlight colour so extra-selected
                // nodes look identical to the primary selection.
                using (var brush = new SolidBrush(SystemColors.Highlight))
                    e.Graphics.FillRectangle(brush, e.Bounds);

                TextRenderer.DrawText(
                    e.Graphics,
                    e.Node.Text,
                    Font,
                    e.Bounds,
                    SystemColors.HighlightText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine);
            }
            else
            {
                e.DrawDefault = true;
            }
        }
    }
}