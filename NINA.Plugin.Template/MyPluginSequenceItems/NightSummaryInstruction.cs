using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Session;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Plugin.NightSummary.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Night Summary - Start Session")]
    [ExportMetadata("Description", "Starts recording imaging session data for the Night Summary report")]
    [ExportMetadata("Category", "Night Summary")]
    [ExportMetadata("Icon", "NightSummary_StartIcon")]
    public class NightSummaryInstruction : SequenceItem {

        private readonly SessionService sessionService;

        [ImportingConstructor]
        public NightSummaryInstruction(SessionService sessionService) {
            this.sessionService = sessionService;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                Logger.Info("NightSummary: Start Session instruction executing");
                sessionService.StartSession(null);
                progress?.Report(new ApplicationStatus() {
                    Status = "Night Summary: Session started - recording imaging data"
                });
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to start session. {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new NightSummaryInstruction(sessionService);
        }

        public override string ToString() {
            return "Night Summary - Start Session";
        }
    }
}