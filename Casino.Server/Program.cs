using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Casino.Data;

namespace Casino.Server
{
    internal static class Program
    {
        // Puerto del servidor
        private const int Port = 5000;

        private static async Task Main()
        {
            Console.Title = "Casino Poker Server";
             
            // Asegurar que la BD existe (tablas Users, Tables, etc.)
            DatabaseInitializer.EnsureCreated();
            Console.WriteLine("BD inicializada.");

            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine($"Servidor escuchando en puerto {Port}...");

            while (true)
            {
                var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                Console.WriteLine($"Nuevo cliente conectado desde {tcpClient.Client.RemoteEndPoint}");

                // Lanzar handler por cliente (thread/task independiente)
                _ = Task.Run(() => ServerClientHandler.HandleClientAsync(tcpClient));
            }
        }
    }
}