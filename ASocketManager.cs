using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AsyncSocket
{
    public class ASocketManager : ASocket
    {
        public event EventHandler MessageEvent;

        private string MessageTerminator = string.Empty;
        private string StartRegexPattern = string.Empty;
        private string EndRegexPattern = string.Empty;

        private bool UseRegex;

        public void StartReceiveMessages(string terminator)
        {
            MessageTerminator = terminator;
            UseRegex = false;

            _ = ReceiveData.Clear();
            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }
        public void StartReceiveMessages(string startRegexPattern, string endRegexPattern)
        {
            StartRegexPattern = startRegexPattern;
            EndRegexPattern = endRegexPattern;
            UseRegex = true;

            _ = ReceiveData.Clear();

            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }

        private StringBuilder ReceiveData = new StringBuilder();
        private object ReceiveLock = new object();

        private void ASocketManager_ReceiveEvent(byte[] buffer, string msg)
        {
            lock (ReceiveLock)
            {
                _ = ReceiveData.Append(msg);

                if (!UseRegex)
                {
                    if (ReceiveData.ToString().Contains(MessageTerminator))
                    {
                        var last = 1;
                        if (ReceiveData.ToString().EndsWith(MessageTerminator))
                            last = 0;

                        var spl = $"{ReceiveData}".Split(new string[1] { MessageTerminator }, StringSplitOptions.RemoveEmptyEntries);
                        _ = ReceiveData.Clear();

                        var len = spl.Length - last;

                        for (var i = 0; i < len; i++)
                        {
                            var data = $"{spl[i]}{MessageTerminator}";
                            MessageEvent?.Invoke(data, null);
                        }

                        if (last == 1)
                            _ = ReceiveData.Append(spl[len]);
                    }
                }
                else
                {
                    var reg = new Regex($"{StartRegexPattern}(?s)(.*?){EndRegexPattern}");

                    var found = false;
                    foreach (Match match in reg.Matches(ReceiveData.ToString()))
                    {
                        MessageEvent?.Invoke(match.Value, null);
                        found = true;
                    }

                    //This needs to be handled better. Could be clearing partial messages.
                    if (found) _ = ReceiveData.Clear();
                }
            }
        }
    }
}
