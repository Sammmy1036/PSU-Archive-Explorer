using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                NativeLoader.EnsureLoaded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "NativeLoader Failed");
            }
            // Remove the ListManifestResources line now
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}