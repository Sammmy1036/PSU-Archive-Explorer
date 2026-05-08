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
        // ====================== Welcome Screen (shown only on first launch) ======================
        private const string GitHubUrl = "https://github.com/Sammmy1036/PSU-Archive-Explorer/";

        // References so we can tear the welcome screen down cleanly when a file is loaded.
        private PictureBox welcomeLogoBox;
        private Panel welcomePanel;
        private bool welcomeVisible = false;

        /// <summary>
        /// Displays a logo in the left tree panel and a welcome message in the right panel.
        /// Only intended to be shown on first launch, before any archive has been loaded.
        /// The logo is inserted UNDERNEATH the existing tree/searchResults controls so that
        /// the search box and search results continue to function normally when the user
        /// starts typing, searchResults becomes visible and naturally covers the logo.
        /// </summary>
        private void ShowWelcomeScreen()
        {
            if (welcomeVisible) return;

            // ---- LEFT: logo occupies the empty tree region ----
            // The tree is empty at launch, so we hide it to let the logo show through.
            // searchResults is already hidden by default and will be shown by RunSearch
            // as soon as the user types a query (at which point it layers over the logo).
            treeView1.Visible = false;

            welcomeLogoBox = new PictureBox
            {
                Name = "welcomeLogoBox",
                Location = new Point(0, 26),
                Size = new Size(
                    splitContainer1.Panel1.ClientSize.Width,
                    splitContainer1.Panel1.ClientSize.Height - 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                       | AnchorStyles.Left | AnchorStyles.Right,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = splitContainer1.Panel1.BackColor,
            };

            // Prefer the embedded high-resolution logo resource. Fall back to the
            // form icon if the resource is missing (e.g. during early development
            // before the logo has been added to Properties/Resources).
            try
            {
                System.Drawing.Image logoImage = Properties.Resources.Logo;
                if (logoImage != null)
                {
                    welcomeLogoBox.Image = logoImage;
                }
                else if (this.Icon != null)
                {
                    welcomeLogoBox.Image = this.Icon.ToBitmap();
                }
            }
            catch
            {
                // If anything goes wrong loading the embedded resource, try the icon;
                // if that also fails, leave the PictureBox empty rather than crashing.
                try
                {
                    if (this.Icon != null)
                        welcomeLogoBox.Image = this.Icon.ToBitmap();
                }
                catch { }
            }

            splitContainer1.Panel1.Controls.Add(welcomeLogoBox);
            // Put the logo at the BACK of the z-order. searchBox / searchResults / treeView1
            // all need to be able to sit on top of it when they become visible.
            welcomeLogoBox.SendToBack();

            // Clicking the logo should also drop focus from the search box so
            // its placeholder is restored.
            welcomeLogoBox.MouseDown += DeadSpace_MouseDown;

            // ---- RIGHT: welcome message ----
            // Use a container Panel so we can dispose everything in one go via ClearRightPanel().
            welcomePanel = new Panel
            {
                Name = "welcomePanel",
                Dock = DockStyle.Fill,
                BackColor = splitContainer1.Panel2.BackColor,
                Padding = new Padding(30, 40, 30, 30),
            };

            var titleLabel = new Label
            {
                Text = "PSU Archive Explorer v1.0.0.2",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 40),
            };

            var checkReleaseLabel = new Label
            {
                Text = "Check GitHub for the latest release:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 100),
            };

            var githubLink = new LinkLabel
            {
                Text = GitHubUrl,
                Font = new Font("Segoe UI", 10F),
                AutoSize = true,
                Location = new Point(30, 125),
            };
            githubLink.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = GitHubUrl,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open link: " + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            var helpfulLabel = new Label
            {
                Text = "Getting started:\r\n\r\n"
                     + "• File ▸ Open or Ctrl + O to open a PSU archive\r\n\r\n"
                     + "• Use the tree on the left to browse the contents of the container\r\n\r\n"
                     + "• Use the search box above the tree panel on the left to find files by name or hash\r\n\r\n"
                     + "• Right click a file for extraction / replacement / renaming options\r\n\r\n"
                     + "• Extract ▸ Extract All In Folder to bulk extract a directory of archives\r\n\r\n"
                     + "Happy Modding!",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = SystemColors.ControlText,
                AutoSize = true,
                Location = new Point(30, 175),
            };

            welcomePanel.Controls.Add(titleLabel);
            welcomePanel.Controls.Add(checkReleaseLabel);
            welcomePanel.Controls.Add(githubLink);
            welcomePanel.Controls.Add(helpfulLabel);

            // Wire up MouseDown on the welcome panel and its non-interactive
            // labels so clicking any of them drops focus from the search box
            // and restores the "Search files..." placeholder. The GitHub link
            // is intentionally left out so clicks on it still open the URL.
            welcomePanel.MouseDown += DeadSpace_MouseDown;
            titleLabel.MouseDown += DeadSpace_MouseDown;
            checkReleaseLabel.MouseDown += DeadSpace_MouseDown;
            helpfulLabel.MouseDown += DeadSpace_MouseDown;

            splitContainer1.Panel2.Controls.Add(welcomePanel);

            welcomeVisible = true;

            // Move focus off the search box. Because treeView1 is hidden, the form's
            // default tab focus would otherwise land on searchBox, firing its Enter
            // event and clearing the "Search files..." placeholder before the user
            // has even clicked it.
            this.ActiveControl = null;
        }

        /// <summary>
        /// Removes the welcome screen and restores the normal tree/right-panel view.
        /// Safe to call even if the welcome screen is not currently visible.
        /// </summary>
        private void HideWelcomeScreen()
        {
            if (!welcomeVisible) return;

            if (welcomeLogoBox != null)
            {
                splitContainer1.Panel1.Controls.Remove(welcomeLogoBox);
                welcomeLogoBox.Image?.Dispose();
                welcomeLogoBox.Dispose();
                welcomeLogoBox = null;
            }
            treeView1.Visible = true;

            if (welcomePanel != null)
            {
                splitContainer1.Panel2.Controls.Remove(welcomePanel);
                welcomePanel.Dispose();
                welcomePanel = null;
            }

            welcomeVisible = false;
        }
    }
}