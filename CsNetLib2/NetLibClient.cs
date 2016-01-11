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
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;

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
        private object writeLock = new object();
        private bool writeInProgress = false;
        private Queue<DataContainer> messageQueue = new Queue<DataContainer>();

        public event DataAvailabeEvent OnDataAvailable;
        public event BytesAvailableEvent OnBytesAvailable;
        public event Disconnected OnDisconnect;
        public event LogEvent OnLogEvent;
        public event LocalPortKnownEvent OnLocalPortKnown;

        private static int clientCount;
        private bool disconnectKnown;

        public bool Connected { get { return Client.Connected; } }
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
            ++clientCount;
        }

        private void Log(string message)
        {
            if (OnLogEvent != null)
            {
                OnLogEvent(message);
            }
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
	        catch (IOException e)
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

        /// <summary>
        /// Called when the client has finished sending data.
        /// </summary>
        /// <param name="ar"></param>
        private void SendCallback(IAsyncResult ar)
        {
	        try
	        {
		        ConnectionStream.EndWrite(ar);
		        // Allow the next write to take place
		        lock (writeLock)
		        {
			        writeInProgress = false;
		        }
	        }
	        catch (ObjectDisposedException e)
	        {
		        ProcessDisconnect(e);
	        }
	        catch (IOException e)
	        {
		        if (e.GetType() == typeof (ObjectDisposedException))
		        {
			        ProcessDisconnect(e);
		        }
		        else
		        {
			        throw;
		        }
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

            Log(string.Format("SSL Connection established: Cipher: {0}-bit {1}; KEX: {2} {3}-bit; Hash: {4} {5}-bit",
                sslStream.CipherStrength, sslStream.CipherAlgorithm,
                sslStream.KeyExchangeAlgorithm, sslStream.KeyExchangeStrength,
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
                // Shouldn't we do some disconnect handling here, as well as return from the method?
                // This situation most likely never occurs, so put a Break() on it.
                Debugger.Break();

				ProcessDisconnect(new InvalidOperationException("End of stream reached."));
                // If we can't do that, we'll have to throw, since we're not sure what to do at this point.
                throw new InvalidOperationException("Unsupported read received. Connection state: " + (Connected ? "Connected" : "Disconnected"));
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
            var t = new Thread(ReadLoop);
            t.Name = "CSNetLib Message Processing Thread";
            t.Start();
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
                    if (OnDataAvailable != null)
                    {
                        OnDataAvailable(message.Text, 0);
                    }
                    if (OnBytesAvailable != null)
                    {
                        OnBytesAvailable(message.Bytes, 0);
                    }
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
