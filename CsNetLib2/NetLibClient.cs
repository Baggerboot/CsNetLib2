using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsNetLib2.Transfer;

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CsNetLib2
{
    public delegate void ClientDataAvailable(string data);
    public delegate void Disconnected(Exception reason);
    public delegate void LogEvent(string data);

    public class NetLibClient : ITransmittable
    {
        private TcpClient Client { get; set; }
        private byte[] buffer;
        private readonly TransferProtocol protocol;
        private Stream ConnectionStream;
        private object writeLock = new object();
        private bool writeInProgress = false;

        public event DataAvailabeEvent OnDataAvailable;
        public event BytesAvailableEvent OnBytesAvailable;
        public event Disconnected OnDisconnect;
        public event LogEvent OnLogEvent;
        public event LocalPortKnownEvent OnLocalPortKnown;

        private static int clientCount;
        private bool disconnectKnown;

        public bool Connected { get { return Client.Connected; } }
        public byte[] Delimiter
        {
            get
            {
                try
                {
                    var protocol = (DelimitedProtocol)this.protocol;
                    return protocol.Delimiter;
                }
                catch (InvalidCastException)
                {
                    throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
                }
            }
            set
            {
                try
                {
                    var protocol = (DelimitedProtocol)this.protocol;
                    protocol.Delimiter = value;
                }
                catch (InvalidCastException)
                {
                    throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
                }
            }
        }

        public NetLibClient(TransferProtocolType protocolType, Encoding encoding, IPEndPoint localEndPoint = null)
        {
            protocol = new TransferProtocolFactory().CreateTransferProtocol(protocolType, encoding, new Action<string>(Log));
            if (localEndPoint != null)
            {
                Client = new TcpClient(localEndPoint);
            }
            else
            {
                Client = new TcpClient();
            }

            Delimiter = new byte[] { 13, 10 };
            ++clientCount;
        }

        private void Log(string message)
        {
            if (OnLogEvent != null)
            {
                OnLogEvent(message);
            }
        }

        private void ProcessDisconnect(Exception reason)
        {
            if (disconnectKnown) return;
            disconnectKnown = true;

            Client.Close();
            if (OnDisconnect != null)
            {
                OnDisconnect(reason);
            }
        }
        public bool SendBytes(byte[] buffer)
        {
            buffer = protocol.FormatData(buffer);
            try
            {
                while (writeInProgress)
                {
                    Log("Waiting for a previous write to finish before starting the next one...");
                    Thread.Sleep(100);
                }
                lock (writeLock)
                {
                    writeInProgress = true;
                    ConnectionStream.BeginWrite(buffer, 0, buffer.Length, SendCallback, null);
                }
                
                return true;
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch (InvalidOperationException e)
            {
                ProcessDisconnect(e);
                return false;
            }
        }
        public bool Send(string data, long clientId)
        {
            return Send(data);
        }
        public bool Send(string data)
        {
            var buffer = protocol.EncodingType.GetBytes(data);
            return SendBytes(buffer);
        }
        public void Disconnect()
        {
            if (Client != null)
            {
                disconnectKnown = true;
                Client.Close();
                if (OnDisconnect != null)
                {
                    OnDisconnect(null);
                }
            }
        }
        public void DisconnectWithoutEvent()
        {
            if (Client != null)
            {
                disconnectKnown = true;
                Client.Close();
            }
        }
        public void SendCallback(IAsyncResult ar)
        {
            try
            {
                ConnectionStream.EndWrite(ar);
                lock (writeLock)
                {
                    writeInProgress = false;
                }
            }
            catch (ObjectDisposedException e)
            {
                ProcessDisconnect(e);
            }
        }
        public async Task ConnectAsync(string hostname, int port, bool useEvents = true)
        {
            var t = Client.ConnectAsync(hostname, port);
            await t;
            if (OnLocalPortKnown != null)
            {
                t = Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
            }
            ConnectionStream = Client.GetStream();
            var stream = ConnectionStream;
            buffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
            }
        }
        public void Connect(string hostname, int port, bool useEvents = true)
        {
            if (Client.Connected)
            {
                throw new InvalidOperationException("Unable to connect: client is already connected");
            }

            Client.Connect(hostname, port);
            if (OnLocalPortKnown != null)
            {
                Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
            }
            ConnectionStream = Client.GetStream();
            var stream = ConnectionStream;
            buffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
            }
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }
            Log("Server certificate error: " + sslPolicyErrors.ToString());
            return false;
        }

        public async Task ConnectSecureAsync(string hostname, int port, bool useEvents = true, bool validateServerCertificate = true)
        {
            var t = Client.ConnectAsync(hostname, port);
            await t;
            if (OnLocalPortKnown != null)
            {
                t = Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
            }
            var sslStream = new SslStream(Client.GetStream(), false, validateServerCertificate ? new RemoteCertificateValidationCallback(ValidateServerCertificate) : null, null);
            var result = sslStream.AuthenticateAsClientAsync(hostname);
            ConnectionStream = sslStream;

            buffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                await result;
                sslStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
            }
        }
        public void ConnectSecure(string hostname, int port, bool useEvents = true, bool validateServerCertificate = true)
        {
            if (Client.Connected)
            {
                throw new InvalidOperationException("Unable to connect: client is already connected");
            }

            Client.Connect(hostname, port);
            if (OnLocalPortKnown != null)
            {
                Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
            }
            var sslStream = new SslStream(Client.GetStream(), false, validateServerCertificate ? new RemoteCertificateValidationCallback(ValidateServerCertificate) : null, null);
            sslStream.AuthenticateAsClient(hostname);
            ConnectionStream = sslStream;

            buffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                sslStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
            }
        }

        private void ReadCallback(IAsyncResult result)
        {
            int read;
            try
            {
                read = ConnectionStream.EndRead(result);
            }
            catch (IOException e)
            {
                ProcessDisconnect(e);
                return;
            }
            catch (NullReferenceException)
            {
                if (Connected)
                {
                    throw;
                }
                return;
            }
            if (read == 0)
            {
                Client.Close();
            }
            var containers = protocol.ProcessData(buffer, read, 0);
            foreach (var container in containers)
            {
                if (OnDataAvailable != null)
                {
                    OnDataAvailable(container.Text, 0);
                } if (OnBytesAvailable != null)
                {
                    OnBytesAvailable(container.Bytes, 0);
                }
            }
            try
            {
                ConnectionStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
            }
            catch (ObjectDisposedException e)
            {
                ProcessDisconnect(e);
                return;
            }
            catch (IOException e)
            {
                ProcessDisconnect(e);
                return;
            }
        }
        public void AwaitConnect()
        {
            while (!Client.Connected)
            {
                Thread.Sleep(5);
            }
        }

        public List<DataContainer> Read()
        {
            var read = ConnectionStream.Read(buffer, 0, buffer.Length);
            return protocol.ProcessData(buffer, read, 0);
        }

        public Stream GetStream()
        {
            return ConnectionStream;
        }
    }
}
