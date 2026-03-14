using Accord.Statistics.Kernels;
using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.NightSummary.Sequencer {
    [Export(typeof(ResourceDictionary))]
    public partial class PluginItemTemplate : ResourceDictionary {
        public PluginItemTemplate() {
            InitializeComponent();
        }
    }
}