﻿using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UmlautAdaptarr.Services
{
    public class HttpProxyService : IHostedService
    {
        private TcpListener _listener;
        private readonly ILogger<HttpProxyService> _logger;
        private readonly int _proxyPort = 5006; // TODO move to appsettings.json
        private readonly IHttpClientFactory _clientFactory;
        private HashSet<string> _knownHosts = [];
        private readonly object _hostsLock = new object();


        public HttpProxyService(ILogger<HttpProxyService> logger, IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _knownHosts.Add("prowlarr.servarr.com");
        }

        private async Task HandleRequests(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var clientSocket = await _listener.AcceptSocketAsync();
                _ = Task.Run(() => ProcessRequest(clientSocket), stoppingToken);
            }
        }

        private async Task ProcessRequest(Socket clientSocket)
        {
            using var clientStream = new NetworkStream(clientSocket, ownsSocket: true);
            var buffer = new byte[8192];
            var bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
            var requestString = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            if (requestString.StartsWith("CONNECT"))
            {
                // Handle HTTPS CONNECT request
                await HandleHttpsConnect(requestString, clientStream, clientSocket);
            }
            else
            {
                // Handle HTTP request
                await HandleHttp(requestString, clientStream, clientSocket, buffer, bytesRead);
            }
        }

        private async Task HandleHttpsConnect(string requestString, NetworkStream clientStream, Socket clientSocket)
        {
            var (host, port) = ParseTargetInfo(requestString);

            // Prowlarr will send grab requests via https which cannot be changed
            if (!_knownHosts.Contains(host))
            {
                _logger.LogWarning($"IMPORTANT! {Environment.NewLine} Indexer {host} needs to be set to http:// instead of https:// {Environment.NewLine}" +
                    $"UmlautAdaptarr will not work for {host}!");
            }
            using var targetSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await targetSocket.ConnectAsync(host, port);
                await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));
                using var targetStream = new NetworkStream(targetSocket, ownsSocket: true);
                await RelayTraffic(clientStream, targetStream);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to target: {ex.Message}");
                clientSocket.Close();
            }
        }

        private async Task HandleHttp(string requestString, NetworkStream clientStream, Socket clientSocket, byte[] buffer, int bytesRead)
        {
            try
            {
                var headers = ParseHeaders(buffer, bytesRead);
                string userAgent = headers.FirstOrDefault(h => h.Key == "User-Agent").Value;
                var uri = new Uri(requestString.Split(' ')[1]);

                // Add to known hosts if not already present
                lock (_hostsLock)
                {
                    if (!_knownHosts.Contains(uri.Host))
                    {
                        _knownHosts.Add(uri.Host);
                    }
                }

                var modifiedUri = $"http://localhost:5005/_/{uri.Host}{uri.PathAndQuery}";  // TODO read port from appsettings?
                using var client = _clientFactory.CreateClient();
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, modifiedUri);
                httpRequestMessage.Headers.Add("User-Agent", userAgent);
                var result = await client.SendAsync(httpRequestMessage);

                if (result.IsSuccessStatusCode)
                {
                    var responseData = await result.Content.ReadAsByteArrayAsync();
                    await clientStream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {responseData.Length}\r\n\r\n"));
                    await clientStream.WriteAsync(responseData);
                }
                else
                {
                    await clientStream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {result.StatusCode}\r\n\r\n"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"HTTP Proxy error: {ex.Message}");
                await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 500 Internal Server Error\r\n\r\n"));
            }
            finally
            {
                clientSocket.Close();
            }
        }

        private Dictionary<string, string> ParseHeaders(byte[] buffer, int length)
        {
            var headers = new Dictionary<string, string>();
            var headerString = Encoding.ASCII.GetString(buffer, 0, length);
            var lines = headerString.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1)) // Skip the request line
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = line.Substring(0, colonIndex).Trim();
                    var value = line.Substring(colonIndex + 1).Trim();
                    headers[key] = value;
                }
            }
            return headers;
        }

        private (string host, int port) ParseTargetInfo(string requestLine)
        {
            var parts = requestLine.Split(' ')[1].Split(':');
            return (parts[0], int.Parse(parts[1]));
        }

        private async Task RelayTraffic(NetworkStream clientStream, NetworkStream targetStream)
        {
            var clientToTargetTask = RelayStream(clientStream, targetStream);
            var targetToClientTask = RelayStream(targetStream, clientStream);
            await Task.WhenAll(clientToTargetTask, targetToClientTask);
        }

        private async Task RelayStream(NetworkStream input, NetworkStream output)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                await output.FlushAsync();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _listener = new TcpListener(IPAddress.Any, _proxyPort);
            _listener.Start();
            Task.Run(() => HandleRequests(cancellationToken), cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _listener.Stop();
            return Task.CompletedTask;
        }
    }
}
