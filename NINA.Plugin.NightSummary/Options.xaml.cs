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

        private void InsertPattern_Click(object sender, RoutedEventArgs e) {
            var button = sender as Button;
            var pattern = button?.Tag as string;
            if (string.IsNullOrEmpty(pattern)) return;

            // Find the pattern TextBox by name
            var textBox = FindPatternTextBox(button);
            if (textBox == null) return;

            // If user hasn't clicked into the textbox, append to end
            var caretIndex = textBox.IsFocused ? textBox.CaretIndex : textBox.Text.Length;
            textBox.Text = textBox.Text.Insert(caretIndex, pattern);
            textBox.CaretIndex = caretIndex + pattern.Length;
            textBox.Focus();
        }

        private TextBox FindPatternTextBox(DependencyObject start) {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(start);
            while (parent != null) {
                if (parent is StackPanel sp) {
                    // Look for sibling StackPanels that contain the named TextBox
                    var container = System.Windows.Media.VisualTreeHelper.GetParent(sp);
                    if (container != null) {
                        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(container); i++) {
                            var child = System.Windows.Media.VisualTreeHelper.GetChild(container, i);
                            var found = FindChild<TextBox>(child, "FilePatternTextBox");
                            if (found != null) return found;
                        }
                    }
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private static T FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name) return t;
                var result = FindChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void BrowseSaveReportPath_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFolderDialog {
                Title = "Select folder for saved reports"
            };

            var currentPath = Data.SettingsManager.Instance.Current.SaveReportPath;
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
