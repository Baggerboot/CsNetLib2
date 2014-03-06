using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsNetLib2
{
	public class StreamingProtocol : TransferProtocol
	{
		public StreamingProtocol(Encoding encoding) : base(encoding) { }

		public override byte[] FormatData(byte[] data)
		{
			return data; // Streaming protocol doesn't care about formatting
		}

		public override List<DataContainer> ProcessData(byte[] buffer, int read, long clientId)
		{
			return new List<DataContainer> {
				new DataContainer {
					Bytes = buffer,
					Text = EncodeText(buffer)
				}
			};
		}
	}
}
