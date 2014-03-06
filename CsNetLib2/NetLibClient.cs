using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

namespace CsNetLib2
{
	public delegate void ClientDataAvailable(string data);
	public delegate void Disconnected();
	public delegate void LogEvent(string data);

	public class NetLibClient : ITransmittable
	{
		public TcpClient Client { get; private set; }
		private byte[] buffer;
		private TransferProtocol Protocol;

		public event DataAvailabeEvent OnDataAvailable;
		public event BytesAvailableEvent OnBytesAvailable;
		public event Disconnected OnDisconnect;
		public event LogEvent OnLogEvent;
		public event LocalPortKnownEvent OnLocalPortKnown;

		public bool Connected { get { return Client.Connected; } }
		public byte[] Delimiter
		{
			get
			{
				try {
					var protocol = (DelimitedProtocol)Protocol;
					return protocol.Delimiter;
				} catch (InvalidCastException) {
					throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
				}
			}
			set
			{
				try {
					var protocol = (DelimitedProtocol)Protocol;
					protocol.Delimiter = value;
				} catch (InvalidCastException) {
					throw new InvalidOperationException("Unable to set the delimiter: Protocol is not of type DelimitedProtocol");
				}
			}
		}

		public NetLibClient(TransferProtocols protocol, Encoding encoding)
		{
			Protocol = new TransferProtocolFactory().CreateTransferProtocol(protocol, encoding, new Action<string>(Log));
			Client = new TcpClient();
			Delimiter = new byte[] { 13, 10 };
		}

		private void Log(string message)
		{
			if (OnLogEvent != null) {
				OnLogEvent(message);
			}
		}

		private void ProcessDisconnect()
		{
			Client.Close();
			if (OnDisconnect != null) {
				OnDisconnect();
			}
		}
		public bool SendBytes(byte[] buffer)
		{
			buffer = Protocol.FormatData(buffer);
			try {
				Client.GetStream().BeginWrite(buffer, 0, buffer.Length, SendCallback, null);
				return true;
			} catch (NullReferenceException) {
				return false;
			} catch (InvalidOperationException) {
				ProcessDisconnect();
				return false;
			}
		}
		public bool Send(string data, long clientId)
		{
			return Send(data);
		}
		public bool Send(string data)
		{
			byte[] buffer = Protocol.EncodingType.GetBytes(data);
			return SendBytes(buffer);
		}
		public void Disconnect()
		{
			if (Client != null) {
				Client.Close();
				if (OnDisconnect != null) {
					OnDisconnect();
				}
			}
		}
		public void DisconnectWithoutEvent()
		{
			if (Client != null) Client.Close();
		}
		public void SendCallback(IAsyncResult ar)
		{
			try {
				Client.GetStream().EndWrite(ar);
			} catch (ObjectDisposedException) {
				ProcessDisconnect();
			}
		}
		public async Task ConnectAsync(string hostname, int port, bool useEvents = true)
		{
			Task t = Client.ConnectAsync(hostname, port);
			await t;
			if (OnLocalPortKnown != null) {
				t = Task.Run(()=>OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
			}
			NetworkStream stream = Client.GetStream();
			buffer = new byte[Client.ReceiveBufferSize];
			if (useEvents) {
				stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			}
		}
		public void Connect(string hostname, int port, bool useEvents = true)
		{
			Client.Connect(hostname, port);
			if (OnLocalPortKnown != null) {
				Task.Run(() => OnLocalPortKnown(((IPEndPoint)Client.Client.LocalEndPoint).Port));
			}
			NetworkStream stream = Client.GetStream();
			buffer = new byte[Client.ReceiveBufferSize];
			if (useEvents) {
				stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			}
		}

		private void ReadCallback(IAsyncResult result)
		{
			NetworkStream networkStream = null;
			try {
				networkStream = Client.GetStream();
			} catch (ObjectDisposedException) {
				ProcessDisconnect();
				return;
			}
			int read;
			try {
				read = networkStream.EndRead(result);
			} catch (System.IO.IOException) {
				ProcessDisconnect();
				return;
			}
			if (read == 0) {
				Client.Close();
			}
			var containers = Protocol.ProcessData(buffer, read, 0);
			foreach (var container in containers) {
				if (OnDataAvailable != null) {
					OnDataAvailable(container.Text, 0);
				} if (OnBytesAvailable != null) {
					OnBytesAvailable(container.Bytes, 0);
				}
			}
			try {
				networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, Client);
			} catch (ObjectDisposedException) {
				ProcessDisconnect();
				return;
			}
		}
		public void AwaitConnect()
		{
			while (!Client.Connected) {
				System.Threading.Thread.Sleep(5);
			}
		}

		public List<DataContainer> Read()
		{
			int read = Client.GetStream().Read(buffer, 0, buffer.Length);
			return Protocol.ProcessData(buffer, read, 0);
		}

		public NetworkStream GetStream()
		{
			return Client.GetStream();
		}
	}
}
