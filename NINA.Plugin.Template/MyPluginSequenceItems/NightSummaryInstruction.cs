using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Sequencer {

    /// <summary>
    /// A NINA sequencer instruction that starts a Night Summary session
    /// when executed, and ends it when the sequence completes or is cancelled.
    /// 
    /// The user adds this instruction to the START of their sequence.
    /// At sequence end, add a second instance to trigger the report.
    /// </summary>
    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Night Summary - Start Session")]
    [ExportMetadata("Description", "Starts recording imaging session data for the Night Summary report")]
    [ExportMetadata("Icon", "NINA.Plugin.NightSummary.Resources.NightSummaryIcon")]
    [ExportMetadata("Category", "Night Summary")]
    public class NightSummaryInstruction : SequenceItem {

        private readonly NightSummaryPlugin plugin;

        [ImportingConstructor]
        public NightSummaryInstruction(NightSummaryPlugin plugin) {
            this.plugin = plugin;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                Logger.Info("NightSummary: Start Session instruction executing");
                plugin.StartSession();
                progress?.Report(new ApplicationStatus() {
                    Status = "Night Summary: Session started - recording imaging data"
                });
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to start session. {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public override object Clone() {
            return new NightSummaryInstruction(plugin);
        }

        public override string ToString() {
            return "Night Summary - Start Session";
        }
    }
}