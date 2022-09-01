using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

            ReceiveData.Clear();
            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }
        public void StartReceiveMessages(string startRegexPattern, string endRegexPattern)
        {
            StartRegexPattern = startRegexPattern;
            EndRegexPattern = endRegexPattern;
            UseRegex = true;

            ReceiveData.Clear();

            ReceiveEvent -= ASocketManager_ReceiveEvent;
            ReceiveEvent += ASocketManager_ReceiveEvent;
            StartReceive();
        }
        StringBuilder ReceiveData = new StringBuilder();

        object ReceiveLock = new object();

        private void ASocketManager_ReceiveEvent(object sender, EventArgs e)
        {
            lock (ReceiveLock)
            {
                ReceiveData.Append((string)sender);

                if (!UseRegex)
                {
                    if (ReceiveData.ToString().Contains(MessageTerminator))
                    {
                        int last = 1;
                        if (ReceiveData.ToString().EndsWith(MessageTerminator))
                            last = 0;

                        string[] spl = $"{ReceiveData}".Split(new string[1] { MessageTerminator }, StringSplitOptions.RemoveEmptyEntries);
                        ReceiveData.Clear();

                        int len = spl.Length - last;

                        for (int i = 0; i < len; i++)
                        {
                            string data = $"{spl[i]}{MessageTerminator}";
                            MessageEvent?.Invoke(data, null);
                        }

                        if(last == 1)
                            ReceiveData.Append(spl[len]);
                    }
                }
                else
                {
                    Regex reg = new Regex($"{StartRegexPattern}(?s)(.*?){EndRegexPattern}");

                    foreach (Match match in reg.Matches(ReceiveData.ToString()))
                    {
                        MessageEvent?.Invoke(match.Value, null);
                    }

                    //This needs to be handled better. Could be clearing partial messages.
                    ReceiveData.Clear();
                }

            }

        }
    }
}
