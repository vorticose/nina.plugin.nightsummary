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

        private void AddChart_Click(object sender, RoutedEventArgs e) {
            var plugin = (sender as Button)?.DataContext as NightSummaryPlugin;
            plugin?.AddAdditionalChart();
        }

        private void RemoveChart_Click(object sender, RoutedEventArgs e) {
            var button = sender as Button;
            var config = button?.DataContext as ChartConfig;
            if (config == null) return;
            // DataContext inside the DataTemplate is ChartConfig; walk up to find NightSummaryPlugin
            var element = System.Windows.Media.VisualTreeHelper.GetParent(button) as System.Windows.DependencyObject;
            while (element != null) {
                if (element is System.Windows.FrameworkElement fe && fe.DataContext is NightSummaryPlugin plugin) {
                    plugin.RemoveAdditionalChart(config);
                    return;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
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
