using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsNetLib2
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
				case TransferProtocolType.SetSize:
					return new SetSizeProtocol(encoding);
				default:
					return null;
			}
		}
	}
}
