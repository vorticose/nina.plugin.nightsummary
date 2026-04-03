using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace NINA.Plugin.SessionCapture {

    [Export(typeof(IPluginManifest))]
    public class SessionCapturePlugin : PluginBase {

        [ImportingConstructor]
        public SessionCapturePlugin() {
            Logger.Info("SessionCapture: Plugin loaded");
        }

        public override Task Teardown() {
            Logger.Info("SessionCapture: Plugin teardown");
            return base.Teardown();
        }
    }
}
