using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsNetLib2
{
	class SetSizeProtocol : TransferProtocol
	{
		public SetSizeProtocol(Encoding encoding) : base(encoding) { }

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
