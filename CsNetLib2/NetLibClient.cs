using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsNetLib2.Transfer;

using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace CsNetLib2
{
    public delegate void ClientDataAvailable(string data);
    public delegate void Disconnected(Exception reason);
    public delegate void LogEvent(string data);

    public class NetLibClient : ITransmittable
    {
        private TcpClient Client { get; set; }
        private byte[] readBuffer;
        private readonly TransferProtocol protocol;
        private Stream ConnectionStream;

        private Queue<byte[]> outgoingQueue = new Queue<byte[]>();
        private Queue<DataContainer> messageQueue = new Queue<DataContainer>();
        private ManualResetEvent outgoingMessageAvailable = new ManualResetEvent(false);

        public event DataAvailabeEvent OnDataAvailable;
        public event BytesAvailableEvent OnBytesAvailable;
        public event Disconnected OnDisconnect;
        public event LogEvent OnLogEvent;
        public event LocalPortKnownEvent OnLocalPortKnown;

        private bool disconnectKnown;

        public bool Connected => Client.Connected;
        public int ReadLoopSleepTime { get; private set; }
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

        public NetLibClient(TransferProtocolType protocolType, Encoding encoding, IPEndPoint localEndPoint = null, int readLoopSleepTime = 50)
        {
            ReadLoopSleepTime = readLoopSleepTime;
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
        }

        private void Log(string message)
        {
            OnLogEvent?.Invoke(message);
        }

        /// <summary>
        /// Handle a server disconnect
        /// </summary>
        /// <param name="reason"></param>
        private void ProcessDisconnect(Exception reason)
        {
            if (disconnectKnown) return;
            disconnectKnown = true;

            Client.Close();
            OnDisconnect?.Invoke(reason);
        }

        public bool SendBytes(byte[] buffer)
        {
            buffer = protocol.FormatData(buffer);
            lock (outgoingMessageAvailable)
            {
                outgoingQueue.Enqueue(buffer);
                outgoingMessageAvailable.Set();
            }
            return true;
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

        /// <summary>
        /// Disconnects the client, and fires the OnDisconnect event.
        /// </summary>
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

        /// <summary>
        /// Disconnects the client without firing an OnDisconnect event.
        /// </summary>
        public void DisconnectWithoutEvent()
        {
            if (Client != null)
            {
                disconnectKnown = true;
                Client.Close();
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
            readBuffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                EnterReadLoop();
                stream.BeginRead(readBuffer, 0, readBuffer.Length, ReadCallback, Client);
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
            readBuffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                EnterReadLoop();
                stream.BeginRead(readBuffer, 0, readBuffer.Length, ReadCallback, Client);
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

            readBuffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                await result;
                Log(string.Format("SSL Connection established: Cipher: {1} ({0}-bit); KEX: {2} ({3}-bit); Hash: {4} ({5}-bit)",
                    sslStream.CipherStrength, sslStream.CipherAlgorithm,
                    sslStream.KeyExchangeAlgorithm, sslStream.KeyExchangeStrength,
                    sslStream.HashAlgorithm, sslStream.HashStrength));
                EnterReadLoop();
                sslStream.BeginRead(readBuffer, 0, readBuffer.Length, ReadCallback, Client);
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

            var kex = (int)sslStream.KeyExchangeAlgorithm == 44550 ? "ECDH" : sslStream.KeyExchangeAlgorithm.ToString();
            Log(string.Format("SSL Connection established: Cipher: {0}-bit {1}; KEX: {2} {3}-bit; Hash: {4} {5}-bit",
                sslStream.CipherStrength, sslStream.CipherAlgorithm,
                kex, sslStream.KeyExchangeStrength,
                sslStream.HashAlgorithm, sslStream.HashStrength));
            ConnectionStream = sslStream;

            readBuffer = new byte[Client.ReceiveBufferSize];
            if (useEvents)
            {
                EnterReadLoop();
                sslStream.BeginRead(readBuffer, 0, readBuffer.Length, ReadCallback, Client);
            }
        }

        /// <summary>
        /// Called whenever there is incoming data
        /// </summary>
        /// <param name="result"></param>
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
                // An NRE while we're still connected is an error...
                if (Connected)
                {
                    throw;
                }
                // ...but if the client or server has closed the connection, an NRE will be thrown 
                // because the disconnection will occur while we're still trying to read data,
                // causing the read to fail as a result.
                // That's fine, and to be expected, so we can safely ignore this exception.
                return;
            }
            if (read == 0)
            {
                Client.Close();
                ProcessDisconnect(new InvalidOperationException("End of stream reached."));
            }
            // Eventually we should just give the protocol a queue to read from, and shove it in there, 
            // which will allow us to return to reading the next data more quickly.
            var decodedMessage = protocol.EncodingType.GetString(readBuffer, 0, read);

            var containers = protocol.ProcessData(readBuffer, read, 0);
            lock (messageQueue)
            {
                foreach (var container in containers)
                {
                    messageQueue.Enqueue(container);
                }
            }
            // Read's finished, so we should get started on the next one.
            try
            {
                ConnectionStream.BeginRead(readBuffer, 0, readBuffer.Length, ReadCallback, Client);
            }
            catch (SocketException e)
            {
                ProcessDisconnect(e);
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

        private void EnterReadLoop()
        {
            Task.Run((Action)ReadLoop);
            Task.Run((Action)WriteLoop);
        }

        private void WriteLoop()
        {
            while (Connected || outgoingQueue.Count > 0)
            {
                byte[] buffer;
                // Wait for a signal indicating that a message is available
                outgoingMessageAvailable.WaitOne();
                lock (outgoingMessageAvailable)
                {
                    buffer = new byte[outgoingQueue.Sum(m => m.Length)];
                    var offset = 0;
                    while (outgoingQueue.Count > 0)
                    {
                        var slice = outgoingQueue.Dequeue();
                        Array.Copy(slice, 0, buffer, offset, slice.Length);
                        offset += slice.Length;
                    }
                    // Reset the signal
                    outgoingMessageAvailable.Reset();
                }
                try
                {
                    ConnectionStream.Write(buffer, 0, buffer.Length);
                }
                catch (NullReferenceException)
                {
                    // The connection has already been disposed
                }
                catch (InvalidOperationException e)
                {
                    ProcessDisconnect(e);
                }
                catch (IOException e)
                {
                    ProcessDisconnect(e);
                }
            }
        }

        private void ReadLoop()
        {
            while (Connected || messageQueue.Count > 0)
            {

                if (messageQueue.Count > 0)
                {
                    Monitor.Enter(messageQueue);
                    var message = messageQueue.Dequeue();
                    Monitor.Exit(messageQueue);
                    OnDataAvailable?.Invoke(message.Text, 0);
                    OnBytesAvailable?.Invoke(message.Bytes, 0);
                }
                else
                {
                    if (ReadLoopSleepTime > 0)
                    {
                        Thread.Sleep(ReadLoopSleepTime);
                    }
                }
            }
            Log("Leaving read loop");
        }

        public void AwaitConnect()
        {
            while (!Client.Connected)
            {
                Thread.Sleep(5);
            }
        }


        public Stream GetStream()
        {
            return ConnectionStream;
        }
    }
}
