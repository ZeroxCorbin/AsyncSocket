using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AsyncSocket
{
    public partial class ASocketManager : ASocket
    {
        public delegate void MessageEventHandler(string message);
        public event MessageEventHandler MessageEvent;

        [ObservableProperty] private string message;

        private string _messageTerminator = string.Empty;
        private string _startRegexPattern = string.Empty;
        private string _endRegexPattern = string.Empty;

        private bool _useRegex;

        private StringBuilder _receiveData = new StringBuilder();
        private object _receiveLock = new object();

        public void StartReceiveMessages(string terminator)
        {
            _messageTerminator = terminator;
            _useRegex = false;

            _ = _receiveData.Clear();
            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }
        public void StartReceiveMessages(string startRegexPattern, string endRegexPattern)
        {
            _startRegexPattern = startRegexPattern;
            _endRegexPattern = endRegexPattern;
            _useRegex = true;

            _ = _receiveData.Clear();

            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }

        public void StopReceiveMessages()
        {
            ReceiveEvent -= ASocketManager_ReceiveEvent;

            lock (_receiveLock)
                _ = _receiveData.Clear();
        }

        private void ASocketManager_ReceiveEvent(byte[] buffer, string msg)
        {
            lock (_receiveLock)
            {
                _ = _receiveData.Append(msg);

                if (!_useRegex)
                {
                    if (_receiveData.ToString().Contains(_messageTerminator))
                    {
                        var last = 1;
                        if (_receiveData.ToString().EndsWith(_messageTerminator))
                            last = 0;

                        var spl = $"{_receiveData}".Split(new string[1] { _messageTerminator }, StringSplitOptions.RemoveEmptyEntries);
                        _ = _receiveData.Clear();

                        var len = spl.Length - last;

                        for (var i = 0; i < len; i++)
                        {
                            Message = spl[i];
                            MessageEvent?.Invoke(spl[i]);
                        }

                        if (last == 1)
                            _ = _receiveData.Append(spl[len]);
                    }
                }
                else
                {
                    var reg = new Regex($"{_startRegexPattern}(?s)(.*?){_endRegexPattern}");

                    var found = false;
                    foreach (Match match in reg.Matches(_receiveData.ToString()))
                    {
                        MessageEvent?.Invoke(match.Value);
                        Message = match.Value;
                        found = true;
                    }

                    //This needs to be handled better. Could be clearing partial messages.
                    if (found) _ = _receiveData.Clear();
                }
            }
        }
    }
}
