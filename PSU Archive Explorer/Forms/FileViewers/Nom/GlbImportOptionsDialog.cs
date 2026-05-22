using System;
using System.Globalization;
using System.Windows.Forms;

namespace psu_archive_explorer.Forms.FileViewers
{
    /// <summary>
    /// Modal dialog shown during a GLB animation import. Lets the user choose
    /// the frame rate the imported NOM should use.
    ///
    /// glTF animation is keyed in seconds and carries no frame-rate field, so
    /// the importer needs a rate from somewhere:
    ///   1) Original — reuse the frame rate of the NOM being replaced. The safe
    ///                 default; in practice every PSU animation is 30 fps.
    ///   2) Manual   — the user types an exact rate.
    ///
    /// LAYOUT NOTES (two WinForms gotchas this version is built to avoid)
    /// ------------------------------------------------------------------
    ///  * Radio mutual-exclusion is per DIRECT PARENT. Both radio buttons must
    ///    share one parent container or they won't exclude each other (you'd be
    ///    able to check both). They are therefore both children of `body`.
    ///  * AutoSize on a form + absolutely-positioned buttons in a sub-panel is
    ///    unreliable — the form can finish sizing before the button row is
    ///    measured and clip it off. So this dialog uses a FIXED ClientSize,
    ///    computed once below, rather than AutoSize.
    ///
    /// The info label still auto-sizes its own height (it wraps to the dialog
    /// width), and the fixed form height includes generous room for it; if the
    /// label text is made much longer, bump INFO_RESERVED_HEIGHT to match.
    /// </summary>
    public sealed class GlbImportOptionsDialog : Form
    {
        private readonly RadioButton _optOriginal;
        private readonly RadioButton _optManual;
        private readonly NumericUpDown _manualValue;

        /// <summary>The frame rate the user settled on. Valid after OK.</summary>
        public float ChosenFrameRate { get; private set; }

        // Layout constants — all positions derive from these.
        private const int FormWidth = 420;
        private const int Margin = 14;
        private const int ContentWidth = FormWidth - 2 * Margin; // 392
        private const int InfoReservedHeight = 64;  // vertical space for the label
        private const int RowHeight = 28;
        private const int ButtonWidth = 84;
        private const int ButtonHeight = 30;

        /// <param name="originalRate">The frame rate of the NOM being replaced.</param>
        public GlbImportOptionsDialog(float originalRate)
        {
            Text = "Import Animation";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            string rateText =
                originalRate.ToString("0.###", CultureInfo.InvariantCulture);

            // Running Y cursor — each control is placed at `y`, then y advances.
            int y = Margin;

            // --- info label ---------------------------------------------
            // AutoSize so it wraps to ContentWidth and takes the height it
            // needs. We reserve INFO_RESERVED_HEIGHT of layout space for it.
            var info = new Label
            {
                Text = "Choose the frame rate the imported NOM should play at.\n\n" +
                       "Most PSU animations are 30 fps, and keeping the original rate is\n" +
                       "recommended.",
                Location = new System.Drawing.Point(Margin, y),
                MaximumSize = new System.Drawing.Size(ContentWidth, 0),
                AutoSize = true,
            };
            y += InfoReservedHeight;

            // --- the two radios share ONE parent (this form) so they form a
            //     single mutually-exclusive group --------------------------
            _optOriginal = new RadioButton
            {
                Text = "Keep the original animation's rate (" + rateText +
                       " fps) (recommended)",
                Location = new System.Drawing.Point(Margin, y),
                Size = new System.Drawing.Size(ContentWidth, RowHeight),
                Checked = true,
            };
            y += RowHeight;

            _optManual = new RadioButton
            {
                Text = "Enter a frame rate manually:",
                Location = new System.Drawing.Point(Margin, y),
                Size = new System.Drawing.Size(200, RowHeight),
            };
            _manualValue = new NumericUpDown
            {
                Location = new System.Drawing.Point(Margin + 206, y + 2),
                Width = 86,
                DecimalPlaces = 2,
                Minimum = 1m,
                Maximum = 240m,
                Value = (decimal)Clamp(originalRate, 1f, 240f),
                Enabled = false,
            };
            _optManual.CheckedChanged += (s, e) =>
                _manualValue.Enabled = _optManual.Checked;
            y += RowHeight + 12;

            // --- buttons, right-aligned ---------------------------------
            var ok = new Button
            {
                Text = "Import",
                DialogResult = DialogResult.OK,
                Size = new System.Drawing.Size(ButtonWidth, ButtonHeight),
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new System.Drawing.Size(ButtonWidth, ButtonHeight),
            };
            cancel.Location = new System.Drawing.Point(
                FormWidth - Margin - ButtonWidth, y);
            ok.Location = new System.Drawing.Point(
                FormWidth - Margin - ButtonWidth - 8 - ButtonWidth, y);
            y += ButtonHeight + Margin;

            ok.Click += (s, e) =>
            {
                if (_optManual.Checked)
                    ChosenFrameRate = (float)_manualValue.Value;
                else
                    ChosenFrameRate = originalRate;
            };

            // Fixed size — y is now the exact total height needed.
            ClientSize = new System.Drawing.Size(FormWidth, y);

            // All controls are children of the form itself, so the two radios
            // share a parent and exclude each other correctly.
            Controls.Add(info);
            Controls.Add(_optOriginal);
            Controls.Add(_optManual);
            Controls.Add(_manualValue);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}