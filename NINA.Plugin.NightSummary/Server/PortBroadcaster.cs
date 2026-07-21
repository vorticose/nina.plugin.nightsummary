using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.Interfaces;

namespace NINA.Plugin.NightSummary.Server;

// Announces the local dashboard server's port over NINA's inter-plugin message
// broker so other plugins (Touch 'N' Stars in particular) can discover it
// without user configuration. Mirrors the AdvancedAPI.Port /
// AdvancedAPI.RequestPort convention TNS already consumes:
//
//   - publishes topic "NightSummary.Port" whenever the server starts or stops
//     (content: the port as a string; "0" means installed but not running);
//   - answers topic "NightSummary.RequestPort" by re-publishing the current
//     value, so a consumer that starts after us can still discover the port.
internal sealed class PortBroadcaster : ISubscriber, IDisposable {
    public const string PortTopic    = "NightSummary.Port";
    public const string RequestTopic = "NightSummary.RequestPort";

    private readonly IMessageBroker broker;
    private readonly Guid senderId;
    private volatile int currentPort; // 0 = server not running

    public PortBroadcaster(IMessageBroker broker, Guid senderId) {
        this.broker   = broker ?? throw new ArgumentNullException(nameof(broker));
        this.senderId = senderId;
        broker.Subscribe(RequestTopic, this);
    }

    // Called on server start (with the bound port) and stop (with 0).
    public async Task AnnounceAsync(int port) {
        currentPort = port;
        await PublishCurrentAsync();
    }

    public async Task OnMessageReceived(IMessage message) {
        try {
            await PublishCurrentAsync();
        } catch (Exception ex) {
            Logger.Warning($"NightSummary: Port broadcast reply failed: {ex.Message}");
        }
    }

    private Task PublishCurrentAsync() =>
        broker.Publish(new PortMessage(senderId, PortTopic,
            currentPort.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public void Dispose() {
        try { broker.Unsubscribe(RequestTopic, this); } catch { }
    }

    private sealed class PortMessage : IMessage {
        public PortMessage(Guid senderId, string topic, string content) {
            SenderId = senderId;
            Topic    = topic;
            Content  = content;
        }

        public Guid SenderId { get; }
        public string Sender => "Night Summary";
        public DateTimeOffset SentAt => DateTimeOffset.UtcNow;
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset? Expiration => null;
        public Guid? CorrelationId { get; } = Guid.NewGuid();
        public int Version => 1;
        public IDictionary<string, object> CustomHeaders { get; } = new Dictionary<string, object>();
        public string Topic { get; }
        public object Content { get; }
    }
}
