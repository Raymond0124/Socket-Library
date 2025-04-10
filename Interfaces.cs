using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SocketLibrary.Interfaces
{
    /// <summary>
    /// Interfaz para manejar mensajes recibidos.
    /// </summary>
    public interface IMessageHandler
    {
        Task HandleMessageAsync(byte[] message, object sender);
    }

    /// <summary>
    /// Interfaz que define un cliente de socket.
    /// </summary>
    public interface ISocketClient : IDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(string host, int port);
        Task SendAsync(byte[] message);
        Task DisconnectAsync();

        event EventHandler<byte[]> MessageReceived;
        event EventHandler<Exception> ErrorOccurred;
    }

    /// <summary>
    /// Interfaz que define un servidor de socket.
    /// </summary>
    public interface ISocketServer : IDisposable
    {
        bool IsListening { get; }

        Task StartListeningAsync(int port);
        Task BroadcastAsync(byte[] message);
        Task StopListeningAsync();

        event EventHandler<TcpClient> ClientConnected;
        event EventHandler<TcpClient> ClientDisconnected;
        event EventHandler<(TcpClient Client, byte[] Message)> MessageReceived;
        event EventHandler<Exception> ErrorOccurred;
    }
}

