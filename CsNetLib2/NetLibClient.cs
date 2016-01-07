using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsNetLib2.Transfer;

namespace CsNetLib2
{
	public delegate void ClientDataAvailable(string data);
	public delegate void Disconnected(Exception reason);
	public delegate void LogEvent(string data);

	public class NetLibClient : ITransmittable
	{
		public TcpClient Client { get; private set; }
		private byte[] buffer;
		private readonly TransferProtocol protocol;

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
				try {
					var protocol = (DelimitedProtocol)this.protocol;
					return protocol.Delimiter;
				} catch (InvalidCastException) {
					throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
				}
			}
			set
			{
				try {
					var protocol = (DelimitedProtocol)this.protocol;
					protocol.Delimiter = value;
				} catch (InvalidCastException) {
					throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
				}
			}
		}

		public NetLibClient(TransferProtocolType protocolType, Encoding encoding, IPEndPoint localEndPoint = null)
		{
			protocol = new TransferProtocolFactory().CreateTransferProtocol(protocolType, encoding, new Action<string>(Log));
			if (localEndPoint != null) {
				Client = new TcpClient(localEndPoint);
			} else {
				Client = new TcpClient();
			}
			
			Delimiter = new byte[] { 13, 10 };
			++clientCount;
		}

		private void Log(string message)
		{
			if (OnLogEvent != null) {
				OnLogEvent(message);
			}
		}

		private void ProcessDisconnect(Exception reason)
		{
			if (disconnectKnown) return;
			disconnectKnown = true;

			Client.Close();
			if (OnDisconnect != null) {
				OnDisconnect(reason);
			}
		}
		public bool SendBytes(byte[] buffer)
		{
			buffer = protocol.FormatData(buffer);
			try {
				Client.GetStream().BeginWrite(buffer, 0, buffer.Length, SendCallback, null);
				return true;
			} catch (NullReferenceException) {
				return false;
			} catch (InvalidOperationException e) {
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
			if (Client != null) {
				disconnectKnown = true;
				Client.Close();
				if (OnDisconnect != null) {
					OnDisconnect(null);
				}
			}
		}
		public void DisconnectWithoutEvent()
		{
			if (Client != null) {
				disconnectKnown = true;
				Client.Close();
			}
		}
		public void SendCallback(IAsyncResult ar)
		{
			try {
				Client.GetStream().EndWrite(ar);
			} catch (ObjectDisposedException e) {
				ProcessDisconnect(e);
			}
		}
		public async Task ConnectAsync(string hostname, int port, bool useEvents = true)
		{
			var t = Client.ConnectAsync(hostname, port);
			await t;
			if (OnLocalPortKnown != null) {
				t = Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
			}
			var stream = Client.GetStream();
			buffer = new byte[Client.ReceiveBufferSize];
			if (useEvents) {
				stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			}
		}
		public void Connect(string hostname, int port, bool useEvents = true)
		{
			if (Client.Connected) {
				throw new InvalidOperationException("Unable to connect: client is already connected");
			}

			Client.Connect(hostname, port);
			if (OnLocalPortKnown != null) {
				Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
			}
			var stream = Client.GetStream();
			buffer = new byte[Client.ReceiveBufferSize];
			if (useEvents) {
				stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			}
		}

		private void ReadCallback(IAsyncResult result)
		{
			NetworkStream networkStream;
			try {
				networkStream = Client.GetStream();
			} catch (ObjectDisposedException e) {
				ProcessDisconnect(e);
				return;
			} catch (InvalidOperationException e) {
				if (Connected) {
					ProcessDisconnect(e);
				}
				return;
			}
			int read;
			try {
				read = networkStream.EndRead(result);
			} catch (IOException e) {
				ProcessDisconnect(e);
				return;
			} catch (NullReferenceException)
			{
				if (Connected) {
					throw;
				}
				return;
			}
			if (read == 0) {
				Client.Close();
			}
			var containers = protocol.ProcessData(buffer, read, 0);
			foreach (var container in containers) {
				if (OnDataAvailable != null) {
					OnDataAvailable(container.Text, 0);
				} if (OnBytesAvailable != null) {
					OnBytesAvailable(container.Bytes, 0);
				}
			}
			try {
				networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			} catch (ObjectDisposedException e) {
				ProcessDisconnect(e);
				return;
			} catch (IOException e) {
				ProcessDisconnect(e);
				return;
			}
		}
		public void AwaitConnect()
		{
			while (!Client.Connected) {
				Thread.Sleep(5);
			}
		}

		public List<DataContainer> Read()
		{
			var read = Client.GetStream().Read(buffer, 0, buffer.Length);
			return protocol.ProcessData(buffer, read, 0);
		}

		public NetworkStream GetStream()
		{
			return Client.GetStream();
		}
	}
}
