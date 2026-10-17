using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HueDesk;

public sealed class MiioClient : IDisposable
{
    private const int MiioPort = 54321;
    private const ushort Magic = 0x2131;
    private const int HeaderLength = 32;
    private const int TokenLength = 16;
    private const int ReceiveTimeoutMilliseconds = 2000;
    private const int MaximumAttempts = 2;
    private const int HelloPacketLength = HeaderLength;
    private const uint UnsetField = 0xFFFFFFFF;

    private readonly IPEndPoint endpoint;
    private readonly byte[] token;
    private readonly byte[] key;
    private readonly byte[] initializationVector;
    private readonly SemaphoreSlim callLock = new(1, 1);
    private readonly UdpClient udpClient = new();
    private uint deviceId;
    private uint deviceStamp;
    private DateTime stampReceivedAt;
    private int requestId;

    public MiioClient(string address, string tokenHex)
    {
        token = Convert.FromHexString(tokenHex);
        if (token.Length != TokenLength)
        {
            throw new ArgumentException("A miIO token is 32 hexadecimal characters.", nameof(tokenHex));
        }

        endpoint = new IPEndPoint(IPAddress.Parse(address), MiioPort);
        key = MD5.HashData(token);
        initializationVector = MD5.HashData([.. key, .. token]);
    }

    public async Task<JsonNode> Call(string method, JsonNode parameters)
    {
        await callLock.WaitAsync();
        try
        {
            return await CallWithRetry(method, parameters);
        }
        finally
        {
            callLock.Release();
        }
    }

    public void Dispose()
    {
        udpClient.Dispose();
        callLock.Dispose();
    }

    private async Task<JsonNode> CallWithRetry(string method, JsonNode parameters)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (deviceId == 0)
                {
                    await Handshake();
                }

                return await SendRequest(method, parameters);
            }
            catch (Exception exception) when (attempt < MaximumAttempts && exception is TimeoutException or SocketException)
            {
                deviceId = 0;
            }
        }
    }

    private async Task Handshake()
    {
        var hello = new byte[HelloPacketLength];
        WriteUInt16(hello, 0, Magic);
        WriteUInt16(hello, 2, HelloPacketLength);
        WriteUInt32(hello, 4, UnsetField);
        WriteUInt32(hello, 8, UnsetField);
        WriteUInt32(hello, 12, UnsetField);
        for (var index = 16; index < HeaderLength; index++)
        {
            hello[index] = 0xFF;
        }

        var reply = await Exchange(hello);
        deviceId = ReadUInt32(reply, 8);
        deviceStamp = ReadUInt32(reply, 12);
        stampReceivedAt = DateTime.UtcNow;
    }

    private async Task<JsonNode> SendRequest(string method, JsonNode parameters)
    {
        requestId = requestId % 9999 + 1;
        var body = new JsonObject
        {
            ["id"] = requestId,
            ["method"] = method,
            ["params"] = parameters,
        };

        var encrypted = Encrypt(Encoding.UTF8.GetBytes(body.ToJsonString()));
        var packet = new byte[HeaderLength + encrypted.Length];
        WriteUInt16(packet, 0, Magic);
        WriteUInt16(packet, 2, (ushort)packet.Length);
        WriteUInt32(packet, 8, deviceId);
        WriteUInt32(packet, 12, deviceStamp + (uint)(DateTime.UtcNow - stampReceivedAt).TotalSeconds);
        token.CopyTo(packet, 16);
        encrypted.CopyTo(packet, HeaderLength);
        MD5.HashData(packet).CopyTo(packet, 16);

        var reply = await Exchange(packet);
        var decrypted = Decrypt(reply.AsSpan(HeaderLength).ToArray());
        var response = JsonNode.Parse(decrypted) ?? throw new InvalidDataException("Empty miIO response.");
        if (response["error"] is { } error)
        {
            throw new InvalidOperationException($"miIO error: {error.ToJsonString()}");
        }

        return response["result"] ?? throw new InvalidDataException("miIO response has no result.");
    }

    private async Task<byte[]> Exchange(byte[] packet)
    {
        await udpClient.SendAsync(packet, endpoint);
        using var timeout = new CancellationTokenSource(ReceiveTimeoutMilliseconds);
        try
        {
            var received = await udpClient.ReceiveAsync(timeout.Token);
            if (received.Buffer.Length < HeaderLength)
            {
                throw new InvalidDataException("miIO reply is shorter than its header.");
            }

            return received.Buffer;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No miIO reply from {endpoint.Address}.");
        }
    }

    private byte[] Encrypt(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(plain, initializationVector, PaddingMode.PKCS7);
    }

    private string Decrypt(byte[] encrypted)
    {
        if (encrypted.Length == 0)
        {
            throw new InvalidDataException("miIO reply has no body.");
        }

        using var aes = Aes.Create();
        aes.Key = key;
        var plain = aes.DecryptCbc(encrypted, initializationVector, PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain).TrimEnd('\0');
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
}
