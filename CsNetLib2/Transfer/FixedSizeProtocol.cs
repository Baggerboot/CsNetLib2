using System;
using System.Collections.Generic;
using System.Text;

namespace CsNetLib2
{
	class FixedSizeProtocol : TransferProtocol
	{
		public FixedSizeProtocol(Encoding encoding) : base(encoding) { }

		public override byte[] FormatData(byte[] data)
		{
			throw new NotImplementedException("SetSizeProtocol is not implemented yet");
		}

		public override List<DataContainer> ProcessData(byte[] buffer, int read, long clientId)
		{
			throw new NotImplementedException("SetSizeProtocol is not implemented yet");
		}
	}
}
