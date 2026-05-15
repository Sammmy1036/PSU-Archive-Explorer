using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using PSULib.FileClasses.Models;

namespace psu_archive_explorer.FileViewers
{
    /// <summary>
    /// Tree-based viewer for PSU .xnj (NN-format skeleton) files.
    ///
    /// Layout: a SplitContainer with the bone hierarchy in a TreeView on the
    /// left and a property/value grid on the right that fills in when a bone
    /// is selected. A status strip across the top reports the bone count or
    /// any parse error.
    ///
    /// Fallback behaviour: if the file produced no bones (parse error, or an
    /// unrecognised XNJ variant), the viewer does NOT go blank. It shows the
    /// error in the status strip and drops a read-only hex dump of the raw
    /// file into the right-hand panel so the file is still inspectable —
    /// closer to what the generic PointeredFileViewer would have shown.
    ///
    /// The XNJ format stores hierarchy three redundant ways per bone
    /// (ParentIndex, FirstChildIndex, NextSiblingIndex). We build the tree
    /// off ParentIndex because it is the simplest to reason about and
    /// naturally handles PSU skeletons having several legitimate roots
    /// (Body plus weapon attach points all have ParentIndex == -1).
    /// </summary>
    public partial class XnjFileViewer : UserControl
    {
        public XnjFile loadedFile;

        public XnjFileViewer(XnjFile xnj)
        {
            InitializeComponent();
            loadedFile = xnj;

            treeView1.AfterSelect += treeView1_AfterSelect;

            PopulateTree();
        }

        /// <summary>
        /// Builds the bone hierarchy in treeView1. Each TreeNode's Tag holds
        /// the XnjBone it represents so the AfterSelect handler can populate
        /// the properties grid without re-walking anything.
        ///
        /// When there are no bones to show, hands off to ShowFallback() so
        /// the panel still displays something useful.
        /// </summary>
        private void PopulateTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            if (loadedFile == null)
            {
                treeView1.EndUpdate();
                ShowFallback("No file loaded.");
                return;
            }

            var bones = loadedFile.Bones;
            bool hasBones = bones != null && bones.Count > 0;

            if (!hasBones)
            {
                treeView1.EndUpdate();
                string reason = !string.IsNullOrEmpty(loadedFile.ParseError)
                    ? loadedFile.ParseError
                    : "File parsed but contains no bone records.";
                ShowFallback(reason);
                return;
            }

            statusLabel.ForeColor = SystemColors.ControlText;
            statusLabel.Text = "Skeleton: " + bones.Count + " bone"
                + (bones.Count == 1 ? "" : "s")
                + "   (file: " + (loadedFile.filename ?? "<unnamed>") + ")";

            // One TreeNode per bone, indexed by bone index so we can wire up
            // parent/child relationships in a second pass.
            TreeNode[] nodes = new TreeNode[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                TreeNode node = new TreeNode(MakeNodeLabel(bone));
                node.Tag = bone;
                nodes[i] = node;
            }

            // Second pass: attach each node to its parent, or to the tree
            // root if ParentIndex is -1 (or points somewhere invalid, which
            // we treat as a root so nothing silently disappears).
            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                int p = bone.ParentIndex;
                if (p >= 0 && p < nodes.Length && p != i)
                {
                    nodes[p].Nodes.Add(nodes[i]);
                }
                else
                {
                    treeView1.Nodes.Add(nodes[i]);
                }
            }

            treeView1.ExpandAll();
            if (treeView1.Nodes.Count > 0)
            {
                treeView1.SelectedNode = treeView1.Nodes[0];
            }

            treeView1.EndUpdate();
        }

        /// <summary>
        /// Called when there are no bones to display. Puts the reason in the
        /// status strip (red) and, if we have raw bytes to show, drops a
        /// read-only hex dump into the properties-grid panel so the file is
        /// still inspectable instead of the panel being blank.
        ///
        /// We reuse the existing right-hand panel (mainSplit.Panel2) rather
        /// than adding new designer controls: clear the grid out of it and
        /// put a RichTextBox in its place.
        /// </summary>
        private void ShowFallback(string reason)
        {
            statusLabel.ForeColor = Color.Firebrick;
            statusLabel.Text = "XNJ not shown as a skeleton: " + reason;

            // Left tree gets a single explanatory node so the pane isn't empty.
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add(new TreeNode("(no bone hierarchy available)"));
            treeView1.EndUpdate();

            byte[] raw = TryGetRawBytes();

            // Swap the properties grid out of the right panel for a hex view.
            mainSplit.Panel2.Controls.Clear();

            var hexBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                WordWrap = false,
                BackColor = Color.White
            };

            if (raw == null || raw.Length == 0)
            {
                hexBox.Text =
                    "This XNJ could not be parsed as a skeleton, and no raw "
                    + "bytes were available to display.\r\n\r\n"
                    + "Reason: " + reason;
            }
            else
            {
                hexBox.Text =
                    "This XNJ could not be parsed as a skeleton.\r\n"
                    + "Reason: " + reason + "\r\n"
                    + "Showing the raw file contents (" + raw.Length
                    + " bytes) below.\r\n"
                    + new string('-', 60) + "\r\n"
                    + BuildHexDump(raw);
            }

            mainSplit.Panel2.Controls.Add(hexBox);
        }

        /// <summary>
        /// Best-effort retrieval of the file's raw bytes for the fallback hex
        /// view. XnjFile.ToRaw() currently returns null (the format is
        /// parse-only), so we fall back to the header bytes the base PsuFile
        /// holds. Wrapped in try/catch because we never want the fallback
        /// path itself to throw.
        /// </summary>
        private byte[] TryGetRawBytes()
        {
            if (loadedFile == null) return null;
            try
            {
                byte[] fromToRaw = loadedFile.ToRaw();
                if (fromToRaw != null && fromToRaw.Length > 0)
                    return fromToRaw;
            }
            catch
            {
                // ignore — fall through to header
            }
            try
            {
                return loadedFile.header;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Classic 16-bytes-per-row hex dump: offset, hex columns, ASCII
        /// gutter. Capped so an unexpectedly huge file can't lock the UI
        /// building one giant string.
        /// </summary>
        private static string BuildHexDump(byte[] data)
        {
            const int bytesPerRow = 16;
            const int maxBytes = 64 * 1024; // 64 KB cap on the dump

            int count = Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder(count * 4);

            for (int rowStart = 0; rowStart < count; rowStart += bytesPerRow)
            {
                sb.Append(rowStart.ToString("X8"));
                sb.Append("  ");

                // Hex columns.
                for (int col = 0; col < bytesPerRow; col++)
                {
                    int idx = rowStart + col;
                    if (idx < count)
                        sb.Append(data[idx].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");
                    if (col == 7) sb.Append(' '); // gap between the two halves
                }

                sb.Append(' ');

                // ASCII gutter.
                for (int col = 0; col < bytesPerRow; col++)
                {
                    int idx = rowStart + col;
                    if (idx >= count) break;
                    byte b = data[idx];
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }

                sb.Append("\r\n");
            }

            if (data.Length > maxBytes)
            {
                sb.Append(new string('-', 60)).Append("\r\n");
                sb.Append("... dump truncated at ").Append(maxBytes)
                  .Append(" bytes (file is ").Append(data.Length)
                  .Append(" bytes total).\r\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Short label shown in the tree: index, the bone "kind" guessed from
        /// whether it has children, and whether it participates in skinning.
        /// Numeric detail is left to the properties grid.
        /// </summary>
        private static string MakeNodeLabel(XnjBone bone)
        {
            string kind;
            if (bone.ParentIndex < 0)
                kind = "root";
            else if (bone.FirstChildIndex < 0)
                kind = "leaf";
            else
                kind = "joint";

            string label = "[" + bone.Index + "] " + kind;
            if (bone.WeightUsed >= 0)
                label += "  (weight slot " + bone.WeightUsed + ")";
            return label;
        }

        /// <summary>
        /// Fills the properties grid with every parsed field of the selected
        /// bone, grouped the way the XnjFile header documents the NN_NODE
        /// struct: hierarchy, translation, rotation, scale, inverse bind matrix.
        /// </summary>
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // In the fallback case the grid has been replaced by a hex box;
            // there's nothing to populate and propertiesGrid isn't in the
            // panel anymore. Guard against both.
            if (propertiesGrid == null || propertiesGrid.IsDisposed)
                return;

            propertiesGrid.Rows.Clear();

            XnjBone bone = e.Node != null ? e.Node.Tag as XnjBone : null;
            if (bone == null)
                return;

            AddRow("Bone index", bone.Index.ToString());
            AddRow("Parent index", FormatIndex(bone.ParentIndex));
            AddRow("First child index", FormatIndex(bone.FirstChildIndex));
            AddRow("Next sibling index", FormatIndex(bone.NextSiblingIndex));
            AddRow("Weight slot (skinning)",
                bone.WeightUsed >= 0 ? bone.WeightUsed.ToString() : "-1 (unused)");

            AddSeparator("Local Translation");
            AddRow("X", F(bone.LocalTranslationX));
            AddRow("Y", F(bone.LocalTranslationY));
            AddRow("Z", F(bone.LocalTranslationZ));

            AddSeparator("Local Rotation  (Euler, XZY order)");
            AddRow("X", F(bone.LocalRotationDegX) + " deg   (" + F(bone.LocalRotationRadX) + " rad)");
            AddRow("Y", F(bone.LocalRotationDegY) + " deg   (" + F(bone.LocalRotationRadY) + " rad)");
            AddRow("Z", F(bone.LocalRotationDegZ) + " deg   (" + F(bone.LocalRotationRadZ) + " rad)");

            AddSeparator("Local Scale");
            AddRow("X", F(bone.LocalScaleX));
            AddRow("Y", F(bone.LocalScaleY));
            AddRow("Z", F(bone.LocalScaleZ));

            AddSeparator("Inverse Bind Matrix  (row-major)");
            if (bone.InverseBindMatrix != null && bone.InverseBindMatrix.Length == 16)
            {
                for (int row = 0; row < 4; row++)
                {
                    AddRow("Row " + row,
                        F(bone.InverseBindMatrix[row * 4 + 0]) + "   " +
                        F(bone.InverseBindMatrix[row * 4 + 1]) + "   " +
                        F(bone.InverseBindMatrix[row * 4 + 2]) + "   " +
                        F(bone.InverseBindMatrix[row * 4 + 3]));
                }
            }
            else
            {
                AddRow("(matrix)", "not available");
            }
        }

        private void AddRow(string property, string value)
        {
            propertiesGrid.Rows.Add(property, value);
        }

        /// <summary>
        /// Adds a visually distinct group-header row. The grid is read-only
        /// so we just style a normal row to act as a section divider.
        /// </summary>
        private void AddSeparator(string title)
        {
            int idx = propertiesGrid.Rows.Add(title, "");
            DataGridViewRow row = propertiesGrid.Rows[idx];
            row.DefaultCellStyle.BackColor = SystemColors.ControlLight;
            row.DefaultCellStyle.Font = new Font(propertiesGrid.Font, FontStyle.Bold);
        }

        private static string FormatIndex(int index)
        {
            return index < 0 ? "-1 (none)" : index.ToString();
        }

        /// <summary>Consistent float formatting for the grid.</summary>
        private static string F(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}