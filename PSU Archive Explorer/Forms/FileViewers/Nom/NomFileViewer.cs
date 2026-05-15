using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Models;
using psu_archive_explorer.Exporters;

namespace psu_archive_explorer
{
    public partial class NomFileViewer : UserControl
    {
        NomFile internalFile;

        // Per-bone summary, cached at construction time. Used to colour the
        // tree (animated vs static), populate the per-bone right-panel view,
        // and feed the top-of-form summary line.
        private class BoneStats
        {
            public int RotationKeys;
            public int XKeys;
            public int YKeys;
            public int ZKeys;
            public int MinFrame = int.MaxValue;
            public int MaxFrame = -1;

            public bool HasAnyAnimation
            {
                get { return RotationKeys > 0 || XKeys > 0 || YKeys > 0 || ZKeys > 0; }
            }

            public int TotalKeys
            {
                get { return RotationKeys + XKeys + YKeys + ZKeys; }
            }
        }

        private BoneStats[] boneStats;

        /// <summary>
        /// One entry in the playback-speed dropdown. Pairs the display label
        /// with the actual multiplier so the SelectedIndexChanged handler can
        /// read the number directly instead of parsing "1.5x" back into 1.5f.
        /// ToString() is what the ComboBox renders.
        /// </summary>
        private sealed class SpeedOption
        {
            public readonly string Label;
            public readonly float Multiplier;
            public SpeedOption(string label, float multiplier)
            {
                Label = label;
                Multiplier = multiplier;
            }
            public override string ToString() { return Label; }
        }

        private NomAnimationPreview previewControl;

        public NomFileViewer(NomFile toImport)
        {
            InitializeComponent();
            internalFile = toImport;

            // Compute per-bone stats up front. The tree-population pass below
            // reads from this to colour nodes, and the right panel's per-bone
            // view reads from it too. Cheap — single linear walk per bone.
            boneStats = ComputeBoneStats(internalFile);

            PopulateSummaryHeader();
            PopulateBoneTree();

            InitializePreviewControl();

            // The tree is auto-sized once at construction (inside
            // PopulateBoneTree), but at that point the control often hasn't
            // been given its real size yet, so the clamp uses the designer
            // width. Re-run it the first time we get laid out at the actual
            // size so the splitter ends up correct. One-shot — after that the
            // user is free to drag the splitter wherever they like.
            this.Layout += NomFileViewer_FirstLayout;

            // Expanding a bone reveals its channel children, and a child label
            // ("Rotation Frames" etc.) can be wider than any collapsed bone
            // row. Re-measure on expand so the tree grows to fit them.
            treeView1.AfterExpand += (s, e) => AutoSizeTreePanel();
        }

        // Set true once the first real layout pass has auto-sized the tree, so
        // we stop reacting to Layout events and leave the splitter under user
        // control from then on.
        private bool treeAutoSized;

        private void NomFileViewer_FirstLayout(object sender, LayoutEventArgs e)
        {
            if (treeAutoSized) return;
            if (this.Width <= 0) return; // still not really sized — wait
            treeAutoSized = true;
            this.Layout -= NomFileViewer_FirstLayout;
            AutoSizeTreePanel();
        }
        private TabControl rightTabControl;

        private void InitializePreviewControl()
        {
            previewControl = new NomAnimationPreview();
            previewControl.Dock = DockStyle.Fill;

            rightTabControl = new TabControl { Dock = DockStyle.Fill };

            // Data Tab
            var dataTab = new TabPage("Data");
            dataTab.Controls.Add(frameDataTextBox);
            frameDataTextBox.Dock = DockStyle.Fill;

            // Preview Tab
            var previewTab = new TabPage("Preview");

            var previewPanel = new Panel { Dock = DockStyle.Fill };

            // Control Bar
            //
            // FlowLayoutPanel so the transport controls, speed selector and
            // Export button flow left-to-right and wrap onto a second row when
            // the preview pane is narrow. AutoSize + GrowAndShrink lets the bar
            // grow tall enough to show a wrapped second row instead of clipping
            // it — important now that the Export button has been moved in here
            // and the bar can hold more than one row's worth of controls.
            var controlBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                MinimumSize = new Size(0, 45)
            };

            var btnPlayPause = new Button { Text = "▶ Play", Width = 90, Height = 30 };
            var btnRestart = new Button { Text = "↺ Restart", Width = 90, Height = 30 };
            var trackTime = new TrackBar
            {
                Width = 350,
                Minimum = 0,
                Maximum = 1000,
                Value = 0
            };
            // Fixed width — wide enough for the readout at typical durations
            // ("0.00 / 3.00 s" up to a 3-digit "120.00 / 360.00 s"). Not
            // AutoSize: the text width changes every frame during playback,
            // and an auto-sizing label would jitter. This label is docked Left
            // inside bottomRow (see below); the width here is just how much of
            // that left edge it occupies.
            var lblTime = new Label
            {
                Text = "0.00 / 0.00 s",
                Width = 100,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 30
            };

            // Playback speed selector. The preview control already multiplies
            // its per-tick delta by a playbackSpeed field — this just gives
            // the user a way to set it. DropDownList style so the value is
            // always one of the known-good multipliers (no free-text parsing).
            // Positioning is handled further down: lblSpeed + cmbSpeed get put
            // into a right-docked sub-panel so they sit at the right edge of
            // the control bar rather than flowing left-to-right after lblTime.
            var lblSpeed = new Label
            {
                Text = "Speed:",
                Width = 48,
                TextAlign = ContentAlignment.MiddleRight,
                Height = 30
            };
            var cmbSpeed = new ComboBox
            {
                Width = 70,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            // Tag carries the actual multiplier so we don't have to parse the
            // display string back into a number.
            cmbSpeed.Items.Add(new SpeedOption("0.25x", 0.25f));
            cmbSpeed.Items.Add(new SpeedOption("0.5x", 0.5f));
            cmbSpeed.Items.Add(new SpeedOption("1x", 1.0f));
            cmbSpeed.Items.Add(new SpeedOption("1.5x", 1.5f));
            cmbSpeed.Items.Add(new SpeedOption("2x", 2.0f));
            cmbSpeed.SelectedIndex = 2; // default 1x

            cmbSpeed.SelectedIndexChanged += (s, e) =>
            {
                if (previewControl != null && cmbSpeed.SelectedItem is SpeedOption)
                {
                    var opt = (SpeedOption)cmbSpeed.SelectedItem;
                    previewControl.PlaybackSpeed = opt.Multiplier;
                }
            };

            // Export button — moved here from the yellow summary header so it
            // sits with the other action controls. It used to be a Designer
            // control (exportGlbButton); it's now created here instead. Placed
            // directly to the right of Restart in the flow order below, with a
            // normal button margin so it lines up with the transport buttons.
            var btnExport = new Button
            {
                Text = "Export Animation to GLB",
                Width = 170,
                Height = 30,
                Margin = new Padding(3, 3, 3, 3)
            };
            btnExport.Click += exportGlbButton_Click;

            btnPlayPause.Click += (s, e) =>
            {
                previewControl.TogglePlayPause();
                btnPlayPause.Text = previewControl.IsPlaying ? "❚❚ Pause" : "▶ Play";
            };

            btnRestart.Click += (s, e) => previewControl.Restart();

            // Timeline scrubbing. User drags the slider -> set the preview's
            // CurrentTime. Guard the frame-rate divide via the preview's
            // Duration property (it returns 0 for a 0 fps file rather than
            // dividing by zero).
            trackTime.Scroll += (s, e) =>
            {
                if (previewControl != null)
                {
                    float duration = previewControl.Duration;
                    previewControl.CurrentTime = (trackTime.Value / 1000f) * duration;
                    previewControl.InvalidatePreview();
                }
            };

            // Keep the time label and slider in sync with playback. The
            // preview control raises TimeChanged on every tick, scrub, restart
            // and load; without this handler the label would sit at
            // "0.00 / 0.00 s" and the slider thumb would never move, because
            // nothing else updates them as the animation runs.
            //
            // Setting trackTime.Value here does NOT re-enter the scrub handler
            // above: TrackBar.Scroll fires only on real user input, not on
            // programmatic Value assignment, so there's no feedback loop to
            // guard against.
            previewControl.TimeChanged += (s, e) =>
            {
                if (previewControl == null) return;

                float cur = previewControl.CurrentTime;
                float dur = previewControl.Duration;

                lblTime.Text = string.Format("{0:0.00} / {1:0.00} s", cur, dur);

                int v = 0;
                if (dur > 0f)
                    v = (int)Math.Round((cur / dur) * trackTime.Maximum);
                // Clamp into the trackbar's range — floating-point error and
                // the playback loop's modulo wrap can both land a hair outside.
                if (v < trackTime.Minimum) v = trackTime.Minimum;
                if (v > trackTime.Maximum) v = trackTime.Maximum;
                trackTime.Value = v;
            };

            // Layout of the preview tab, top to bottom:
            //   controlBar  (Dock=Top)  — Play / Restart / Export / trackbar,
            //                             flowing + wrapping as before.
            //   bottomRow   (Dock=Top)  — time readout (left) + speed (right).
            //   previewControl (Fill)   — the skeleton drawing.
            //
            // bottomRow is its OWN Dock=Top panel rather than another item
            // inside the FlowLayoutPanel, for two reasons:
            //   1) A FlowLayoutPanel only packs left-to-right — it can't push
            //      the speed control to the right edge. A plain Panel can:
            //      dock the time label Left, dock the speed group Right.
            //   2) Because bottomRow is Dock=Top, docking gives it the full
            //      pane width automatically — no manual width tracking, and
            //      nothing to get wrong during the initial layout pass.
            var bottomRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38
            };

            // Time readout docks to the left edge of the row.
            lblTime.Dock = DockStyle.Left;
            lblTime.TextAlign = ContentAlignment.MiddleLeft;

            // Speed label + dropdown live together in a right-docked
            // FlowLayoutPanel so they travel as a unit and hug the right edge
            // of the pane — roughly under the Export button above — and stay
            // there when the window is resized. The right padding keeps them a
            // few px clear of the very edge.
            var speedGroup = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 4, 8, 4)
            };
            lblSpeed.Margin = new Padding(0, 3, 0, 3); // snug inside the speed group
            speedGroup.Controls.Add(lblSpeed);
            speedGroup.Controls.Add(cmbSpeed);

            bottomRow.Controls.Add(lblTime);
            bottomRow.Controls.Add(speedGroup);

            // Add order matters for Dock=Top stacking: the LAST control added
            // ends up on top (closest to the pane's top edge). We want, top to
            // bottom: controlBar, then bottomRow, then the Fill preview. So add
            // the Fill control first, then bottomRow, then controlBar last.
            controlBar.Controls.Add(btnPlayPause);
            controlBar.Controls.Add(btnRestart);
            controlBar.Controls.Add(btnExport);
            controlBar.Controls.Add(trackTime);

            previewPanel.Controls.Add(previewControl); // Fill — added first
            previewPanel.Controls.Add(bottomRow);      // Dock=Top, below controlBar
            previewPanel.Controls.Add(controlBar);     // Dock=Top, on top
            previewTab.Controls.Add(previewPanel);

            rightTabControl.TabPages.Add(previewTab);
            rightTabControl.TabPages.Add(dataTab);

            splitContainer1.Panel2.Controls.Clear();
            splitContainer1.Panel2.Controls.Add(rightTabControl);

            previewControl.LoadAnimation(internalFile, VanillaPsuSkeleton.Create());

            // Nothing is selected in the tree yet, so the Data tab would
            // otherwise be an empty white box. Show a hint explaining how to
            // populate it instead.
            ShowDataTabPlaceholder();
        }

        /// <summary>
        /// Placeholder shown in the Data tab when no tree node is selected.
        /// Without this the tab is just a blank box on load (and again every
        /// time the selection is cleared), which reads as "broken" rather than
        /// "waiting for input". Explains both what to click and what each kind
        /// of node shows.
        /// </summary>
        private void ShowDataTabPlaceholder()
        {
            var sb = new StringBuilder();
            sb.AppendLine("No bone selected.");
            sb.AppendLine();
            sb.AppendLine("Select a bone in the tree on the left to view its");
            sb.AppendLine("properties here:");
            sb.AppendLine();
            sb.AppendLine("  \u2022  Click a bone name for a per-bone summary");
            sb.AppendLine("     (key counts per channel and animated frame range).");
            sb.AppendLine();
            sb.AppendLine("  \u2022  Expand a bone and click one of its channels");
            sb.AppendLine("     (Rotation / X / Y / Z Position Frames) for the");
            sb.AppendLine("     full keyframe data dump.");
            sb.AppendLine();
            sb.AppendLine("Tip: bones marked \u25CF are animated; bones marked");
            sb.AppendLine("\u25CB (greyed out) have no animation data.");

            frameDataTextBox.Text = sb.ToString();
        }

        /// <summary>
        /// Walk the four per-bone frame lists once and tally key counts plus
        /// animated frame range. Reset markers and terminator frames are
        /// excluded so the counts match what would actually be exported.
        /// </summary>
        private static BoneStats[] ComputeBoneStats(NomFile nom)
        {
            int n = nom.boneNames.Length;
            var result = new BoneStats[n];
            for (int i = 0; i < n; i++) result[i] = new BoneStats();

            for (int i = 0; i < n; i++)
            {
                var rot = i < nom.rotationFrameList.Count ? nom.rotationFrameList[i] : null;
                var px = i < nom.xPositionFrameList.Count ? nom.xPositionFrameList[i] : null;
                var py = i < nom.yPositionFrameList.Count ? nom.yPositionFrameList[i] : null;
                var pz = i < nom.zPositionFrameList.Count ? nom.zPositionFrameList[i] : null;

                CountKeys(rot, nom.frameCount, ref result[i].RotationKeys, result[i], rotationKeyTypes: true);
                CountKeys(px, nom.frameCount, ref result[i].XKeys, result[i], rotationKeyTypes: false);
                CountKeys(py, nom.frameCount, ref result[i].YKeys, result[i], rotationKeyTypes: false);
                CountKeys(pz, nom.frameCount, ref result[i].ZKeys, result[i], rotationKeyTypes: false);
            }
            return result;
        }

        private static void CountKeys(List<NomFile.NomFrame> frames, ushort frameCount,
                                      ref int counter, BoneStats stats, bool rotationKeyTypes)
        {
            if (frames == null) return;
            foreach (var nf in frames)
            {
                if (nf.frame >= frameCount) continue;  // terminator
                bool isRealKey;
                if (rotationKeyTypes)
                {
                    // Rotation key types that produce an actual animation key:
                    //   0x0       full quaternion (4 values)
                    //   0x5/6/7   partial quaternion (single axis + W, 2 values)
                    //   0x8..0xB  "hold" — repeat the previous quaternion
                    // All of these are real keys the exporter emits. The
                    // partial and hold types in particular are heavily used
                    // by leg bones, so missing them here would wrongly show
                    // animated legs as "static" in the tree.
                    isRealKey = (nf.type == 0x0 && nf.data.Count >= 4)
                             || (nf.type >= 0x5 && nf.type <= 0x7)
                             || (nf.type >= 0x8 && nf.type <= 0xB);
                }
                else
                {
                    // Position key types that produce an actual key:
                    //   0x0       direct value
                    //   0x2       4-value key (we use the first component)
                    //   0x4       interpolated key (we use the target value)
                    //   0x6       3-value key (we use the first component)
                    //   0x8/9/A   "hold" — repeat the previous value
                    isRealKey = (nf.type == 0x0 && nf.data.Count >= 1)
                             || (nf.type == 0x2 && nf.data.Count >= 1)
                             || (nf.type == 0x4 && nf.data.Count >= 1)
                             || (nf.type == 0x6 && nf.data.Count >= 1)
                             || (nf.type >= 0x8 && nf.type <= 0xA);
                }
                if (!isRealKey) continue;

                counter++;
                if (nf.frame < stats.MinFrame) stats.MinFrame = nf.frame;
                if (nf.frame > stats.MaxFrame) stats.MaxFrame = nf.frame;
            }
        }

        /// <summary>
        /// Fill the readonly summary labels at the top of the viewer with
        /// at-a-glance info about the loaded NOM.
        /// </summary>
        private void PopulateSummaryHeader()
        {
            int animatedBones = boneStats.Count(b => b.HasAnyAnimation);
            int staticBones = boneStats.Length - animatedBones;
            float durationSec = internalFile.frameRate > 0
                ? internalFile.frameCount / internalFile.frameRate
                : 0;

            fileNameLabel.Text = string.IsNullOrEmpty(internalFile.filename)
                ? "(unnamed NOM)"
                : internalFile.filename;

            summaryLabel.Text = string.Format(
                "{0} bones ({1} animated, {2} static)   \u2022   {3} frames @ {4:0.##} fps   \u2022   Duration: {5:0.000} s",
                boneStats.Length, animatedBones, staticBones,
                internalFile.frameCount, internalFile.frameRate, durationSec);
        }

        /// <summary>
        /// Build the bone tree. Each bone gets an indicator prefix:
        ///   \u25CF = animated (has at least one key on any channel)
        ///   \u25CB = static (no animation data)
        /// Static bones are also greyed out so they're visually de-emphasized.
        /// </summary>
        private void PopulateBoneTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            for (int i = 0; i < internalFile.boneNames.Length; i++)
            {
                var stats = boneStats[i];
                string indicator = stats.HasAnyAnimation ? "\u25CF" : "\u25CB";
                TreeNode node = new TreeNode(indicator + " (" + i + ") " + internalFile.boneNames[i]);
                node.Tag = i;

                if (!stats.HasAnyAnimation)
                {
                    node.ForeColor = SystemColors.GrayText;
                }

                // Child nodes for each channel that has data.
                if (internalFile.rotationFrameList[i] != null)
                    node.Nodes.Add(new TreeNode("Rotation Frames"));
                if (internalFile.xPositionFrameList[i] != null)
                    node.Nodes.Add(new TreeNode("X Position Frames"));
                if (internalFile.yPositionFrameList[i] != null)
                    node.Nodes.Add(new TreeNode("Y Position Frames"));
                if (internalFile.zPositionFrameList[i] != null)
                    node.Nodes.Add(new TreeNode("Z Position Frames"));

                treeView1.Nodes.Add(node);
            }
            treeView1.EndUpdate();

            AutoSizeTreePanel();
        }

        /// <summary>
        /// Sizes the splitter so the tree panel is exactly wide enough to show
        /// the longest node label without horizontal scrolling, leaving the
        /// rest of the width for the frame-data / preview panel on the right.
        ///
        /// A SplitContainer doesn't do this on its own — it just keeps a fixed
        /// SplitterDistance. So we measure every node's rendered text width
        /// (top-level bone nodes AND their channel children, since an expanded
        /// child like "Rotation Frames" can be the widest thing on screen),
        /// add the per-node chrome — indent, expand glyph, a little padding —
        /// and set SplitterDistance to the widest result.
        ///
        /// The result is clamped: never narrower than a usable minimum, and
        /// never so wide it leaves the right panel too small. If the control
        /// hasn't been sized yet (Width == 0 during construction) the clamp
        /// uses the designer width as a fallback.
        /// </summary>
        private void AutoSizeTreePanel()
        {
            if (treeView1.Nodes.Count == 0) return;

            // Per-level horizontal cost: WinForms indents child nodes, and each
            // node also reserves space for the expand [+]/[-] glyph. Indent is
            // the TreeView's own Indent value; the glyph is roughly 20 px.
            int indent = treeView1.Indent;
            const int glyphAndPadding = 28; // expand glyph + left/right breathing room

            int widest = 0;
            foreach (TreeNode bone in treeView1.Nodes)
            {
                // Top-level bone node (depth 0).
                int boneWidth = TextRenderer.MeasureText(bone.Text, treeView1.Font).Width
                                + glyphAndPadding;
                if (boneWidth > widest) widest = boneWidth;

                // Channel children (depth 1) — measured with one extra indent.
                foreach (TreeNode child in bone.Nodes)
                {
                    int childWidth = TextRenderer.MeasureText(child.Text, treeView1.Font).Width
                                     + glyphAndPadding + indent;
                    if (childWidth > widest) widest = childWidth;
                }
            }

            // Clamp. Use the live control width when available, otherwise the
            // designer size, so construction-time calls still get a sane cap.
            int available = splitContainer1.Width > 0
                ? splitContainer1.Width
                : this.Width;

            const int minTreeWidth = 120;
            // Leave at least this much for the right-hand (preview / data)
            // panel. Sized so the preview tab's control bar can fit its three
            // action buttons — Play (~90) + Restart (~90) + Export (~170),
            // plus button margins and the bar's own padding — on a single row
            // at the normal window size, instead of wrapping Export below.
            // If the bone names are long enough that auto-fit WOULD push past
            // this, the tree is capped here and the names scroll instead.
            const int minRightWidth = 400;
            int maxTreeWidth = Math.Max(minTreeWidth, available - minRightWidth);

            int target = Math.Max(minTreeWidth, Math.Min(widest, maxTreeWidth));

            // SplitterDistance throws if it's out of range for the current
            // size; guard so a freshly-constructed (still tiny) control can't
            // crash here.
            try
            {
                splitContainer1.SplitterDistance = target;
            }
            catch (InvalidOperationException)
            {
                // Control not laid out yet — the Resize handler below will
                // retry once it has a real size.
            }
        }

        /// <summary>
        /// Tree selection handler. Three cases:
        ///   1) A channel sub-node (e.g. "Rotation Frames") -> frame data dump
        ///   2) A bone node (top-level)                     -> per-bone summary
        ///   3) Anything else                               -> clear
        /// </summary>
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            frameDataTextBox.Clear();
            if (e.Node == null)
            {
                ShowDataTabPlaceholder();
                return;
            }

            // Case 1: channel sub-node. Its parent is a bone node carrying
            // the bone index as its Tag.
            List<List<NomFile.NomFrame>> nomFrames = null;
            switch (e.Node.Text)
            {
                case "Rotation Frames": nomFrames = internalFile.rotationFrameList; break;
                case "X Position Frames": nomFrames = internalFile.xPositionFrameList; break;
                case "Y Position Frames": nomFrames = internalFile.yPositionFrameList; break;
                case "Z Position Frames": nomFrames = internalFile.zPositionFrameList; break;
            }

            if (nomFrames != null && e.Node.Parent != null)
            {
                ShowFrameDataDump(nomFrames, (int)e.Node.Parent.Tag, e.Node.Text);
                return;
            }

            // Case 2: bone node — its Tag is the bone index.
            if (e.Node.Tag is int)
            {
                ShowBoneSummary((int)e.Node.Tag);
                return;
            }

            // Case 3: anything else (shouldn't normally happen) — fall back to
            // the placeholder rather than leaving the box blank.
            ShowDataTabPlaceholder();
        }

        /// <summary>
        /// Per-bone summary: key counts per channel, animated frame range,
        /// hierarchical context. Shown when the user clicks a bone (not a
        /// channel sub-node). Gives a quick "what does this bone do" view
        /// without having to drill in.
        /// </summary>
        private void ShowBoneSummary(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= boneStats.Length) return;
            var s = boneStats[boneIndex];
            string name = internalFile.boneNames[boneIndex];

            var sb = new StringBuilder();
            sb.AppendLine("Bone (" + boneIndex + ") " + name);
            sb.AppendLine(new string('-', 40));
            sb.AppendLine();

            if (!s.HasAnyAnimation)
            {
                sb.AppendLine("This bone has no animation data.");
                sb.AppendLine("It will be exported at the rest pose, unchanged");
                sb.AppendLine("for the duration of the take.");
                frameDataTextBox.Text = sb.ToString();
                return;
            }

            sb.AppendLine("Animation channels:");
            sb.AppendLine(string.Format("  Rotation:    {0,4} key{1}", s.RotationKeys, s.RotationKeys == 1 ? "" : "s"));
            sb.AppendLine(string.Format("  Position X:  {0,4} key{1}", s.XKeys, s.XKeys == 1 ? "" : "s"));
            sb.AppendLine(string.Format("  Position Y:  {0,4} key{1}", s.YKeys, s.YKeys == 1 ? "" : "s"));
            sb.AppendLine(string.Format("  Position Z:  {0,4} key{1}", s.ZKeys, s.ZKeys == 1 ? "" : "s"));
            sb.AppendLine();
            sb.AppendLine("Animated frame range: " + s.MinFrame + " - " + s.MaxFrame);
            if (internalFile.frameRate > 0)
            {
                float startSec = s.MinFrame / internalFile.frameRate;
                float endSec = s.MaxFrame / internalFile.frameRate;
                sb.AppendLine(string.Format("                       ({0:0.000} s - {1:0.000} s)", startSec, endSec));
            }
            sb.AppendLine();
            sb.AppendLine("Click a channel below to view its keyframe data.");

            frameDataTextBox.Text = sb.ToString();
        }

        /// <summary>
        /// Tabular frame data dump for a single channel of a single bone.
        /// Replaces the original concatenated-string display with column-
        /// aligned output that's easier to scan when there are many keys.
        ///
        /// We use the original NomFrame fields (frame, type, type2, data,
        /// filePosition) but format them in fixed-width columns. The frame
        /// data text box uses a monospaced font (set in the designer) so
        /// alignment holds.
        /// </summary>
        private void ShowFrameDataDump(List<List<NomFile.NomFrame>> nomFrames, int boneIndex, string channelLabel)
        {
            if (boneIndex < 0 || boneIndex >= nomFrames.Count) return;
            var frames = nomFrames[boneIndex];
            if (frames == null)
            {
                frameDataTextBox.Text = "(no data on this channel)";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(channelLabel + " - Bone (" + boneIndex + ") " + internalFile.boneNames[boneIndex]);
            sb.AppendLine(frames.Count + " frame entr" + (frames.Count == 1 ? "y" : "ies"));
            sb.AppendLine(new string('-', 70));
            sb.AppendLine(string.Format("{0,4}  {1,5}  {2,-9}  {3,-32}  {4,8}",
                "#", "Frame", "Type", "Values", "Offset"));
            sb.AppendLine();

            for (int i = 0; i < frames.Count; i++)
            {
                var nf = frames[i];
                string typeStr = nf.type.ToString("X") + "/" + nf.type2.ToString("X");
                string valStr;
                if (nf.data.Count == 0)
                {
                    valStr = "(none)";
                }
                else
                {
                    valStr = string.Join(", ", nf.data.Select(v => v.ToString("0.###")));
                    if (valStr.Length > 30) valStr = valStr.Substring(0, 27) + "...";
                }

                sb.AppendLine(string.Format("{0,4}  {1,5}  {2,-9}  {3,-32}  {4:X8}",
                    i, nf.frame, typeStr, valStr, nf.filePosition));
            }

            frameDataTextBox.Text = sb.ToString();
        }

        /// <summary>
        /// "Export Animation to GLB..." button handler. Asks the user which
        /// skeleton to use for the rest pose, then prompts for an output path
        /// and runs the export.
        ///
        /// Skeleton choices:
        ///   - Vanilla PSU player skeleton: a hardcoded bone table baked into
        ///     PSULib (VanillaPsuSkeleton). No file needed; this is the default
        ///     and works for any standard PSU player animation.
        ///   - A user-selected XNJ file: for non-standard skeletons, or when
        ///     the user wants an exact match to a specific character's rig.
        ///   - No skeleton: the flat synthetic fallback (all bones at origin).
        ///     Kept as an escape hatch but rarely what anyone wants now that
        ///     the vanilla skeleton exists.
        ///
        /// We use GLB (glTF binary) rather than FBX because Blender's FBX
        /// importer requires *binary* FBX (no official spec, nontrivial to
        /// write from scratch), while every modern DCC tool imports GLB
        /// cleanly. Users who specifically need an FBX can open the GLB in
        /// Blender and re-export to FBX in one click.
        /// </summary>
        private void exportGlbButton_Click(object sender, EventArgs e)
        {
            if (internalFile == null) return;

            // Ask which skeleton to use. Returns null if the user cancelled.
            SkeletonChoice choice = AskSkeletonChoice();
            if (choice == SkeletonChoice.Cancelled) return;

            // Resolve the choice into an actual XnjFile (or null for flat).
            XnjFile skeleton = null;
            switch (choice)
            {
                case SkeletonChoice.Vanilla:
                    // Baked-in standard PSU player skeleton — no file needed.
                    skeleton = VanillaPsuSkeleton.Create();
                    break;

                case SkeletonChoice.PickFile:
                    skeleton = PromptForXnjSkeleton();
                    if (skeleton == null) return; // user cancelled / parse failed
                    break;

                case SkeletonChoice.None:
                    skeleton = null; // flat synthetic fallback
                    break;
            }

            // Now pick the output path. Default to the NOM's name with .glb.
            string defaultName = "animation.glb";
            if (!string.IsNullOrEmpty(internalFile.filename))
            {
                defaultName = Path.GetFileNameWithoutExtension(internalFile.filename) + ".glb";
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export NOM animation";
                dlg.Filter = "glTF Binary (*.glb)|*.glb|All files (*.*)|*.*";
                dlg.FileName = defaultName;
                dlg.OverwritePrompt = true;
                dlg.AddExtension = true;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    UseWaitCursor = true;
                    Application.DoEvents();
                    string written = NomGlbExporter.Export(internalFile, dlg.FileName, skeleton);

                    // Build a context-aware confirmation message.
                    string note;
                    if (choice == SkeletonChoice.Vanilla)
                        note = "Exported with the standard PSU player skeleton.";
                    else if (choice == SkeletonChoice.PickFile)
                        note = "Exported with the rest pose from the selected XNJ skeleton.";
                    else
                        note = "Note: exported with a flat synthetic skeleton (all bones " +
                               "at origin). For a proper rest pose, re-export using the " +
                               "standard PSU skeleton or an XNJ file.";

                    MessageBox.Show(this,
                        "Exported to:\n" + written + "\n\n" + note,
                        "Export Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Export failed:\n\n" + ex.Message,
                        "Export Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    UseWaitCursor = false;
                }
            }
        }

        /// <summary>
        /// The three skeleton options the export prompt offers, plus a
        /// "cancelled" sentinel.
        /// </summary>
        private enum SkeletonChoice
        {
            Cancelled,
            Vanilla,
            PickFile,
            None,
        }

        /// <summary>
        /// Show a small modal dialog asking which skeleton to use for export.
        /// Built programmatically rather than as a Designer form — it's three
        /// radio buttons and two command buttons, not worth a .Designer.cs.
        ///
        /// The vanilla option is pre-selected: it's the right answer for the
        /// overwhelming majority of exports (standard PSU player animations),
        /// so the common path is "click Export, press Enter, done".
        /// </summary>
        private SkeletonChoice AskSkeletonChoice()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Skeleton for Export";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(420, 210);
                dlg.ShowInTaskbar = false;

                var prompt = new Label
                {
                    Text = "Which skeleton should the exported animation use for its rest pose?",
                    Location = new Point(12, 12),
                    Size = new Size(396, 32),
                };

                var rbVanilla = new RadioButton
                {
                    Text = "Standard PSU player skeleton (recommended)",
                    Location = new Point(20, 50),
                    Size = new Size(380, 22),
                    Checked = true,
                };
                var vanillaHint = new Label
                {
                    Text = "Built-in. Works for any standard PSU player animation.",
                    Location = new Point(40, 70),
                    Size = new Size(370, 16),
                    ForeColor = SystemColors.GrayText,
                };

                var rbPick = new RadioButton
                {
                    Text = "Select a different XNJ skeleton file...",
                    Location = new Point(20, 92),
                    Size = new Size(380, 22),
                };
                var pickHint = new Label
                {
                    Text = "For non-standard skeletons or an exact character-rig match.",
                    Location = new Point(40, 112),
                    Size = new Size(370, 16),
                    ForeColor = SystemColors.GrayText,
                };

                var rbNone = new RadioButton
                {
                    Text = "No skeleton (flat — all bones at origin)",
                    Location = new Point(20, 134),
                    Size = new Size(380, 22),
                };

                var okButton = new Button
                {
                    Text = "Export",
                    DialogResult = DialogResult.OK,
                    Location = new Point(244, 172),
                    Size = new Size(80, 26),
                };
                var cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(330, 172),
                    Size = new Size(80, 26),
                };

                dlg.Controls.Add(prompt);
                dlg.Controls.Add(rbVanilla);
                dlg.Controls.Add(vanillaHint);
                dlg.Controls.Add(rbPick);
                dlg.Controls.Add(pickHint);
                dlg.Controls.Add(rbNone);
                dlg.Controls.Add(okButton);
                dlg.Controls.Add(cancelButton);
                dlg.AcceptButton = okButton;
                dlg.CancelButton = cancelButton;

                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return SkeletonChoice.Cancelled;

                if (rbVanilla.Checked) return SkeletonChoice.Vanilla;
                if (rbPick.Checked) return SkeletonChoice.PickFile;
                return SkeletonChoice.None;
            }
        }

        /// <summary>
        /// Prompt the user to choose an XNJ file and parse it. Returns null
        /// if the user cancels the picker or the parse fails (in which case
        /// we've already shown an error message).
        ///
        /// We parse via the PsuFile constructor pattern even though we're
        /// reading a standalone file from disk — XnjFile doesn't currently
        /// have a stream-only path, and replicating one would duplicate the
        /// NXOB-locating logic. The empty header and pointer arrays are fine
        /// because XnjFile.ReconstructFile handles those defensively.
        /// </summary>
        private XnjFile PromptForXnjSkeleton()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select PSU skeleton (XNJ) file";
                dlg.Filter = "PSU Skeleton (*.xnj)|*.xnj|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;

                byte[] raw;
                try
                {
                    raw = File.ReadAllBytes(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Could not read the file:\n\n" + ex.Message,
                        "XNJ Load Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return null;
                }

                var xnj = new XnjFile(Path.GetFileName(dlg.FileName), raw,
                                      new byte[0], new int[0], 0);
                if (!string.IsNullOrEmpty(xnj.ParseError))
                {
                    MessageBox.Show(this,
                        "This file doesn't look like a valid PSU XNJ skeleton:\n\n" +
                        xnj.ParseError,
                        "XNJ Parse Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return null;
                }
                if (xnj.Bones.Count != internalFile.boneNames.Length)
                {
                    var result = MessageBox.Show(this,
                        "The selected XNJ has " + xnj.Bones.Count + " bones, but " +
                        "this NOM expects " + internalFile.boneNames.Length + ". " +
                        "The export will continue without the skeleton (flat fallback).",
                        "Bone Count Mismatch",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);
                    if (result == DialogResult.Cancel) return null;
                    return null; // bone count mismatch → don't pass the skeleton
                }
                return xnj;
            }
        }
    }
}