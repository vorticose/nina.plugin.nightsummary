using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock ISafetyMonitorMediator for replay testing.
    /// Night Summary registers as a consumer and receives UpdateDeviceInfo callbacks.
    /// </summary>
    internal class MockSafetyMonitorMediator : ISafetyMonitorMediator {

        private readonly List<ISafetyMonitorConsumer> _consumers = new();

        // ── Used by Night Summary ────────────────────────────────────────────

        public void RegisterConsumer(ISafetyMonitorConsumer consumer) {
            _consumers.Add(consumer);
        }

        public void RemoveConsumer(ISafetyMonitorConsumer consumer) {
            _consumers.Remove(consumer);
        }

        /// <summary>Pushes a safety state change to all registered consumers.</summary>
        public void PushSafetyState(bool isSafe) {
            var info = new SafetyMonitorInfo { IsSafe = isSafe };
            foreach (var consumer in _consumers)
                consumer.UpdateDeviceInfo(info);
        }

        // ── Not used by Night Summary ────────────────────────────────────────
        public event EventHandler<IsSafeEventArgs> IsSafeChanged;
        public event Func<object, EventArgs, Task> Connected;
        public event Func<object, EventArgs, Task> Disconnected;

        public void RegisterHandler(ISafetyMonitorVM handler) { }
        public Task<IList<string>> Rescan() => throw new NotImplementedException();
        public Task<bool> Connect() => throw new NotImplementedException();
        public Task Disconnect() => throw new NotImplementedException();
        public void Broadcast(SafetyMonitorInfo deviceInfo) { }
        public SafetyMonitorInfo GetInfo() => new SafetyMonitorInfo();
        public string Action(string actionName, string actionParameters) => throw new NotImplementedException();
        public string SendCommandString(string command, bool raw = true) => throw new NotImplementedException();
        public bool SendCommandBool(string command, bool raw = true) => throw new NotImplementedException();
        public void SendCommandBlind(string command, bool raw = true) => throw new NotImplementedException();
        public IDevice GetDevice() => throw new NotImplementedException();
    }
}
