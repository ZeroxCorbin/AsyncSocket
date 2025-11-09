using CommunityToolkit.Mvvm.ComponentModel;
using Logging.lib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncSocket
{
    public enum ASocketStates
    {
        Closed,
        Trying,
        Open,
        Exception
    }

    public class StateObject
    {
        public const int BufferSize = 4096;
        public byte[] buffer = new byte[BufferSize];
        public Socket? WorkSocket;
    }
    
    public partial class ASocket : ObservableObject
    {
        [ObservableProperty] private bool isReceiving = false;
        [ObservableProperty] private bool isListener = false;

        [ObservableProperty] private ASocketStates state = ASocketStates.Closed;
        [ObservableProperty] private Exception? lastException;

        public delegate void SimpleDelegate();
        public delegate void ReceiveEventDelegate(byte[] buffer, string msg);

        public event ReceiveEventDelegate? ReceiveEvent;
        public event EventHandler? ExceptionEvent;

        private Socket? _client; // Used as the listening socket in listener mode
        private readonly ConcurrentDictionary<Socket, StateObject> _clients = new ConcurrentDictionary<Socket, StateObject>();
        private int _maxClients = 1;

        public ReadOnlyCollection<Socket> Clients => new List<Socket>(_clients.Keys).AsReadOnly();

        public bool GetException(out Exception? exception)
        {
            exception = LastException;
            return LastException != null;
        }

        protected void HandleException(Exception e)
        {
            Logger.Error(e);

            State = ASocketStates.Exception;
            IsReceiving = false;

            LastException = e;

            Close();

            _ = Task.Run(() => ExceptionEvent?.Invoke(e, null));
        }
        public bool Listen(string host, int port, int maxClients = 1, int backlog = 10)
        {
            Close();
            try
            {
                _maxClients = maxClients;
                IsListener = true;
                var localEP = new IPEndPoint(IPAddress.Parse(host), port);
                _client = new Socket(localEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _client.Bind(localEP);
                _client.Listen(backlog);
                State = ASocketStates.Open;

                // Start accepting clients in a loop
                _ = Task.Run(() => AcceptLoop());
                return true;
            }
            catch (Exception e)
            {
                HandleException(e);
                return false;
            }
        }
        private void AcceptLoop()
        {
            while (State == ASocketStates.Open && IsListener && _client != null)
            {
                try
                {
                    if (_clients.Count >= _maxClients)
                    {
                        Thread.Sleep(100); // Max clients reached, wait a bit before checking again
                        continue;
                    }

                    var acceptResult = _client.BeginAccept(null, null);
                    acceptResult.AsyncWaitHandle.WaitOne();
                    if (_client == null) break;
                    Socket handler = _client.EndAccept(acceptResult);

                    Console.WriteLine("Socket accepted from {0}", handler.RemoteEndPoint.ToString());

                    var state = new StateObject
                    {
                        buffer = new byte[StateObject.BufferSize],
                        WorkSocket = handler
                    };

                    if (_clients.TryAdd(handler, state))
                    {
                        _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            new AsyncCallback(ReceiveCallback), state);
                    }
                    else
                    {
                        // Should not happen if logic is correct
                        handler.Close();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break; // Listener closed
                }
                catch (Exception e)
                {
                    HandleException(e);
                    break;
                }
            }
        }
        public void Close()
        {
            State = ASocketStates.Closed;
            IsReceiving = false;

            if (_client != null)
            {
                try
                {
                    if (_client.Connected)
                        _client.Shutdown(SocketShutdown.Both);
                }
                catch { }
                _client.Close();
                _client = null;
            }

            foreach (var client in _clients.Keys)
            {
                try
                {
                    if (client.Connected)
                        client.Shutdown(SocketShutdown.Both);
                }
                catch { }
                try
                {
                    client.Close();
                }
                catch { }
            }
            _clients.Clear();
        }
        public void CancelConnect()
        {
            if (_client == null) return;

            if (!_client.Connected)
            {
                Close();
            }
        }

        public bool Connect(string host, int port, int timeout = 5000) => Connect(new ASocketSettings($"{host}:{port}"), timeout);
        public bool Connect(ASocketSettings settings, int timeout = 5000)
        {
            Close();

            try
            {
                IsListener = false;

                var remoteEP = new IPEndPoint(settings.IPAddress, settings.Port);

                _client = new Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                IAsyncResult connectResult = _client.BeginConnect(remoteEP, null, null);

                _ = connectResult.AsyncWaitHandle.WaitOne(timeout, true);

                if (_client == null) return false;

                if (_client.Connected)
                {
                    _client.EndConnect(connectResult);

                    // In client mode, we still use _clients, but it will only have one
                    var state = new StateObject { WorkSocket = _client };
                    _clients.TryAdd(_client, state);

                    State = ASocketStates.Open;

                    return true;
                }
                else
                {
                    throw new Exception("Connection timeout.");
                }
            }
            catch (Exception e)
            {
                HandleException(e);
                return false;
            }
        }

        public void StartConnect(string host, int port) => StartConnect(new ASocketSettings($"{host}:{port}"));
        public void StartConnect(ASocketSettings settings)
        {
            Close();

            var remoteEP = new IPEndPoint(settings.IPAddress, settings.Port);

            _client = new Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            _ = _client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), _client);
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                if (_client == null)
                {
                    State = ASocketStates.Closed;
                    return;
                }

                _client.EndConnect(ar);

                // Assign the connected socket to _acceptedClient
                var state = new StateObject { WorkSocket = _client };
                _clients.TryAdd(_client, state);

                Console.WriteLine("Socket connected to {0}",
                    _client.RemoteEndPoint.ToString());

                State = ASocketStates.Open;
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void StartReceive()
        {
            if (State != ASocketStates.Open) return;

            try
            {
                // In client mode, there's one socket. In server mode, this will start receiving on all sockets.
                foreach (var client in _clients.Values)
                {
                    var state = new StateObject
                    {
                        WorkSocket = client.WorkSocket
                    };

                    _ = client.WorkSocket?.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }

                IsReceiving = true;
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket? handler = state.WorkSocket;

            try
            {
                if (handler == null)
                {
                    IsReceiving = false;
                    return;
                }

                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    IsReceiving = true;

                    var msg = Encoding.ASCII.GetString(state.buffer, 0, bytesRead);

                    ReceiveEvent?.Invoke(state.buffer, msg);

                    Array.Clear(state.buffer, 0, StateObject.BufferSize);

                    // Continue receiving on this socket
                    _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // Client disconnected
                    if (_clients.TryRemove(handler, out _))
                    {
                        handler.Shutdown(SocketShutdown.Both);
                        handler.Close();
                    }
                    if (_clients.IsEmpty)
                    {
                        IsReceiving = false;
                    }
                }
            }
            catch (ObjectDisposedException e1)
            {
                HandleException(e1);
            }
            catch (Exception e)
            {
                if (handler != null && _clients.TryRemove(handler, out _))
                {
                    handler.Close();
                }
                HandleException(e);
            }
        }

        public byte[] ReceiveBytes(int timeout)
        {
            if (State != ASocketStates.Open || _clients.IsEmpty) return [];

            var client = _clients.Keys.FirstOrDefault(); // Only receives from the first client
            if (client == null) return [];

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[1024];
                int readBytes;

                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (client.Available > 0)
                        if ((readBytes = client.Receive(byteData)) > 0)
                            return byteData;
                }

                return byteData;
            }
            catch (Exception e)
            {
                HandleException(e);
                return [];
            }
        }

        public string? Receive(int timeout)
        {
            if (State != ASocketStates.Open || _clients.IsEmpty) return null;

            var client = _clients.Keys.FirstOrDefault(); // Only receives from the first client
            if (client == null) return null;

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[1024];
                int readBytes;

                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (client.Available > 0)
                        if ((readBytes = client.Receive(byteData)) > 0)
                            return System.Text.Encoding.UTF8.GetString(byteData, 0, readBytes);
                }

                return "";
            }
            catch (Exception e)
            {
                HandleException(e);
                return null;
            }
        }

        public string? Receive(int timeout, string terminator)
        {
            if (State != ASocketStates.Open || _clients.IsEmpty) return null;

            var client = _clients.Keys.FirstOrDefault(); // Only receives from the first client
            if (client == null) return null;

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[4098];
                var sb = new StringBuilder();
                int bytesRead;
                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (client.Available > 0)
                    {
                        if ((bytesRead = client.Receive(byteData)) > 0)
                        {
                            stop.Restart();

                            _ = sb.Append(Encoding.ASCII.GetString(byteData, 0, bytesRead));

                            if (sb.ToString().EndsWith(terminator))
                                break;
                        }
                    }
                    else
                        Thread.Sleep(10);

                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                HandleException(e);
                return null;
            }
        }

        public void Send(string data)
        {
            if (State != ASocketStates.Open) return;
            var byteData = Encoding.ASCII.GetBytes(data);
            Send(byteData);
        }

        public void Send(byte[] data)
        {
            if (State != ASocketStates.Open) return;
            // This method now broadcasts to all clients.
            Broadcast(data);
        }

        public void Send(Socket client, string data)
        {
            if (State != ASocketStates.Open) return;
            var byteData = Encoding.ASCII.GetBytes(data);
            Send(client, byteData);
        }

        public void Send(Socket client, byte[] data)
        {
            if (State != ASocketStates.Open) return;
            if (!_clients.ContainsKey(client)) return;

            try
            {
                _ = client.Send(data);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void Broadcast(string data)
        {
            if (State != ASocketStates.Open) return;
            var byteData = Encoding.ASCII.GetBytes(data);
            Broadcast(byteData);
        }

        public void Broadcast(byte[] data)
        {
            if (State != ASocketStates.Open) return;
            foreach (var client in _clients.Keys)
            {
                try
                {
                    _ = client.Send(data);
                }
                catch (Exception e)
                {
                    // Log per-client send exception, but don't tear down everything
                    Logger.Error($"Failed to send to client {client.RemoteEndPoint}: {e.Message}");
                }
            }
        }

        private void StartSend(string data)
        {
            // This method is ambiguous in multi-client scenario.
            // Let's make it broadcast.
            var byteData = Encoding.ASCII.GetBytes(data);
            foreach (var client in _clients.Keys)
            {
                _ = client.BeginSend(byteData, 0, byteData.Length, 0,
                    new AsyncCallback(SendCallback), client);
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                var client = (System.Net.Sockets.Socket)ar.AsyncState;

                var bytesSent = client.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to server.", bytesSent);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private bool DetectConnection()
        {
            if (_clients.IsEmpty) return false;

            // This check is ambiguous in a multi-client scenario.
            // Checking the first client for simplicity.
            var client = _clients.Keys.FirstOrDefault();
            if (client == null) return false;

            if (client.Poll(0, SelectMode.SelectRead))
            {
                var buff = new byte[1];
                if (client.Receive(buff, SocketFlags.Peek) == 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            return true;
        }
    }
}
