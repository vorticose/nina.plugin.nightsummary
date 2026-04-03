using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock IFocuserMediator for replay testing.
    /// Night Summary registers as a consumer and receives UpdateEndAutoFocusRun callbacks.
    /// </summary>
    internal class MockFocuserMediator : IFocuserMediator {

        private readonly List<IFocuserConsumer> _consumers = new();

        // ── Used by Night Summary ────────────────────────────────────────────

        public void RegisterConsumer(IFocuserConsumer consumer) {
            _consumers.Add(consumer);
        }

        public void RemoveConsumer(IFocuserConsumer consumer) {
            _consumers.Remove(consumer);
        }

        /// <summary>Pushes an autofocus completion event to all registered consumers.</summary>
        public void FireAutoFocusComplete(AutoFocusInfo info) {
            foreach (var consumer in _consumers)
                consumer.UpdateEndAutoFocusRun(info);
        }

        // ── Not used by Night Summary ────────────────────────────────────────
        public event Func<object, EventArgs, Task> Connected;
        public event Func<object, EventArgs, Task> Disconnected;

        public void RegisterHandler(IFocuserVM handler) { }
        public Task<IList<string>> Rescan() => throw new NotImplementedException();
        public Task<bool> Connect() => throw new NotImplementedException();
        public Task Disconnect() => throw new NotImplementedException();
        public void Broadcast(FocuserInfo deviceInfo) { }
        public FocuserInfo GetInfo() => new FocuserInfo();
        public string Action(string actionName, string actionParameters) => throw new NotImplementedException();
        public string SendCommandString(string command, bool raw = true) => throw new NotImplementedException();
        public bool SendCommandBool(string command, bool raw = true) => throw new NotImplementedException();
        public void SendCommandBlind(string command, bool raw = true) => throw new NotImplementedException();
        public IDevice GetDevice() => throw new NotImplementedException();
        public void ToggleTempComp(bool tempComp) { }
        public Task<int> MoveFocuser(int position, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> MoveFocuserRelative(int position, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> MoveFocuserByTemperatureRelative(double temperature, double Slope, CancellationToken ct) => throw new NotImplementedException();
        public void BroadcastSuccessfulAutoFocusRun(AutoFocusInfo info) { }
        public void BroadcastNewAutoFocusPoint(DataPoint dataPoint) { }
        public void BroadcastUserFocused(FocuserInfo info) { }
        public void BroadcastAutoFocusRunStarting() { }
    }
}
