using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Casino.Data
{
    /// <summary>
    /// Cliente TCP simple para hablar con Casino.Server.
    /// </summary>
    public sealed class ServerClient : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private readonly CancellationTokenSource _cts = new();

        public event Action<string>? LineReceived;

        public bool IsConnected => _tcpClient.Connected;

        // Canal interno para respuestas de "peticion sincrona"
        private readonly Channel<string> _responseChannel = Channel.CreateUnbounded<string>();

        // Solo un SendAndWaitAsync a la vez
        private readonly SemaphoreSlim _sendWaitLock = new(1, 1);

        public ServerClient(string host, int port)
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(host, port);

            var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };

            // Lanzar bucle de lectura en background
            Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Envia una linea (comando) al servidor.
        /// </summary>
        public Task SendAsync(string line)
        {
            if (!_tcpClient.Connected)
                throw new IOException("No conectado al servidor.");

            return _writer.WriteLineAsync(line);
        }

        /// <summary>
        /// Envía un comando y espera una línea que empiece por el prefijo indicado
        /// o bien un ERR. Cualquier otra línea que llegue se reenvía a LineReceived
        /// pero NO se devuelve como resultado de este método.
        /// </summary>
        public async Task<string> SendAndWaitAsync(string line, string expectedPrefix, CancellationToken cancellationToken = default)
        {
            await _sendWaitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendAsync(line).ConfigureAwait(false);

                var reader = _responseChannel.Reader;
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);

                while (await reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var resp))
                    {
                        // Si es la respuesta esperada (OK <CMD>) o un error, la devolvemos
                        if (resp.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                            resp.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[Client] SendAndWaitAsync('{line}') -> '{resp}'");
                            return resp;
                        }

                        // Si no es para este comando (POLL_STATE, CHAT, etc.), la mandamos al evento general
                        LineReceived?.Invoke(resp);
                    }
                }

                throw new IOException("Conexion cerrada al esperar respuesta del servidor.");
            }
            finally
            {
                _sendWaitLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                        break; // servidor cerro conexion

                    // Todas las lineas entran primero al canal; SendAndWaitAsync decidirá si son para él
                    await _responseChannel.Writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);

                    // Además, notificar siempre al oyente general (POLL_STATE, CHAT, etc.)
                    LineReceived?.Invoke(line);
                }
            }
            catch
            {
                // Ignorar errores de red aqui, el ViewModel podra reaccionar a desconexion
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _tcpClient.Close();
            _cts.Dispose();
        }
    }
}