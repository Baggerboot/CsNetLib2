using System.Collections.Generic;
using System.Text;

namespace CsNetLib2
{
	public abstract class TransferProtocol
	{
		public Encoding EncodingType { get; private set; }

		protected TransferProtocol(Encoding encodingType)
		{
			EncodingType = encodingType;
		}

		protected string EncodeText(byte[] data)
		{
			return EncodingType.GetString(data);
		}
		protected byte[] DecodeText(string data)
		{
			return EncodingType.GetBytes(data);
		}
		protected string EncodeText(byte[] data, int index, int count)
		{
			return EncodingType.GetString(data, index, count);
		}

		/// <summary>
		/// Processes the incoming data. Once the data has been processed, events are fired and the data is passed with them.
		/// </summary>
		public abstract List<DataContainer> ProcessData(byte[] buffer, int read, long clientId);

		/// <summary>
		/// Formats the data about to be sent so is formatted according to the protocol.
		/// </summary>
		/// <param name="buffer">The data that is about to be sent.</param>
		/// <returns>The formatted data, to be sent.</returns>
		public abstract byte[] FormatData(byte[] buffer);
	}
}
