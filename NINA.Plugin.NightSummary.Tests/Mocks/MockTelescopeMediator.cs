using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock ITelescopeMediator for replay testing.
    /// Night Summary only subscribes to the AfterMeridianFlip event.
    /// </summary>
    internal class MockTelescopeMediator : ITelescopeMediator {

        // ── Used by Night Summary ────────────────────────────────────────────
        public event Func<object, AfterMeridianFlipEventArgs, Task> AfterMeridianFlip;

        public async Task FireMeridianFlip(bool success, double raHours = 0, double decDeg = 0) {
            if (AfterMeridianFlip != null) {
                var coords = new Coordinates(raHours, decDeg, Epoch.J2000, Coordinates.RAType.Hours);
                var args = new AfterMeridianFlipEventArgs(success, coords);
                await AfterMeridianFlip.Invoke(this, args);
            }
        }

        // ── Not used by Night Summary ────────────────────────────────────────
        public event Func<object, BeforeMeridianFlipEventArgs, Task> BeforeMeridianFlip;
        public event Func<object, EventArgs, Task> Connected;
        public event Func<object, EventArgs, Task> Disconnected;
        public event Func<object, EventArgs, Task> Parked;
        public event Func<object, EventArgs, Task> Homed;
        public event Func<object, EventArgs, Task> Unparked;
        public event Func<object, MountSlewedEventArgs, Task> Slewed;

        public void RegisterHandler(ITelescopeVM handler) { }
        public void RegisterConsumer(ITelescopeConsumer consumer) { }
        public void RemoveConsumer(ITelescopeConsumer consumer) { }
        public Task<IList<string>> Rescan() => throw new NotImplementedException();
        public Task<bool> Connect() => throw new NotImplementedException();
        public Task Disconnect() => throw new NotImplementedException();
        public void Broadcast(TelescopeInfo deviceInfo) { }
        public TelescopeInfo GetInfo() => new TelescopeInfo();
        public string Action(string actionName, string actionParameters) => throw new NotImplementedException();
        public string SendCommandString(string command, bool raw = true) => throw new NotImplementedException();
        public bool SendCommandBool(string command, bool raw = true) => throw new NotImplementedException();
        public void SendCommandBlind(string command, bool raw = true) => throw new NotImplementedException();
        public IDevice GetDevice() => throw new NotImplementedException();
        public void MoveAxis(TelescopeAxes axis, double rate) { }
        public void PulseGuide(GuideDirections direction, int duration) { }
        public Task<bool> Sync(Coordinates coordinates) => throw new NotImplementedException();
        public Task<bool> SlewToCoordinatesAsync(Coordinates coords, CancellationToken token) => throw new NotImplementedException();
        public Task<bool> SlewToCoordinatesAsync(TopocentricCoordinates coords, CancellationToken token) => throw new NotImplementedException();
        public Task<bool> SlewToTopocentricCoordinates(TopocentricCoordinates coords, CancellationToken token) => throw new NotImplementedException();
        public Task<bool> MeridianFlip(Coordinates targetCoordinates, CancellationToken token) => throw new NotImplementedException();
        public bool SetTrackingEnabled(bool trackingEnabled) => false;
        public bool SetTrackingMode(TrackingMode trackingMode) => false;
        public bool SetCustomTrackingRate(SiderealShiftTrackingRate rate) => false;
        public bool SendToSnapPort(bool start) => false;
        public Coordinates GetCurrentPosition() => new Coordinates(0, 0, Epoch.J2000, Coordinates.RAType.Hours);
        public Task<bool> ParkTelescope(IProgress<ApplicationStatus> progress, CancellationToken token) => throw new NotImplementedException();
        public Task<bool> UnparkTelescope(IProgress<ApplicationStatus> progress, CancellationToken token) => throw new NotImplementedException();
        public Task WaitForSlew(CancellationToken token) => throw new NotImplementedException();
        public Task<bool> FindHome(IProgress<ApplicationStatus> progress, CancellationToken token) => throw new NotImplementedException();
        public void StopSlew() { }
        public PierSide DestinationSideOfPier(Coordinates coordinates) => PierSide.pierUnknown;
        public Task RaiseBeforeMeridianFlip(BeforeMeridianFlipEventArgs e) => Task.CompletedTask;
        public Task RaiseAfterMeridianFlip(AfterMeridianFlipEventArgs e) => Task.CompletedTask;
    }
}
