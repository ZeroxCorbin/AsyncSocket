using CommunityToolkit.Mvvm.ComponentModel;
using System;
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

    // State object for receiving data from remote device.  
    public class StateObject
    {
        // Client socket.  
        //public Socket workSocket = null;
        // Size of receive buffer.  
        public const int BufferSize = 4096;
        // Receive buffer.  
        public byte[] buffer = new byte[BufferSize];
        // Received data string.  
        //public StringBuilder sb = new StringBuilder();
    }
    public partial class ASocket : ObservableObject
    {
        [ObservableProperty] private bool isReceiving = false;

        [ObservableProperty] private ASocketStates state = ASocketStates.Closed;
        [ObservableProperty] private Exception lastException;

        public delegate void SimpleDelegate();
        public delegate void ReceiveEventDelegate(byte[] buffer, string msg);

        public event ReceiveEventDelegate ReceiveEvent;
        public event EventHandler ExceptionEvent;

        private Socket _client;


        public bool GetException(out Exception exception)
        {
            exception = LastException;
            return LastException != null;
        }

        protected void HandleException(Exception e)
        {
            Console.WriteLine(e.ToString());

            State = ASocketStates.Exception;
            IsReceiving = false;

            LastException = e;

            Close();

            _ = Task.Run(() => ExceptionEvent?.Invoke(e, null));
        }

        public bool Connect(string host, int port, int timeout = 5000) => Connect(new ASocketSettings($"{host}:{port}"), timeout);
        public bool Connect(ASocketSettings settings, int timeout = 5000)
        {
            Close();

            try
            {
                var remoteEP = new IPEndPoint(settings.IPAddress, settings.Port);

                // Create a TCP/IP socket.  
                _client = new System.Net.Sockets.Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                // Connect to the remote endpoint.  
                IAsyncResult connectResult = _client.BeginConnect(remoteEP, null, null);

                _ = connectResult.AsyncWaitHandle.WaitOne(timeout, true);

                if (_client == null) return false;

                if (_client.Connected)
                {
                    _client.EndConnect(connectResult);

                    State = ASocketStates.Open;

                    return true;
                }
                else
                {
                    State = ASocketStates.Closed;
                    throw new Exception("Connection timeout.");
                }
            }
            catch (Exception e)
            {
                HandleException(e);
                return false;
            }
        }
        public void Close()
        {
            State = ASocketStates.Closed;
            if (_client == null) return;

            if (_client.Connected)
                _client.Shutdown(SocketShutdown.Both);


            if (_client == null) return;

            _client.Close();
            _client = null;
        }

        public void StartConnect(string host, int port) => StartConnect(new ASocketSettings($"{host}:{port}"));
        public void StartConnect(ASocketSettings settings)
        {
            Close();

            var remoteEP = new IPEndPoint(settings.IPAddress, settings.Port);

            // Create a TCP/IP socket.  
            _client = new System.Net.Sockets.Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Connect to the remote endpoint.  
            _ = _client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), _client);
        }
        public void CancelConnect()
        {
            if (_client == null) return;

            if (!_client.Connected)
            {
                Close();
            }
        }
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                // Socket client = (Socket)ar.AsyncState;
                if (_client == null)
                {
                    State = ASocketStates.Closed;
                    return;
                }

                // Complete the connection.  
                _client.EndConnect(ar);

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
                // Create the state object.  
                var state = new StateObject();

                // Begin receiving the data from the remote device.  
                _ = _client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
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
            try
            {
                if (_client == null)
                {
                    IsReceiving = false;
                    return;
                }

                // Retrieve the state object and the client socket
                // from the asynchronous state object.  
                var state = (StateObject)ar.AsyncState;

                // Read data from the remote device.  
                var bytesRead = _client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    IsReceiving = true;

                    var msg = Encoding.ASCII.GetString(state.buffer, 0, bytesRead);

                    ReceiveEvent?.Invoke(state.buffer, msg);

                    Array.Clear(state.buffer, 0, StateObject.BufferSize);

                    // Get the rest of the data.  
                    _ = (_client?.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state));
                }
                else
                {
                    Close();
                    IsReceiving = false;
                }
            }
            catch (ObjectDisposedException)
            {
                IsReceiving = false;
                Close();
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
                    if (_client.Available > 0)
                        if ((readBytes = _client.Receive(byteData)) > 0)
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
                    if (_client.Available > 0)
                        if ((readBytes = _client.Receive(byteData)) > 0)
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
                    if (_client.Available > 0)
                    {
                        //Array.Clear(byteData, 0, byteData.Length);
                        if ((bytesRead = _client.Receive(byteData)) > 0)
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
                // Convert the string data to byte data using ASCII encoding.  
                var byteData = Encoding.ASCII.GetBytes(data);

                // Begin sending the data to the remote device.  
                _ = _client.Send(byteData);
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
                // Begin sending the data to the remote device.  
                _ = _client.Send(data);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private void StartSend(string data)
        {
            // Convert the string data to byte data using ASCII encoding.  
            var byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.  
            _ = _client.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), _client);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                var client = (System.Net.Sockets.Socket)ar.AsyncState;

                // Complete sending the data to the remote device.  
                var bytesSent = client.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                // Signal that all bytes have been sent.  
                //sendDone.Set();
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private bool DetectConnection()
        {
            if (_client == null) return false;

            // Detect if client disconnected
            if (_client.Poll(0, SelectMode.SelectRead))
            {
                var buff = new byte[1];
                if (_client.Receive(buff, SocketFlags.Peek) == 0)
                {
                    // Client disconnected
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
