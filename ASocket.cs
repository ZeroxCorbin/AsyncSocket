using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncSocket
{

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
    public class ASocket
    {
        public bool IsConnected { get; private set; } = false;
        public bool IsReceiving { get; private set; } = false;

        public delegate void SimpleDelegate();
        public delegate void ReceiveEventDelegate(byte[] buffer, string msg);

        public event SimpleDelegate ConnectEvent;
        public event SimpleDelegate CloseEvent;
        public event ReceiveEventDelegate ReceiveEvent;
        public event EventHandler ExceptionEvent;

        private Socket client;
        private Exception lastException;

        public bool GetException(out Exception exception)
        {
            exception = lastException;
            return lastException != null;
        }

        protected void HandleException(Exception e)
        {
            Console.WriteLine(e.ToString());

            IsConnected = false;
            IsReceiving = false;

            lastException = e;

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
                client = new System.Net.Sockets.Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                // Connect to the remote endpoint.  
                var connectResult = client.BeginConnect(remoteEP, null, null);

                _ = connectResult.AsyncWaitHandle.WaitOne(timeout, true);

                if (client == null) return false;

                if (client.Connected)
                {
                    client.EndConnect(connectResult);

                    IsConnected = true;

                    _ = Task.Run(() => ConnectEvent?.Invoke());

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
        public void Close(bool noEvent = false)
        {
            IsConnected = false;

            if (client == null) return;

            if (client.Connected)
                client.Shutdown(SocketShutdown.Both);

            if (client == null) return;

            client.Close();
            client = null;

            if (!noEvent)
                CloseEvent?.Invoke();
        }

        public void StartConnect(string host, int port) => StartConnect(new ASocketSettings($"{host}:{port}"));
        public void StartConnect(ASocketSettings settings)
        {
            Close();

            var remoteEP = new IPEndPoint(settings.IPAddress, settings.Port);

            // Create a TCP/IP socket.  
            client = new System.Net.Sockets.Socket(settings.IPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Connect to the remote endpoint.  
            _ = client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), client);
        }
        public void CancelConnect()
        {
            if (client == null) return;

            if (!client.Connected)
            {
                Close(true);
            }
        }
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                // Socket client = (Socket)ar.AsyncState;
                if (client == null)
                {
                    IsConnected = false;
                    return;
                }

                // Complete the connection.  
                client.EndConnect(ar);

                Console.WriteLine("Socket connected to {0}",
                    client.RemoteEndPoint.ToString());

                // Signal that the connection has been made.
                IsConnected = true;
                _ = Task.Run(() => ConnectEvent?.Invoke());
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void StartReceive()
        {
            if (!IsConnected) return;

            try
            {
                // Create the state object.  
                var state = new StateObject();

                // Begin receiving the data from the remote device.  
                _ = client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
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
                if (client == null)
                {
                    IsReceiving = false;
                    return;
                }

                // Retrieve the state object and the client socket
                // from the asynchronous state object.  
                var state = (StateObject)ar.AsyncState;

                // Read data from the remote device.  
                var bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    IsReceiving = true;

                    var msg = Encoding.ASCII.GetString(state.buffer, 0, bytesRead);

                    ReceiveEvent?.Invoke(state.buffer, msg);

                    Array.Clear(state.buffer, 0, StateObject.BufferSize);

                    // Get the rest of the data.  
                    _ = (client?.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
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
                IsConnected = false;
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
            if (!IsConnected) return null;

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
                return null;
            }
        }

        public string Receive(int timeout)
        {
            if (!IsConnected) return null;

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

        public string Receive(int timeout, string terminator)
        {
            if (!IsConnected) return null;

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
                        //Array.Clear(byteData, 0, byteData.Length);
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
            if (!IsConnected) return;

            try
            {
                // Convert the string data to byte data using ASCII encoding.  
                var byteData = Encoding.ASCII.GetBytes(data);

                // Begin sending the data to the remote device.  
                _ = client.Send(byteData);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void Send(byte[] data)
        {
            if (!IsConnected) return;

            try
            {
                // Begin sending the data to the remote device.  
                _ = client.Send(data);
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
            _ = client.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), client);
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
            if (client == null) return false;

            // Detect if client disconnected
            if (client.Poll(0, SelectMode.SelectRead))
            {
                var buff = new byte[1];
                if (client.Receive(buff, SocketFlags.Peek) == 0)
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
