using System;
using System.Text;

namespace CsNetLib2.Transfer
{
	class TransferProtocolFactory
	{
		public TransferProtocol CreateTransferProtocol(TransferProtocolType protocol, Encoding encoding, Action<string> logCallback)
		{
			switch (protocol) {
				case TransferProtocolType.Streaming:
					return new StreamingProtocol(encoding);
				case TransferProtocolType.Delimited:
					return new DelimitedProtocol(encoding, logCallback);
				case TransferProtocolType.FixedSize:
					return new FixedSizeProtocol(encoding);
				default:
					return null;
			}
		}
	}
}
