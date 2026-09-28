using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StreamingBridge
{
    // OBS WebSocket v5. All operations must be awaited sequentially by the caller.
    public sealed class ObsClient : IDisposable
    {
        private const int MaxMessageBytes = 1024 * 1024;
        private readonly Func<IObsTransport> transportFactory;
        private readonly int timeoutMilliseconds;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxMessageBytes };
        private IObsTransport transport;
        private bool identified;
        private bool disposed;

        public ObsClient() : this(delegate { return new ObsWebSocketTransport(); }, 8000) { }

        internal ObsClient(Func<IObsTransport> factory, int timeoutMilliseconds)
        {
            this.transportFactory = factory;
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        public bool IsConnected
        {
            get { return !disposed && identified && transport != null && transport.State == WebSocketState.Open; }
        }

        public async Task ConnectAsync(string host, int port, string password)
        {
            ThrowIfDisposed();
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter the OBS computer's IP address or hostname.", "host");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port", "The WebSocket port must be between 1 and 65535.");
            host = host.Trim();
            if (host.IndexOfAny(new[] { '/', '\\', '@', '?', '#' }) >= 0)
                throw new ArgumentException("Enter only the OBS computer's IP address or hostname, without a URL or port.", "host");
            Uri address;
            try { address = new UriBuilder("ws", host, port, "/").Uri; }
            catch (UriFormatException) { throw new ArgumentException("The OBS computer's IP address or hostname is invalid.", "host"); }

            ResetConnection();
            transport = transportFactory();
            using (var timeout = new CancellationTokenSource(timeoutMilliseconds))
            {
                try
                {
                    await transport.ConnectAsync(address, timeout.Token).ConfigureAwait(false);
                    var hello = await ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (Number(hello, "op") != 0) throw ProtocolError("expected the OBS Hello message");
                    var helloData = Object(hello, "d");
                    if (Number(helloData, "rpcVersion") < 1)
                        throw new InvalidOperationException("This server does not support OBS WebSocket v5 RPC version 1.");
                    var identify = new Dictionary<string, object> { { "rpcVersion", 1 }, { "eventSubscriptions", 0 } };
                    if (helloData.ContainsKey("authentication"))
                    {
                        if (String.IsNullOrEmpty(password))
                            throw new InvalidOperationException("OBS requires its WebSocket password. Enter it and connect again.");
                        var auth = Object(helloData, "authentication");
                        identify.Add("authentication", CreateAuthentication(password, Text(auth, "salt"), Text(auth, "challenge")));
                    }
                    await SendAsync(1, identify, timeout.Token).ConfigureAwait(false);
                    var reply = await ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (Number(reply, "op") != 2) throw ProtocolError("expected the OBS Identified message");
                    if (Number(Object(reply, "d"), "negotiatedRpcVersion") != 1)
                        throw ProtocolError("unsupported negotiated RPC version");
                    identified = true;
                }
                catch (Exception ex)
                {
                    ResetConnection();
                    throw FriendlyException(ex, timeout.IsCancellationRequested, "connecting to OBS");
                }
            }
        }

        public async Task<string[]> GetSceneNamesAsync()
        {
            var data = await RequestAsync("GetSceneList", null).ConfigureAwait(false);
            try
            {
                object value;
                if (!data.TryGetValue("scenes", out value) || !(value is IList)) throw ProtocolError("missing scene list");
                var names = new List<string>();
                foreach (object item in (IList)value)
                {
                    var scene = item as Dictionary<string, object>;
                    if (scene == null) throw ProtocolError("invalid scene in scene list");
                    names.Add(Text(scene, "sceneName"));
                }
                return names.ToArray();
            }
            catch
            {
                ResetConnection();
                throw;
            }
        }

        public async Task SetSceneAsync(string sceneName)
        {
            if (String.IsNullOrWhiteSpace(sceneName)) throw new ArgumentException("Choose an OBS scene for this key first.", "sceneName");
            await RequestAsync("SetCurrentProgramScene", new Dictionary<string, object> { { "sceneName", sceneName } }).ConfigureAwait(false);
        }

        private async Task<Dictionary<string, object>> RequestAsync(string requestType, Dictionary<string, object> requestData)
        {
            ThrowIfDisposed();
            if (!IsConnected) throw new InvalidOperationException("OBS is disconnected. Connect to the Mac again.");
            string requestId = Guid.NewGuid().ToString("N");
            var data = new Dictionary<string, object> { { "requestType", requestType }, { "requestId", requestId } };
            if (requestData != null) data.Add("requestData", requestData);
            using (var timeout = new CancellationTokenSource(timeoutMilliseconds))
            {
                try
                {
                    await SendAsync(6, data, timeout.Token).ConfigureAwait(false);
                    while (true)
                    {
                        var message = await ReceiveAsync(timeout.Token).ConfigureAwait(false);
                        int op = Number(message, "op");
                        if (op == 5) continue; // Ignore an unsolicited event from the server.
                        if (op != 7) throw ProtocolError("expected an OBS request response");
                        var reply = Object(message, "d");
                        if (Text(reply, "requestId") != requestId) continue;
                        if (Text(reply, "requestType") != requestType) throw ProtocolError("response request type did not match");
                        var status = Object(reply, "requestStatus");
                        object result;
                        if (!status.TryGetValue("result", out result) || !(result is bool)) throw ProtocolError("missing request result");
                        int code = Number(status, "code");
                        if (!(bool)result)
                        {
                            object comment;
                            string detail = status.TryGetValue("comment", out comment) && comment is string ? " " + (string)comment : "";
                            throw new InvalidOperationException("OBS rejected " + requestType + " (code " + code + ")." + detail);
                        }
                        return reply.ContainsKey("responseData") ? Object(reply, "responseData") : new Dictionary<string, object>();
                    }
                }
                catch (Exception ex)
                {
                    ResetConnection();
                    throw FriendlyException(ex, timeout.IsCancellationRequested, "waiting for OBS to complete " + requestType);
                }
            }
        }

        private Task SendAsync(int op, Dictionary<string, object> data, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> { { "op", op }, { "d", data } }));
            return transport.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private async Task<Dictionary<string, object>> ReceiveAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using (var bytes = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frame = await transport.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        int code = frame.CloseStatus.HasValue ? (int)frame.CloseStatus.Value : 0;
                        if (code == 4009) throw new InvalidOperationException("OBS rejected the WebSocket password. Check the password in OBS WebSocket Server Settings.");
                        throw new InvalidOperationException("OBS closed the WebSocket connection" + (code == 0 ? "." : " (code " + code + ").") + " Connect again.");
                    }
                    if (frame.MessageType != WebSocketMessageType.Text) throw ProtocolError("received a binary message instead of JSON text");
                    if (bytes.Length + frame.Count > MaxMessageBytes) throw ProtocolError("message exceeded the 1 MB limit");
                    bytes.Write(buffer, 0, frame.Count);
                    if (frame.EndOfMessage) break;
                }
                try
                {
                    string text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                    var message = json.DeserializeObject(text) as Dictionary<string, object>;
                    if (message == null) throw ProtocolError("expected a JSON object");
                    return message;
                }
                catch (ArgumentException) { throw ProtocolError("invalid JSON or UTF-8 message"); }
            }
        }

        internal static string CreateAuthentication(string password, string salt, string challenge)
        {
            using (var sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        {
            object found;
            if (!value.TryGetValue(key, out found) || !(found is Dictionary<string, object>)) throw ProtocolError("missing or invalid " + key);
            return (Dictionary<string, object>)found;
        }

        private static string Text(Dictionary<string, object> value, string key)
        {
            object found;
            if (!value.TryGetValue(key, out found) || !(found is string)) throw ProtocolError("missing or invalid " + key);
            return (string)found;
        }

        private static int Number(Dictionary<string, object> value, string key)
        {
            object found;
            if (!value.TryGetValue(key, out found) || !(found is int)) throw ProtocolError("missing or invalid " + key);
            return (int)found;
        }

        private static InvalidOperationException ProtocolError(string detail)
        {
            return new InvalidOperationException("Unexpected OBS WebSocket response: " + detail + ".");
        }

        private static Exception FriendlyException(Exception error, bool timedOut, string operation)
        {
            if (timedOut || error is OperationCanceledException)
                return new TimeoutException("Timed out " + operation + ". Check that OBS is running, WebSocket Server is enabled, and both computers are on the same network.", error);
            if (error is WebSocketException || error is IOException)
                return new InvalidOperationException("Could not communicate with OBS. Check the host, port, WebSocket Server setting, and firewall.", error);
            return error;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("ObsClient");
        }

        private void ResetConnection()
        {
            identified = false;
            if (transport == null) return;
            transport.Dispose();
            transport = null;
        }

        public void Dispose()
        {
            disposed = true;
            ResetConnection();
        }
    }

    // Small internal boundary lets tests drive full handshake/request logic and frame assembly.
    internal interface IObsTransport : IDisposable
    {
        WebSocketState State { get; }
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
        Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken cancellationToken);
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
    }

    internal sealed class ObsWebSocketTransport : IObsTransport
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        public WebSocketState State { get { return socket.State; } }
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            // Default OBS subprotocol is JSON. Avoid depending on subprotocol negotiation.
            socket.Options.Proxy = null;
            return socket.ConnectAsync(uri, cancellationToken);
        }
        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken cancellationToken)
        {
            return socket.SendAsync(buffer, type, endOfMessage, cancellationToken);
        }
        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return socket.ReceiveAsync(buffer, cancellationToken);
        }
        public void Dispose() { socket.Dispose(); }
    }
}
