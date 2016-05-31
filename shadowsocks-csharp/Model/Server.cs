using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using SimpleJson;
using Shadowsocks.Controller;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using Shadowsocks.Encryption;

namespace Shadowsocks.Model
{
    public class DnsBuffer
    {
        public IPAddress ip;
        public DateTime updateTime;
        public string host;
        public bool isExpired(string host)
        {
            if (updateTime == null) return true;
            if (this.host != host) return true;
            return (DateTime.Now - updateTime).TotalMinutes > 10;
        }
        public void UpdateDns(string host, IPAddress ip)
        {
            updateTime = DateTime.Now;
            this.ip = new IPAddress(ip.GetAddressBytes());
            this.host = (string)host.Clone();
        }
    }
    public class Connections
    {
        private System.Collections.Generic.Dictionary<Socket, Int32> sockets = new Dictionary<Socket, int>();
        public bool AddRef(Socket socket)
        {
            lock (this)
            {
                if (sockets.ContainsKey(socket))
                {
                    sockets[socket] += 1;
                }
                else
                {
                    sockets[socket] = 1;
                }
                return true;
            }
        }
        public bool DecRef(Socket socket)
        {
            lock (this)
            {
                if (sockets.ContainsKey(socket))
                {
                    sockets[socket] -= 1;
                    if (sockets[socket] == 0)
                    {
                        sockets.Remove(socket);
                    }
                }
                else
                {
                    return false;
                }
                return true;
            }
        }
        public void CloseAll()
        {
            Socket[] s;
            lock (this)
            {
                s = new Socket[sockets.Count];
                sockets.Keys.CopyTo(s, 0);
            }
            foreach (Socket socket in s)
            {
                try
                {
                    //socket.Shutdown(SocketShutdown.Send);
                    socket.Shutdown(SocketShutdown.Both);
                    //socket.Close();
                }
                catch
                {

                }
            }
        }
        public int Count
        {
            get
            {
                return sockets.Count;
            }
        }
    }

    [Serializable]
    public class Server
    {
        public string server;
        public int server_port;
        public int server_udp_port;
        public string password;
        public string method;
        public string obfs;
        public string obfsparam;
        public string remarks_base64;
        public string group;
        public bool tcp_over_udp;
        public bool udp_over_tcp;
        public string protocol;
        //public bool obfs_udp;
        public bool enable;
        public string id;

        private object protocoldata;
        private object obfsdata;
        private ServerSpeedLog serverSpeedLog = new ServerSpeedLog();
        private DnsBuffer dnsBuffer = new DnsBuffer();
        private DnsBuffer dnsTargetBuffer = new DnsBuffer();
        private Connections Connections = new Connections();

        public void CopyServer(Server Server)
        {
            this.serverSpeedLog = Server.serverSpeedLog;
            this.dnsBuffer = Server.dnsBuffer;
            this.dnsTargetBuffer = Server.dnsTargetBuffer;
            this.Connections = Server.Connections;
            this.enable = Server.enable;
        }
        public void SetConnections(Connections Connections)
        {
            this.Connections = Connections;
        }

        public Connections GetConnections()
        {
            return Connections;
        }

        public DnsBuffer DnsBuffer()
        {
            return dnsBuffer;
        }

        public DnsBuffer DnsTargetBuffer()
        {
            return dnsTargetBuffer;
        }

        public ServerSpeedLog ServerSpeedLog()
        {
            return serverSpeedLog;
        }
        public void SetServerSpeedLog(ServerSpeedLog log)
        {
            serverSpeedLog = log;
        }
        public string remarks
        {
            get
            {
                if (this.remarks_base64.Length == 0)
                    return this.remarks_base64;
                string remarks = this.remarks_base64.Replace('-', '+').Replace('_', '/');
                try
                {
                    return Encoding.UTF8.GetString(System.Convert.FromBase64String(remarks));
                }
                catch (FormatException)
                {
                    remarks = remarks_base64;
                    remarks_base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(remarks_base64)).Replace('+', '-').Replace('/', '_');
                    return remarks;
                }
            }
            set
            {
                remarks_base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).Replace('+', '-').Replace('/', '_');
            }
        }
        public string FriendlyName()
        {
            if (string.IsNullOrEmpty(server))
            {
                return I18N.GetString("New server");
            }
            if (string.IsNullOrEmpty(remarks_base64))
            {
                if (server.IndexOf(':') >= 0)
                {
                    return "[" + server + "]:" + server_port;
                }
                else
                {
                    return server + ":" + server_port;
                }
            }
            else
            {
                if (server.IndexOf(':') >= 0)
                {
                    return remarks + " ([" + server + "]:" + server_port + ")";
                }
                else
                {
                    return remarks + " (" + server + ":" + server_port + ")";
                }
            }
        }

        public Server Clone()
        {
            Server ret = new Server();
            ret.server = (string)server.Clone();
            ret.server_port = server_port;
            ret.password = (string)password.Clone();
            ret.method = (string)method.Clone();
            ret.protocol = protocol;
            ret.obfs = (string)obfs.Clone();
            ret.obfsparam = (string)obfsparam.Clone();
            ret.remarks_base64 = (string)remarks_base64.Clone();
            ret.enable = enable;
            ret.udp_over_tcp = udp_over_tcp;
            //ret.obfs_udp = obfs_udp;
            ret.id = id;
            ret.protocoldata = protocoldata;
            ret.obfsdata = obfsdata;
            return ret;
        }

        public Server()
        {
            this.server = "server ip or url";
            this.server_port = 8388;
            this.method = "aes-256-cfb";
            this.obfs = "plain";
            this.obfsparam = "";
            this.password = "0";
            this.remarks_base64 = "";
            this.udp_over_tcp = false;
            this.protocol = "origin";
            //this.obfs_udp = false;
            this.enable = true;
            byte[] id = new byte[16];
            Util.Utils.RandBytes(id, id.Length);
            this.id = BitConverter.ToString(id);
        }

        public Server(string ssURL) : this()
        {
            string[] r1 = Regex.Split(ssURL, "ss://", RegexOptions.IgnoreCase);
            string base64 = r1[1].ToString();
            byte[] bytes = null;
            string data = "";
            if (base64.LastIndexOf('@') > 0)
            {
                data = base64;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        bytes = System.Convert.FromBase64String(base64);
                    }
                    catch (FormatException)
                    {
                        base64 += "=";
                    }
                }
                if (bytes != null)
                {
                    data = Encoding.UTF8.GetString(bytes);
                }
            }
            if (data.Length == 0)
            {
                throw new FormatException();
            }
            try
            {
                int indexLastAt = data.LastIndexOf('@');
                int remarkIndexLastAt = data.IndexOf('#', indexLastAt);
                if (remarkIndexLastAt > 0)
                {
                    if (remarkIndexLastAt + 1 < data.Length)
                    {
                        this.remarks_base64 = data.Substring(remarkIndexLastAt + 1);
                    }
                    data = data.Substring(0, remarkIndexLastAt);
                }
                remarkIndexLastAt = data.IndexOf('/', indexLastAt);
                string param = "";
                if (remarkIndexLastAt > 0)
                {
                    if (remarkIndexLastAt + 1 < data.Length)
                    {
                        param = data.Substring(remarkIndexLastAt + 1);
                    }
                    data = data.Substring(0, remarkIndexLastAt);
                }

                string afterAt = data.Substring(indexLastAt + 1);
                int indexLastColon = afterAt.LastIndexOf(':');
                this.server_port = int.Parse(afterAt.Substring(indexLastColon + 1));
                this.server = afterAt.Substring(0, indexLastColon);

                string beforeAt = data.Substring(0, indexLastAt);
                this.method = "";
                for (bool next = true; next;)
                {
                    string[] parts = beforeAt.Split(new[] { ':' }, 2);
                    if (parts.Length > 1)
                    {
                        try
                        {
                            Obfs.ObfsBase obfs = (Obfs.ObfsBase)Obfs.ObfsFactory.GetObfs(parts[0]);
                            if (obfs.GetObfs().ContainsKey(parts[0]))
                            {
                                int[] p = obfs.GetObfs()[parts[0]];
                                if (p[0] == 1)
                                {
                                    this.protocol = parts[0];
                                }
                                if (p[1] == 1)
                                {
                                    this.obfs = parts[0];
                                }
                            }
                            else
                            {
                                next = false;
                            }
                        }
                        catch
                        {
                            try
                            {
                                IEncryptor encryptor = EncryptorFactory.GetEncryptor(parts[0], "m");
                                encryptor.Dispose();
                                this.method = parts[0];
                                beforeAt = parts[1];
                            }
                            catch
                            {
                            }
                            break;
                        }
                        beforeAt = parts[1];
                    }
                    else
                        break;
                }
                if (this.method.Length == 0)
                    throw new FormatException();
                this.password = beforeAt;
            }
            catch (IndexOutOfRangeException)
            {
                throw new FormatException();
            }
        }

        public bool isEnable()
        {
            return enable;
        }

        public void setEnable(bool enable)
        {
            this.enable = enable;
        }

        public object getObfsData()
        {
            return this.obfsdata;
        }
        public void setObfsData(object data)
        {
            this.obfsdata = data;
        }

        public object getProtocolData()
        {
            return this.protocoldata;
        }
        public void setProtocolData(object data)
        {
            this.protocoldata = data;
        }
    }
}
