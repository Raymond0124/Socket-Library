using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SocketLibrary.Core;
using SocketLibrary.Interfaces;

namespace SocketLibrary.Implementation
{
    public class TcpSocketClient : ISocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly RetryPolicy _retryPolicy;
        private readonly IMessageHandler _messageHandler;
        private CancellationTokenSource _cts;
        private readonly int _timeoutMilliseconds;

        public bool IsConnected => _client?.Connected ?? false;

        public event EventHandler<byte[]> MessageReceived;
        public event EventHandler<Exception> ErrorOccurred;

        public TcpSocketClient(IMessageHandler messageHandler = null, int retries = 3,
                             int retryDelayMilliseconds = 1000, int timeoutMilliseconds = 30000)
        {
            _messageHandler = messageHandler;
            _retryPolicy = new RetryPolicy(retries, retryDelayMilliseconds);
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        public async Task ConnectAsync(string host, int port)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                _client = new TcpClient();

                var timeoutTask = Task.Delay(_timeoutMilliseconds);
                var connectTask = _client.ConnectAsync(host, port);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                    throw new TimeoutException($"Connection to {host}:{port} timed out after {_timeoutMilliseconds}ms");

                await connectTask;
                _stream = _client.GetStream();

                _cts = new CancellationTokenSource();
                _ = StartListeningAsync(_cts.Token);
            });
        }

        public async Task SendAsync(byte[] message)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Client is not connected");

            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _stream.WriteAsync(BitConverter.GetBytes(message.Length), 0, 4);
                await _stream.WriteAsync(message, 0, message.Length);
                await _stream.FlushAsync();
            });
        }

        public async Task DisconnectAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client = null;
            }

            await Task.CompletedTask;
        }

        private async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            byte[] lengthBuffer = new byte[4];

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    int bytesRead = await _stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);
                    if (bytesRead < 4) break;

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await _stream.ReadAsync(messageBuffer, 0, messageLength, cancellationToken);
                    if (bytesRead < messageLength) break;

                    MessageReceived?.Invoke(this, messageBuffer);

                    if (_messageHandler != null)
                        await _messageHandler.HandleMessageAsync(messageBuffer, this);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}
