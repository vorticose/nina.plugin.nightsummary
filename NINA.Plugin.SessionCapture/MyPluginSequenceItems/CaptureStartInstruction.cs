using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.SessionCapture.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Session Capture Start")]
    [ExportMetadata("Description", "Starts recording NINA mediator events for replay testing")]
    [ExportMetadata("Category", "Session Capture")]
    [ExportMetadata("Icon", "RecordSVG")]
    public class CaptureStartInstruction : SequenceItem {

        private readonly CaptureService captureService;

        [ImportingConstructor]
        public CaptureStartInstruction(CaptureService captureService) {
            this.captureService = captureService;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                Logger.Info("SessionCapture: Start Capture instruction executing");
                captureService.StartCapture();
                progress?.Report(new ApplicationStatus {
                    Status = "Session Capture: Recording started"
                });
            } catch (Exception ex) {
                Logger.Error($"SessionCapture: Failed to start capture. {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new CaptureStartInstruction(captureService) { Icon = this.Icon, Name = this.Name };
        }

        public override string ToString() {
            return "Session Capture - Start Recording";
        }
    }
}
