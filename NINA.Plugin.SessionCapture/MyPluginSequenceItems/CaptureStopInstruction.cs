using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.SessionCapture.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Session Capture Stop")]
    [ExportMetadata("Description", "Stops recording and saves the captured session to a JSON file")]
    [ExportMetadata("Category", "Session Capture")]
    [ExportMetadata("Icon", "StopSVG")]
    public class CaptureStopInstruction : SequenceItem {

        private readonly CaptureService captureService;

        [ImportingConstructor]
        public CaptureStopInstruction(CaptureService captureService) {
            this.captureService = captureService;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                Logger.Info("SessionCapture: Stop Capture instruction executing");
                captureService.StopCapture();
                progress?.Report(new ApplicationStatus {
                    Status = "Session Capture: Recording saved"
                });
            } catch (Exception ex) {
                Logger.Error($"SessionCapture: Failed to stop capture. {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new CaptureStopInstruction(captureService) { Icon = this.Icon, Name = this.Name };
        }

        public override string ToString() {
            return "Session Capture - Stop Recording";
        }
    }
}
