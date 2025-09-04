using CommunityToolkit.Mvvm.ComponentModel;
using Logging.lib;
using System;
using System.Collections.Concurrent;
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
        public Socket WorkSocket;
    }
    
    public partial class ASocket : ObservableObject
    {
        [ObservableProperty] private bool isReceiving = false;
        [ObservableProperty] private bool isListener = false;

        [ObservableProperty] private ASocketStates state = ASocketStates.Closed;
        [ObservableProperty] private Exception lastException;

        public delegate void SimpleDelegate();
        public delegate void ReceiveEventDelegate(byte[] buffer, string msg);

        public event ReceiveEventDelegate ReceiveEvent;
        public event EventHandler ExceptionEvent;

        private Socket _client; // Used as the listening socket in listener mode
        private Socket _acceptedClient; // Only one client allowed at a time

        public bool GetException(out Exception exception)
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
        public bool Listen(string host, int port, int backlog = 10)
        {
            Close();
            try
            {
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
                    var acceptResult = _client.BeginAccept(null, null);
                    acceptResult.AsyncWaitHandle.WaitOne();
                    if (_client == null) break;
                    Socket handler = _client.EndAccept(acceptResult);

                    // Disconnect previous client if exists
                    if (_acceptedClient != null)
                    {
                        try
                        {
                            if (_acceptedClient.Connected)
                                _acceptedClient.Shutdown(SocketShutdown.Both);
                        }
                        catch { }
                        try
                        {
                            _acceptedClient.Close();
                        }
                        catch { }
                        _acceptedClient = null;
                    }

                    _acceptedClient = handler;

                    Console.WriteLine("Socket accepted from {0}", handler.RemoteEndPoint.ToString());

                    var state = new StateObject
                    {
                        buffer = new byte[StateObject.BufferSize],
                        WorkSocket = handler
                    };
                    _ = handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
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

            if (_acceptedClient != null)
            {
                try
                {
                    if (_acceptedClient.Connected)
                        _acceptedClient.Shutdown(SocketShutdown.Both);
                }
                catch { }
                try
                {
                    _acceptedClient.Close();
                }
                catch { }
                _acceptedClient = null;
            }
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

                    // Assign the connected socket to _acceptedClient
                    _acceptedClient = _client;

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
                _acceptedClient = _client;

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
                var state = new StateObject
                {
                    WorkSocket = _acceptedClient
                };

                _ = _acceptedClient.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);

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
            Socket handler = state.WorkSocket ?? _acceptedClient;

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
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                    IsReceiving = false;
                }
            }
            catch (ObjectDisposedException e1)
            {
                HandleException(e1);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public byte[] ReceiveBytes(int timeout)
        {
            if (State != ASocketStates.Open) return null;

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[1024];
                int readBytes;

                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (_acceptedClient != null && _acceptedClient.Available > 0)
                        if ((readBytes = _acceptedClient.Receive(byteData)) > 0)
                            return byteData;
                }

                return byteData;
            }
            catch (Exception e)
            {
                HandleException(e);
                return null;
            }
        }

        public string Receive(int timeout)
        {
            if (State != ASocketStates.Open) return null;

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[1024];
                int readBytes;

                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (_acceptedClient != null && _acceptedClient.Available > 0)
                        if ((readBytes = _acceptedClient.Receive(byteData)) > 0)
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

        public string Receive(int timeout, string terminator)
        {
            if (State != ASocketStates.Open) return null;

            var stop = new Stopwatch();
            stop.Restart();
            try
            {
                var byteData = new byte[4098];
                var sb = new StringBuilder();
                int bytesRead;
                while (stop.ElapsedMilliseconds < timeout)
                {
                    if (_acceptedClient != null && _acceptedClient.Available > 0)
                    {
                        if ((bytesRead = _acceptedClient.Receive(byteData)) > 0)
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

            try
            {
                var byteData = Encoding.ASCII.GetBytes(data);

                _ = _acceptedClient.Send(byteData);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void Send(byte[] data)
        {
            if (State != ASocketStates.Open) return;

            try
            {
                _ = _acceptedClient.Send(data);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private void StartSend(string data)
        {
            var byteData = Encoding.ASCII.GetBytes(data);

            _ = _acceptedClient.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), _acceptedClient);
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
            if (_acceptedClient == null) return false;

            if (_acceptedClient.Poll(0, SelectMode.SelectRead))
            {
                var buff = new byte[1];
                if (_acceptedClient.Receive(buff, SocketFlags.Peek) == 0)
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
