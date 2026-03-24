using Microsoft.Web.WebView2.Core;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Plugin.NightSummary {

    public partial class PreviewWindow : Window {

        private readonly SessionService sessionService;
        private ReportData cachedReportData;
        private bool isLoading;

        // Data source entries
        private readonly List<PreviewSource> sources = new List<PreviewSource>();

        public PreviewWindow(SessionService sessionService) {
            InitializeComponent();
            this.sessionService = sessionService;

            Loaded += async (s, e) => {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins", CoreUtil.Version, "NightSummary", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await PreviewWebView.EnsureCoreWebView2Async(env);
                PopulateDataSources();
            };
        }

        private void PopulateDataSources() {
            sources.Clear();

            // Live sessions
            var liveDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Plugins", CoreUtil.Version, "NightSummary", "nightsummary.sqlite");

            if (File.Exists(liveDbPath)) {
                try {
                    var db = new SessionDatabase(liveDbPath);
                    var sessions = db.GetAllSessions();
                    foreach (var s in sessions.Take(20)) {
                        var imageCount = db.GetImagesForSession(s.SessionId).Count;
                        sources.Add(new PreviewSource {
                            Label = $"{s.SessionStart:yyyy-MM-dd HH:mm} — {s.ProfileName} ({imageCount} images)",
                            DbPath = liveDbPath,
                            SessionId = s.SessionId
                        });
                    }
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Preview — could not read live database. {ex.Message}");
                }
            }

            // Test data
            var testDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Plugins", CoreUtil.Version, "NightSummary", "test", "nightsummary.sqlite");

            if (File.Exists(testDbPath)) {
                sources.Add(new PreviewSource {
                    Label = "Test Data (seeded database)",
                    DbPath = testDbPath,
                    SessionId = null
                });
            }

            DataSourceCombo.ItemsSource = sources;
            DataSourceCombo.DisplayMemberPath = "Label";

            if (sources.Count > 0) {
                DataSourceCombo.SelectedIndex = 0;
            } else {
                StatusText.Text = "No session data available";
            }
        }

        private async void DataSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (DataSourceCombo.SelectedItem is PreviewSource source) {
                await LoadFullPreview(source);
            }
        }

        private async void UpdatePreview_Click(object sender, RoutedEventArgs e) {
            if (cachedReportData == null) {
                // No cached data — do a full load from current selection
                if (DataSourceCombo.SelectedItem is PreviewSource source)
                    await LoadFullPreview(source);
                return;
            }

            if (isLoading) return;
            isLoading = true;
            StatusText.Text = "Updating preview...";

            try {
                var html = await sessionService.GenerateHtmlAsync(cachedReportData);
                PreviewWebView.NavigateToString(html);
                StatusText.Text = $"{cachedReportData.Images.Count} images — {cachedReportData.Session.SessionStart:yyyy-MM-dd}";
            } catch (Exception ex) {
                StatusText.Text = $"Error: {ex.Message}";
            } finally {
                isLoading = false;
            }
        }

        private async Task LoadFullPreview(PreviewSource source) {
            if (isLoading) return;
            isLoading = true;
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Loading session data...";

            try {
                cachedReportData = await Task.Run(async () =>
                    await sessionService.BuildReportDataAsync(source.DbPath, source.SessionId));

                if (cachedReportData == null) {
                    StatusText.Text = "No session data found";
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    isLoading = false;
                    return;
                }

                StatusText.Text = "Rendering report...";
                var html = await sessionService.GenerateHtmlAsync(cachedReportData);
                PreviewWebView.NavigateToString(html);
                StatusText.Text = $"{cachedReportData.Images.Count} images — {cachedReportData.Session.SessionStart:yyyy-MM-dd}";
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Preview generation failed. {ex.Message}");
                StatusText.Text = $"Error: {ex.Message}";
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                isLoading = false;
            }
        }

        private class PreviewSource {
            public string Label { get; set; }
            public string DbPath { get; set; }
            public string SessionId { get; set; }
        }
    }
}
