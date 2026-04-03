using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock ICameraMediator for replay testing.
    /// Night Summary only calls GetInfo() once at session start.
    /// </summary>
    internal class MockCameraMediator : ICameraMediator {

        // ── Used by Night Summary ────────────────────────────────────────────
        public CameraInfo ConfiguredInfo { get; set; } = new CameraInfo();

        public CameraInfo GetInfo() => ConfiguredInfo;

        // ── Not used by Night Summary ────────────────────────────────────────
        public event Func<object, EventArgs, Task> Connected;
        public event Func<object, EventArgs, Task> Disconnected;
        public event Func<object, EventArgs, Task> DownloadTimeout;

        public bool AtTargetTemp => false;
        public double TargetTemp => 0;

        public void RegisterHandler(ICameraVM handler) { }
        public void RegisterConsumer(ICameraConsumer consumer) { }
        public void RemoveConsumer(ICameraConsumer consumer) { }
        public Task<IList<string>> Rescan() => throw new NotImplementedException();
        public Task<bool> Connect() => throw new NotImplementedException();
        public Task Disconnect() => throw new NotImplementedException();
        public void Broadcast(CameraInfo deviceInfo) { }
        public string Action(string actionName, string actionParameters) => throw new NotImplementedException();
        public string SendCommandString(string command, bool raw = true) => throw new NotImplementedException();
        public bool SendCommandBool(string command, bool raw = true) => throw new NotImplementedException();
        public void SendCommandBlind(string command, bool raw = true) => throw new NotImplementedException();
        public IDevice GetDevice() => throw new NotImplementedException();
        public Task Capture(CaptureSequence sequence, CancellationToken token, IProgress<ApplicationStatus> progress) => throw new NotImplementedException();
        public IAsyncEnumerable<IExposureData> LiveView(CancellationToken token) => throw new NotImplementedException();
        public IAsyncEnumerable<IExposureData> LiveView(CaptureSequence sequence, CancellationToken token) => throw new NotImplementedException();
        public Task<IExposureData> Download(CancellationToken token) => throw new NotImplementedException();
        public void AbortExposure() { }
        public void SetReadoutMode(short mode) { }
        public void SetReadoutModeForNormalImages(short mode) { }
        public void SetBinning(short x, short y) { }
        public void SetDewHeater(bool onOff) { }
        public Task<bool> CoolCamera(double temperature, TimeSpan duration, IProgress<ApplicationStatus> progress, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> WarmCamera(TimeSpan duration, IProgress<ApplicationStatus> progress, CancellationToken ct) => throw new NotImplementedException();
        public void RegisterCaptureBlock(ICameraConsumer cameraConsumer) { }
        public void ReleaseCaptureBlock(ICameraConsumer cameraConsumer) { }
        public bool IsFreeToCapture(ICameraConsumer cameraConsumer) => true;
        public void RegisterCaptureBlock(object cameraConsumer) { }
        public void ReleaseCaptureBlock(object cameraConsumer) { }
        public bool IsFreeToCapture(object cameraConsumer) => true;
        public void SetUSBLimit(int usbLimit) { }
        public void SetSubSambleRectangle(ObservableRectangle observableRectangle) { }
    }
}
