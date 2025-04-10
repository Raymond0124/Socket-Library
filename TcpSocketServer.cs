using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SocketLibrary.Core;
using SocketLibrary.Interfaces;

namespace SocketLibrary.Implementation
{
    public class TcpSocketServer : ISocketServer
    {
        private TcpListener _listener;
        private readonly RetryPolicy _retryPolicy;
        private readonly IMessageHandler _messageHandler;
        private CancellationTokenSource _cts;
        private readonly List<TcpClient> _connectedClients = new();
        private readonly int _timeoutMilliseconds;
        private readonly object _clientsLock = new();

        public bool IsListening => _listener != null;

        public event EventHandler<TcpClient> ClientConnected;
        public event EventHandler<TcpClient> ClientDisconnected;
        public event EventHandler<(TcpClient Client, byte[] Message)> MessageReceived;
        public event EventHandler<Exception> ErrorOccurred;

        public TcpSocketServer(IMessageHandler messageHandler = null, int retries = 3,
                              int retryDelayMilliseconds = 1000, int timeoutMilliseconds = 30000)
        {
            _messageHandler = messageHandler;
            _retryPolicy = new RetryPolicy(retries, retryDelayMilliseconds);
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        public async Task StartListeningAsync(int port)
        {
            if (IsListening)
                throw new InvalidOperationException("Server is already listening");

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _cts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var timeoutTask = Task.Delay(_timeoutMilliseconds);
                        var acceptTask = _listener.AcceptTcpClientAsync();

                        var completedTask = await Task.WhenAny(acceptTask, timeoutTask);

                        if (completedTask == timeoutTask)
                            continue;

                        var client = await acceptTask;

                        lock (_clientsLock)
                        {
                            _connectedClients.Add(client);
                        }

                        ClientConnected?.Invoke(this, client);

                        _ = HandleClientAsync(client, _cts.Token);
                    }
                }
                catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }, _cts.Token);

            await Task.CompletedTask;
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using NetworkStream stream = client.GetStream();
                byte[] lengthBuffer = new byte[4];

                while (!token.IsCancellationRequested && client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, token);
                    if (bytesRead < 4) break;

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await stream.ReadAsync(messageBuffer, 0, messageLength, token);
                    if (bytesRead < messageLength) break;

                    MessageReceived?.Invoke(this, (client, messageBuffer));

                    if (_messageHandler != null)
                        await _messageHandler.HandleMessageAsync(messageBuffer, this);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
            finally
            {
                lock (_clientsLock)
                {
                    _connectedClients.Remove(client);
                }

                ClientDisconnected?.Invoke(this, client);
                client.Dispose();
            }
        }

        public async Task BroadcastAsync(byte[] message)
        {
            if (!IsListening)
                throw new InvalidOperationException("Server is not listening");

            List<TcpClient> clients;
            lock (_clientsLock)
            {
                clients = _connectedClients.ToList();
            }

            var tasks = new List<Task>();

            foreach (var client in clients)
            {
                tasks.Add(_retryPolicy.ExecuteAsync(async () =>
                {
                    if (!client.Connected)
                        return;

                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(BitConverter.GetBytes(message.Length), 0, 4);
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                }));
            }

            await Task.WhenAll(tasks);
        }

        public async Task StopListeningAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            _listener?.Stop();
            _listener = null;

            TcpClient[] clientsToDispose;
            lock (_clientsLock)
            {
                clientsToDispose = _connectedClients.ToArray();
                _connectedClients.Clear();
            }

            foreach (var client in clientsToDispose)
            {
                client.Dispose();
            }

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            StopListeningAsync().Wait();
        }
    }
}
