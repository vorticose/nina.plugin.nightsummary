using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Night Summary - Start Session")]
    [ExportMetadata("Description", "Starts recording imaging session data for the Night Summary report")]
    [ExportMetadata("Category", "Night Summary")]
    public class NightSummaryInstruction : SequenceItem {

        [ImportingConstructor]
        public NightSummaryInstruction() {
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            Logger.Info("NightSummary: Start Session instruction executing");
            progress?.Report(new ApplicationStatus() {
                Status = "Night Summary: Session started"
            });
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new NightSummaryInstruction();
        }

        public override string ToString() {
            return "Night Summary - Start Session";
        }
    }
}