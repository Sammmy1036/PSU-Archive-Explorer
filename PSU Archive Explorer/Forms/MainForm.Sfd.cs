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
        // ---------------------------------------------------------------------
        // SFD preview helpers
        // ---------------------------------------------------------------------

        // 2 GB ceiling for in-panel SFD preview. Set conservatively
        // demuxer produces two more in memory buffers (video ES + ADX)
        // on top of the source. If the app is x86 or lacks
        // <gcAllowVeryLargeObjects> in App.config, you'll OOM well before this.
        // The SfdPreviewPanel catches OutOfMemoryException and shows a status
        // message rather than crashing.
        private const long MAX_SFD_PREVIEW_SIZE = 2L * 1024L * 1024L * 1024L; // 2 GB

        /// <summary>
        /// Load an archive embedded .sfd into the right panel.
        /// </summary>
        private void LoadSfdIntoRightPanel(ContainerFile parent, int index, string displayName, TreeNode node)
        {
            try
            {
                RawFile raw = parent.getFileRaw(index);
                byte[] bytes = raw?.fileContents;
                if (bytes == null || bytes.Length == 0)
                {
                    bytes = raw?.WriteToBytes(false);
                }

                if (bytes == null || bytes.Length == 0)
                {
                    ShowSfdError("Could not read SFD data from archive.");
                    return;
                }

                // Make sure currentRight points at the parsed file so the
                // right-click ▸ Extract Selected (and Save As, viewInHex, etc.)
                // can find it. Without this, exportSelected() silently bails
                // because it guards on `currentRight != null`. We mirror the
                // pattern used elsewhere in treeView1_AfterSelect.
                try { currentRight = parent.getFileParsed(index); }
                catch (Exception ex) { Console.WriteLine("getFileParsed failed for SFD: " + ex); }

                var panel = new SfdPreviewPanel { Dock = DockStyle.Fill };
                splitContainer1.Panel2.Controls.Add(panel);
                panel.LoadSfd(bytes, displayName);
            }
            catch (OutOfMemoryException)
            {
                ShowSfdError("Out of memory — this SFD is too large to preview in the current build.");
            }
            catch (Exception ex)
            {
                ShowSfdError("Could not preview SFD: " + ex.Message);
            }
        }

        /// <summary>
        /// Load a standalone .sfd from disk into the right panel.
        /// </summary>
        private void LoadSfdIntoRightPanelFromFile(string path, TreeNode node)
        {
            try
            {
                // Build a synthetic UnpointeredFile so right-click ▸ Extract
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    byte[] header = new byte[4];
                    if (data.Length >= 4) Array.Copy(data, 0, header, 0, 4);
                    currentRight = new UnpointeredFile(Path.GetFileName(path), data, header);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to capture standalone SFD bytes for currentRight: " + ex);
                    currentRight = null;
                }

                var panel = new SfdPreviewPanel { Dock = DockStyle.Fill };
                splitContainer1.Panel2.Controls.Add(panel);
                panel.LoadSfdFromFile(path);
            }
            catch (OutOfMemoryException)
            {
                ShowSfdError("Out of memory — this SFD is too large to preview in the current build.");
            }
            catch (Exception ex)
            {
                ShowSfdError("Could not preview SFD: " + ex.Message);
            }
        }

        private void ShowSfdError(string message)
        {
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
                Text = message + "\n\nYou can right-click the file and choose Extract Selected " +
                       "to save it, then open it with VLC Media Player."
            };
            warningPanel.Controls.Add(lbl);
            splitContainer1.Panel2.Controls.Add(warningPanel);
        }
    }
}