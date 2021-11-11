using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsyncSocket
{
    public class ASocketSettings
    {
        public ASocketSettings(string connectionString) { ConnectionString = connectionString; }

        public string ConnectionString { get; set; } = string.Empty;
        public bool IsConnectionStringValid => IsIPAddressValid & IsPortValid;

        private bool GetIPAddressString(out string ip)
        {
            ip = string.Empty;
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                string value;
                if (ConnectionString.Contains(":"))
                {
                    value = ConnectionString.Split(':')[0];
                    if (string.IsNullOrEmpty(value))
                    {
                        return false;
                    }
                }
                else
                {
                    value = ConnectionString;
                }

                return _IsIPAddressValid(ip = value);
            }
            return false;

        }
        public string IPAddressString { get { GetIPAddressString(out string test); return test; } }
        public System.Net.IPAddress IPAddress => GetIPAddressString(out string test) ? System.Net.IPAddress.Parse(test) : null;
        public bool IsIPAddressValid => GetIPAddressString(out string _);

        private bool GetPortString(out string port)
        {
            port = "-1";
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                if (ConnectionString.Contains(":"))
                {
                    string value = ConnectionString.Split(':')[1];
                    if (string.IsNullOrEmpty(value))
                    {
                        return false;
                    }
                    else
                    {
                        return _IsPortValid(port = value);
                    }
                }
            }
            return false;

        }
        public string PortString { get { GetPortString(out string test); return test; } }
        public int Port => GetPortString(out string test) ? int.Parse(test) : -1;
        public bool IsPortValid => GetPortString(out string _);

        //public System.Net.IPEndPoint RemoteEP => (IsIPAddressValid & IsPortValid) ? new System.Net.IPEndPoint(IPAddress, Port) : null;

        private bool _IsIPAddressValid(string ip)
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"^((0|1[0-9]{0,2}|2[0-9]?|2[0-4][0-9]|25[0-5]|[3-9][0-9]?)\.){3}(0|1[0-9]{0,2}|2[0-9]?|2[0-4][0-9]|25[0-5]|[3-9][0-9]?)$");

            return regex.IsMatch(ip);
        }
        private bool _IsPortValid(string port)
        {
                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"^([0-9]{1,4}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$");
                return regex.IsMatch(port);
        }
    }
}
