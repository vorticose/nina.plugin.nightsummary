using NINA.Astrometry.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.ViewModel.Sequencer;
using NINA.Sequencer.SequenceItem;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock ISequenceMediator for replay testing.
    /// Night Summary reads Initialized and polls GetAdvancedSequencerCurrentRunningItems().
    /// With Clock.DisableSkipPolling = true, the polling timer is suppressed,
    /// so this mock just needs to satisfy the interface contract.
    /// </summary>
    internal class MockSequenceMediator : ISequenceMediator {

        // ── Used by Night Summary ────────────────────────────────────────────
        public bool Initialized { get; set; } = true;

        public IReadOnlyCollection<ISequenceItem> GetAdvancedSequencerCurrentRunningItems()
            => Array.Empty<ISequenceItem>();

        // ── Not used by Night Summary ────────────────────────────────────────
        public event Func<object, EventArgs, Task> SequenceStarting;
        public event Func<object, EventArgs, Task> SequenceFinished;

        public void FireSequenceStarting() => SequenceStarting?.Invoke(this, EventArgs.Empty)?.Wait();
        public void FireSequenceFinished() => SequenceFinished?.Invoke(this, EventArgs.Empty)?.Wait();

        public IList<IDeepSkyObjectContainer> GetDeepSkyObjectContainerTemplates() => Array.Empty<IDeepSkyObjectContainer>();
        public void SetAdvancedSequence(ISequenceRootContainer container) { }
        public void AddAdvancedTarget(IDeepSkyObjectContainer container) { }
        public void AddSimpleTarget(IDeepSkyObject deepSkyObject) { }
        public void RegisterSequenceNavigation(ISequenceNavigationVM sequenceNavigation) { }
        public void SwitchToAdvancedView() { }
        public void SwitchToOverview() { }
        public void AddTargetToTargetList(IDeepSkyObjectContainer container) { }
        public IList<IDeepSkyObjectContainer> GetAllTargetsInAdvancedSequence() => Array.Empty<IDeepSkyObjectContainer>();
        public IList<IDeepSkyObjectContainer> GetAllTargetsInSimpleSequence() => Array.Empty<IDeepSkyObjectContainer>();
        public Task StartAdvancedSequence(bool skipValidation) => throw new NotImplementedException();
        public void CancelAdvancedSequence() { }
        public bool IsAdvancedSequenceRunning() => false;
        public Task SaveContainer(ISequenceContainer content, string filePath, CancellationToken token) => throw new NotImplementedException();
        public string GetAdvancedSequencerSavePath() => "";
    }
}
