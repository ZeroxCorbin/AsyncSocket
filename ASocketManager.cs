using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AsyncSocket
{
    public class ASocketManager : ASocket
    {
        public event EventHandler MessageEvent;

        private string MessageTerminator = string.Empty;

        public void StartReceiveMessages(string terminator)
        {
            MessageTerminator = terminator;
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
                string msg = (string)sender;

                if (msg.Contains(MessageTerminator))
                {
                    int last = 1;
                    if (msg.EndsWith(MessageTerminator))
                        last = 0;

                    string[] spl = $"{ReceiveData}{msg}".Split(new string[1] { MessageTerminator }, StringSplitOptions.RemoveEmptyEntries);
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
                else
                {
                    ReceiveData.Append(msg);
                }
            }

        }
    }
}
