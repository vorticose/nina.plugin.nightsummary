using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(ResourceDictionary))]
    partial class Options : ResourceDictionary {

        public Options() {
            InitializeComponent();
        }

        private void AddChart2_Click(object sender, RoutedEventArgs e) {
            var plugin = (sender as Button)?.DataContext as NightSummaryPlugin;
            if (plugin != null) plugin.ShowChart2 = true;
        }

        private void RemoveChart2_Click(object sender, RoutedEventArgs e) {
            var plugin = (sender as Button)?.DataContext as NightSummaryPlugin;
            if (plugin != null) plugin.ShowChart2 = false;
        }

        private void BrowseSaveReportPath_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFolderDialog {
                Title = "Select folder for saved reports"
            };

            var currentPath = MyPluginProperties.Settings.Default.SaveReportPath;
            if (!string.IsNullOrWhiteSpace(currentPath) && System.IO.Directory.Exists(currentPath)) {
                dialog.InitialDirectory = currentPath;
            }

            if (dialog.ShowDialog() == true) {
                var plugin = (sender as Button)?.DataContext as NightSummaryPlugin;
                if (plugin != null) {
                    plugin.SaveReportPath = dialog.FolderName;
                }
            }
        }
    }
}
