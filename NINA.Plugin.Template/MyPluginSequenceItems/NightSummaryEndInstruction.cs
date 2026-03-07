using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Session;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Night Summary End")]
    [ExportMetadata("Description", "Ends the Night Summary session and sends the email report")]
    [ExportMetadata("Category", "Night Summary")]
    [ExportMetadata("Icon", "NightSummary_EndIcon")]
    public class NightSummaryEndInstruction : SequenceItem {

        private readonly SessionService sessionService;

        [ImportingConstructor]
        public NightSummaryEndInstruction(SessionService sessionService) {
            this.sessionService = sessionService;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                Logger.Info("NightSummary: End Session instruction executing");

                progress?.Report(new ApplicationStatus() {
                    Status = "Night Summary: Waiting for images to save..."
                });

                // Wait for NINA to finish saving any pending images
                await Task.Delay(TimeSpan.FromSeconds(15), token);

                sessionService.EndSession();

                Logger.Info("NightSummary: Session ended successfully");

                progress?.Report(new ApplicationStatus() {
                    Status = "Night Summary: Session complete"
                });

            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to end session. {ex.Message}");
            }
        }

        public override object Clone() {
            return new NightSummaryEndInstruction(sessionService) { Icon = this.Icon, Name = this.Name };
        }

        public override string ToString() {
            return "Night Summary - End Session";
        }
    }
}