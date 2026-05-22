using psu_archive_explorer.Forms.FileViewers;   // NomGlbImporter, GlbImportOptionsDialog
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Models;
using SharpGLTF.Schema2;
using System;
using System.Linq;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    /// <summary>
    /// "Import Animation from GLB" — the inverse of the viewer's Export button.
    /// Lets the user take a GLB they edited in Blender / Maya / etc. and bake
    /// it back into the NOM this viewer is showing.
    ///
    /// The import edits <c>internalFile</c> in place (the same NomFile instance
    /// the owning NBL chunk has cached) and sets its dirty flag, so the change
    /// is picked up when the user saves the archive. After a successful import
    /// the viewer refreshes itself so the preview reflects the new animation.
    ///
    /// Wiring: the btnImport button is created inline in NomFileViewer.cs's
    /// control-bar setup (next to btnExport) and its Click is wired to
    /// btnImport_Click, defined below.
    /// </summary>
    public partial class NomFileViewer
    {
        private void btnImport_Click(object sender, EventArgs e)
        {
            if (internalFile == null) return;

            // --- pick the GLB ---------------------------------------------
            string glbPath;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Import animation";
                ofd.Filter = "glTF Binary (*.glb)|*.glb|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                glbPath = ofd.FileName;
            }

            // --- frame-rate dialog ----------------------------------------
            float chosenRate;
            using (var dlg = new GlbImportOptionsDialog(internalFile.frameRate))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosenRate = dlg.ChosenFrameRate;
            }

            // --- confirm — this overwrites the current animation ----------
            var confirm = MessageBox.Show(this,
                "This will replace the animation \"" +
                (string.IsNullOrEmpty(internalFile.filename) ? "this NOM" : internalFile.filename) +
                "\" \n\n" +
                "Continue?",
                "Import Animation",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            // --- run the importer -----------------------------------------
            NomGlbImporter.ImportResult result;
            try
            {
                UseWaitCursor = true;
                Application.DoEvents();
                result = NomGlbImporter.Import(glbPath, internalFile, chosenRate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "The import failed unexpectedly:\n\n" + ex.Message,
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                UseWaitCursor = false;
            }

            // --- report ----------------------------------------------------
            if (!result.Success)
            {
                MessageBox.Show(this, result.Error,
                    "Import Could Not Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The importer edited internalFile in place. Refresh the viewer so
            // the summary, bone tree and preview all reflect the new data.
            RefreshAfterImport();

            string message =
                "Animation imported successfully.\n\n" +
                "Frames: " + internalFile.frameCount + " at " +
                chosenRate.ToString("0.###") + " fps";

            if (result.Warnings.Count > 0)
            {
                message += "\n\nNotes:\n  " +
                    string.Join("\n  ", result.Warnings.Take(8));
                if (result.Warnings.Count > 8)
                    message += "\n  ... (" + (result.Warnings.Count - 8) + " more)";
            }

            MessageBox.Show(this, message, "Import Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Rebuilds the viewer's derived state after <c>internalFile</c> has
        /// been edited by an import: per-bone stats, the summary header, the
        /// bone tree, and the preview animation.
        /// </summary>
        private void RefreshAfterImport()
        {
            boneStats = ComputeBoneStats(internalFile);
            PopulateSummaryHeader();

            // PopulateBoneTree appends to treeView1; clear it first so the
            // refresh doesn't stack a second copy of every node.
            treeView1.Nodes.Clear();
            PopulateBoneTree();

            if (previewControl != null)
                previewControl.LoadAnimation(internalFile, VanillaPsuSkeleton.Create());
        }

        /// <summary>
        /// Best-effort guess of the GLB's authoring frame rate from its
        /// keyframe timings — a hint shown in the rate dialog, never applied
        /// silently. Returns null if the GLB can't be read or is too sparse.
        /// </summary>
        private static float? TryDetectGlbFrameRate(string glbPath)
        {
            try
            {
                ModelRoot model = ModelRoot.Load(glbPath);
                if (model.LogicalAnimations == null ||
                    model.LogicalAnimations.Count == 0)
                    return null;

                Animation anim = model.LogicalAnimations[0];
                var times = new System.Collections.Generic.SortedSet<float>();

                foreach (var channel in anim.Channels)
                {
                    var rs = channel.GetRotationSampler();
                    if (rs != null)
                        foreach (var k in rs.GetLinearKeys())
                            times.Add(k.Key);

                    var ts = channel.GetTranslationSampler();
                    if (ts != null)
                        foreach (var k in ts.GetLinearKeys())
                            times.Add(k.Key);
                }

                if (times.Count < 2) return null;

                float smallestGap = float.MaxValue;
                float prev = float.NaN;
                foreach (float t in times)
                {
                    if (!float.IsNaN(prev))
                    {
                        float gap = t - prev;
                        if (gap > 1e-4f && gap < smallestGap)
                            smallestGap = gap;
                    }
                    prev = t;
                }

                if (smallestGap <= 0f || smallestGap == float.MaxValue)
                    return null;

                float rate = 1f / smallestGap;
                if (rate < 1f || rate > 240f) return null;
                return rate;
            }
            catch
            {
                return null;
            }
        }
    }
}
