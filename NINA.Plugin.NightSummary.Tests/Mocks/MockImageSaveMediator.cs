using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock IImageSaveMediator for replay testing.
    /// Night Summary only uses the ImageSaved event.
    /// </summary>
    internal class MockImageSaveMediator : IImageSaveMediator {

        // ── Used by Night Summary ────────────────────────────────────────────
        public event EventHandler<ImageSavedEventArgs> ImageSaved;

        public void FireImageSaved(ImageSavedEventArgs args) {
            ImageSaved?.Invoke(this, args);
        }

        // ── Not used by Night Summary ────────────────────────────────────────
        public event Func<object, BeforeImageSavedEventArgs, Task> BeforeImageSaved;
        public event Func<object, BeforeFinalizeImageSavedEventArgs, Task> BeforeFinalizeImageSaved;

        public Task Enqueue(IImageData imageData, Task<IRenderedImage> prepareTask,
            IProgress<ApplicationStatus> progress, CancellationToken token)
            => throw new NotImplementedException();

        public void Shutdown() { }
        public void RegisterHandler(IImageSaveController handler) { }
    }
}
