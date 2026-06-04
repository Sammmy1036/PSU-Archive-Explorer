using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainForm : Form
    {
        // ====================== System Tray Icon ======================

        private NotifyIcon _trayIcon;
        private static Mutex _trayOwnerMutex;

        private void InitTrayIcon()
        {
            // Only the first process creates the tray icon.
            _trayOwnerMutex = new Mutex(true, "PSUArchiveExplorer_TrayOwner", out bool createdNew);
            if (!createdNew)
            {
                _trayOwnerMutex.Dispose();
                _trayOwnerMutex = null;
                return;
            }

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("New Session", null, TrayNewSession_Click);
            trayMenu.Items.Add("Show All Windows", null, TrayShowAll_Click);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Close All Windows", null, TrayCloseAll_Click);

            _trayIcon = new NotifyIcon
            {
                Text = "PSU Archive Explorer",
                Icon = this.Icon,
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            _trayIcon.Click += (s, e) =>
            {
                if (((MouseEventArgs)e).Button == MouseButtons.Left)
                    TrayShowAll_Click(s, e);
            };

            _trayIcon.DoubleClick += (s, e) => TrayShowAll_Click(s, e);

            // When this (owner) process exits, release the mutex so the next
            // process can become tray owner if needed.
            this.FormClosed += (s, e) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayOwnerMutex?.ReleaseMutex();
                _trayOwnerMutex?.Dispose();
                _trayOwnerMutex = null;
            };
        }

        /// <summary>
        /// Returns all running PSU Archive Explorer processes including this one.
        /// </summary>
        private static Process[] GetAllInstances()
        {
            string exeName = Process.GetCurrentProcess().ProcessName;
            return Process.GetProcessesByName(exeName);
        }

        private void TrayShowAll_Click(object sender, EventArgs e)
        {
            foreach (var proc in GetAllInstances())
            {
                try
                {
                    // Restore the window if minimised then bring it to the foreground.
                    IntPtr hwnd = proc.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    NativeMethods.ShowWindow(hwnd, SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hwnd);
                }
                catch { /* process may have just exited */ }
            }
        }

        private void TrayNewSession_Click(object sender, EventArgs e)
        {
            openNewSessionToolStripMenuItem_Click(sender, e);
        }

        private void TrayCloseAll_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Close all open PSU Archive Explorer windows?",
                "Close All",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            // Kill every instance — including this one last so the tray icon
            // is cleaned up by our FormClosed handler first.
            int currentId = Process.GetCurrentProcess().Id;
            foreach (var proc in GetAllInstances())
            {
                try
                {
                    if (proc.Id != currentId)
                        proc.CloseMainWindow(); // graceful close
                }
                catch { }
            }

            // Close this instance last (goes through normal shutdown path).
            Application.Exit();
        }

        private const int SW_RESTORE = 9;
    }
}