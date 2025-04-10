using SocketLibrary.Implementation;
using SocketLibrary.Interfaces;

namespace SocketLibrary.Factory
{
    public static class SocketFactory
    {
        public static ISocketClient CreateClient(IMessageHandler messageHandler = null,
                                               int retries = 3,
                                               int retryDelay = 1000,
                                               int timeout = 30000)
        {
            return new TcpSocketClient(messageHandler, retries, retryDelay, timeout);
        }

        public static ISocketServer CreateServer(IMessageHandler messageHandler = null,
                                               int retries = 3,
                                               int retryDelay = 1000,
                                               int timeout = 30000)
        {
            return new TcpSocketServer(messageHandler, retries, retryDelay, timeout);
        }
    }
}
