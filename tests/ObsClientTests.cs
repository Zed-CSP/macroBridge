using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using StreamingBridge;

internal static class ObsClientTests
{
    private const string Salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=";
    private const string Challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=";
    // Fixed result independently calculated from the protocol's example password/salt/challenge.
    private const string ExpectedAuthentication = "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static int Main()
    {
        try
        {
            Run().GetAwaiter().GetResult();
            Console.WriteLine("PASS: 11 OBS client tests (mock transport; no remote OBS access).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task Run()
    {
        Assert(ObsClient.CreateAuthentication("supersecretpassword", Salt, Challenge) == ExpectedAuthentication, "OBS documentation authentication vector");
        await FragmentedHandshakeAndRequests();
        await RequestFailureDisposes();
        await AuthenticationFailureExplained();
        await MissingPasswordExplained();
        await ConnectTimeoutDisposes();
        await RequestTimeoutDisposes();
        await OversizedMessageRejected();
        await BinaryMessageRejected();
        await InvalidJsonRejected();
        await ReconnectReplacesSocket();
    }

    private static async Task FragmentedHandshakeAndRequests()
    {
        var wire = Hello(true);
        bool sceneChanged = false;
        wire.OnSend = delegate(Dictionary<string, object> message)
        {
            var data = Data(message);
            if ((int)message["op"] == 1)
            {
                Assert((int)data["rpcVersion"] == 1 && (int)data["eventSubscriptions"] == 0, "identification options");
                Assert((string)data["authentication"] == ExpectedAuthentication, "wire challenge response");
                wire.Enqueue("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}", 3);
                return;
            }
            Assert((int)message["op"] == 6, "request opcode");
            string type = (string)data["requestType"];
            if (type == "GetSceneList")
            {
                // An event and an unrelated response must not satisfy this request.
                wire.Enqueue("{\"op\":5,\"d\":{\"eventType\":\"ExitStarted\"}}", 9);
                wire.Enqueue(Response(type, "other-request", true, 100, new Dictionary<string, object> { { "scenes", new object[0] } }), 11);
                var scenes = new object[] {
                    new Dictionary<string, object> { { "sceneName", "Gaming" } },
                    new Dictionary<string, object> { { "sceneName", "Café 🎮" } }
                };
                // One-byte chunks deliberately split multibyte UTF-8 characters.
                wire.Enqueue(Response(type, (string)data["requestId"], true, 100, new Dictionary<string, object> { { "scenes", scenes } }), 1);
            }
            else
            {
                Assert(type == "SetCurrentProgramScene", "program scene request type");
                Assert((string)((Dictionary<string, object>)data["requestData"])["sceneName"] == "Café 🎮", "exact scene name preserved");
                sceneChanged = true;
                wire.Enqueue(Response(type, (string)data["requestId"], true, 100, null), 7);
            }
        };
        using (var client = Client(wire))
        {
            await client.ConnectAsync("192.0.2.10", 4455, "supersecretpassword");
            Assert(wire.Address == new Uri("ws://192.0.2.10:4455/"), "endpoint construction");
            var scenes = await client.GetSceneNamesAsync();
            Assert(scenes.Length == 2 && scenes[0] == "Gaming" && scenes[1] == "Café 🎮", "fragmented Unicode scene list");
            await client.SetSceneAsync("Café 🎮");
            Assert(sceneChanged && client.IsConnected, "scene acknowledged");
        }
        Assert(wire.Disposed, "dispose closes transport");
    }

    private static async Task RequestFailureDisposes()
    {
        var wire = Ready();
        wire.OnSend = delegate(Dictionary<string, object> message)
        {
            if ((int)message["op"] != 6) return;
            var data = Data(message);
            wire.Enqueue(Response((string)data["requestType"], (string)data["requestId"], false, 600, null), 13);
        };
        using (var client = Client(wire))
        {
            await client.ConnectAsync("localhost", 4455, "");
            var error = await Fails<InvalidOperationException>(delegate { return client.SetSceneAsync("Missing scene"); });
            Assert(error.Message.Contains("600") && error.Message.Contains("Scene does not exist"), "request status code and comment");
            Assert(wire.Disposed && !client.IsConnected, "request failure disposes transport");
        }
    }

    private static async Task AuthenticationFailureExplained()
    {
        var wire = Hello(true);
        wire.OnSend = delegate { wire.EnqueueClose(4009); };
        using (var client = Client(wire))
        {
            var error = await Fails<InvalidOperationException>(delegate { return client.ConnectAsync("localhost", 4455, "wrong-secret"); });
            Assert(error.Message.Contains("password") && !error.Message.Contains("wrong-secret"), "auth failure safe readable message");
            Assert(wire.Disposed, "authentication failure disposes");
        }
    }

    private static async Task MissingPasswordExplained()
    {
        var wire = Hello(true);
        using (var client = Client(wire))
        {
            var error = await Fails<InvalidOperationException>(delegate { return client.ConnectAsync("localhost", 4455, ""); });
            Assert(error.Message.Contains("requires its WebSocket password") && wire.Disposed, "missing password handling");
        }
    }

    private static async Task ConnectTimeoutDisposes()
    {
        var wire = new FakeTransport { BlockConnect = true };
        using (var client = new ObsClient(delegate { return wire; }, 40))
        {
            await Fails<TimeoutException>(delegate { return client.ConnectAsync("localhost", 4455, ""); });
            Assert(wire.Disposed, "connect timeout disposes");
        }
    }

    private static async Task RequestTimeoutDisposes()
    {
        var wire = Ready();
        using (var client = new ObsClient(delegate { return wire; }, 40))
        {
            await client.ConnectAsync("localhost", 4455, "");
            await Fails<TimeoutException>(delegate { return client.GetSceneNamesAsync(); });
            Assert(wire.Disposed && !client.IsConnected, "request timeout disposes");
        }
    }

    private static async Task OversizedMessageRejected()
    {
        var wire = new FakeTransport();
        wire.Enqueue(new string('x', 1024 * 1024 + 1), 8192);
        using (var client = Client(wire))
        {
            var error = await Fails<InvalidOperationException>(delegate { return client.ConnectAsync("localhost", 4455, ""); });
            Assert(error.Message.Contains("1 MB") && wire.Disposed, "size limit enforced");
        }
    }

    private static async Task BinaryMessageRejected()
    {
        var wire = new FakeTransport();
        wire.Frames.Enqueue(new Frame { Bytes = new byte[] { 0 }, Type = WebSocketMessageType.Binary, End = true });
        using (var client = Client(wire))
        {
            var error = await Fails<InvalidOperationException>(delegate { return client.ConnectAsync("localhost", 4455, ""); });
            Assert(error.Message.Contains("binary") && wire.Disposed, "binary frame rejected");
        }
    }

    private static async Task InvalidJsonRejected()
    {
        var wire = new FakeTransport();
        wire.Enqueue("{broken", 2);
        using (var client = Client(wire))
        {
            var error = await Fails<InvalidOperationException>(delegate { return client.ConnectAsync("localhost", 4455, ""); });
            Assert(error.Message.Contains("invalid JSON") && wire.Disposed, "invalid JSON rejected");
        }
    }

    private static async Task ReconnectReplacesSocket()
    {
        var first = Ready();
        var second = Ready();
        var pool = new Queue<IObsTransport>(new IObsTransport[] { first, second });
        using (var client = new ObsClient(delegate { return pool.Dequeue(); }, 1000))
        {
            await client.ConnectAsync("localhost", 4455, "");
            await client.ConnectAsync("localhost", 4455, "");
            Assert(first.Disposed && client.IsConnected && !second.Disposed, "reconnect replaces old transport");
        }
    }

    private static ObsClient Client(FakeTransport wire) { return new ObsClient(delegate { return wire; }, 1000); }
    private static Dictionary<string, object> Data(Dictionary<string, object> message) { return (Dictionary<string, object>)message["d"]; }
    private static FakeTransport Hello(bool authentication)
    {
        var wire = new FakeTransport();
        var data = new Dictionary<string, object> { { "rpcVersion", 1 } };
        if (authentication) data.Add("authentication", new Dictionary<string, object> { { "salt", Salt }, { "challenge", Challenge } });
        wire.Enqueue(Json.Serialize(new Dictionary<string, object> { { "op", 0 }, { "d", data } }), 5);
        return wire;
    }
    private static FakeTransport Ready()
    {
        var wire = Hello(false);
        wire.Enqueue("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}", 8);
        return wire;
    }
    private static string Response(string requestType, string requestId, bool result, int code, object response)
    {
        var status = new Dictionary<string, object> { { "result", result }, { "code", code } };
        if (!result) status.Add("comment", "Scene does not exist");
        var data = new Dictionary<string, object> { { "requestType", requestType }, { "requestId", requestId }, { "requestStatus", status } };
        if (response != null) data.Add("responseData", response);
        return Json.Serialize(new Dictionary<string, object> { { "op", 7 }, { "d", data } });
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
    private static async Task<T> Fails<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new Exception("FAIL: expected " + typeof(T).Name);
    }

    private sealed class Frame
    {
        internal byte[] Bytes;
        internal WebSocketMessageType Type = WebSocketMessageType.Text;
        internal bool End;
        internal WebSocketCloseStatus? Close;
    }
    private sealed class FakeTransport : IObsTransport
    {
        internal readonly Queue<Frame> Frames = new Queue<Frame>();
        internal Action<Dictionary<string, object>> OnSend;
        internal bool Disposed;
        internal bool BlockConnect;
        internal Uri Address;
        public WebSocketState State { get; private set; }
        public async Task ConnectAsync(Uri uri, CancellationToken token)
        {
            Address = uri;
            if (BlockConnect) await Task.Delay(Timeout.Infinite, token);
            State = WebSocketState.Open;
        }
        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert(type == WebSocketMessageType.Text && end, "client sends complete JSON text");
            if (OnSend != null) OnSend((Dictionary<string, object>)Json.DeserializeObject(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count)));
            return Task.FromResult(0);
        }
        public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            if (Frames.Count == 0) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
            var frame = Frames.Dequeue();
            Assert(frame.Bytes.Length <= buffer.Count, "mock fragment fits receive buffer");
            Array.Copy(frame.Bytes, 0, buffer.Array, buffer.Offset, frame.Bytes.Length);
            return new WebSocketReceiveResult(frame.Bytes.Length, frame.Type, frame.End, frame.Close, null);
        }
        internal void Enqueue(string message, int fragmentSize)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            for (int offset = 0; offset < bytes.Length; offset += fragmentSize)
            {
                int count = Math.Min(fragmentSize, bytes.Length - offset);
                byte[] fragment = new byte[count];
                Array.Copy(bytes, offset, fragment, 0, count);
                Frames.Enqueue(new Frame { Bytes = fragment, End = offset + count == bytes.Length });
            }
        }
        internal void EnqueueClose(int code) { Frames.Enqueue(new Frame { Bytes = new byte[0], Type = WebSocketMessageType.Close, End = true, Close = (WebSocketCloseStatus)code }); }
        public void Dispose() { Disposed = true; State = WebSocketState.Closed; }
    }
}
