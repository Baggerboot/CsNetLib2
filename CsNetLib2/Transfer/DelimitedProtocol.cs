using System;
using System.Collections.Generic;
using System.Text;

namespace CsNetLib2.Transfer
{
	public class DelimitedProtocol : TransferProtocol
	{
		private byte[] delimiter = { 0x00 };
		public byte[] Delimiter { get { return delimiter; } set { delimiter = value; } }

		private byte[] retain = new byte[0];

		public DelimitedProtocol(Encoding encoding, Action<string> logCallback) : base(encoding) {
		}

        /// <summary>
        /// Formats a message so it complies with this protocol.
        /// It does so by adding the correct delimiter to the end of the byte array.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
		public override byte[] FormatData(byte[] data)
		{
			var newData = new byte[data.Length + Delimiter.Length]; // Add space for delimiter
			Array.Copy(data, newData, data.Length); // Put the data back in
			Array.Copy(Delimiter, 0, newData, data.Length, Delimiter.Length);
			return newData;
		}

		public override List<DataContainer> ProcessData(byte[] buffer, int read, long clientId)
		{
			var containers = new List<DataContainer>();

			if (retain.Length != 0) { // There's still data left over
				var oldBuf = buffer; // Temporarily put the new data aside
				buffer = new byte[read + retain.Length]; // Expand the buffer to fit both the old and the new data
				Array.Copy(retain, buffer, retain.Length); // Put the old data in first
				Array.Copy(oldBuf, 0, buffer, retain.Length, read); // Now put the new data back in
				read += retain.Length;
                retain = new byte[0];
			}

			var beginIndex = 0; // This is where the next message starts
			for (var i = beginIndex; i < read; i++) { // Iterate over buffer
				if (buffer[i] == Delimiter[0]) { // We've found a delimiter
					var validDelimiter = true;
					i++;
					for (var j = 1; j < Delimiter.Length; j++, i++) { // Check if the next delimiter bytes occur as well, if applicable
						if (i >= buffer.Length) {
							validDelimiter = false;
							break;
						}
						if (buffer[i] != Delimiter[j]) {
							validDelimiter = false;
							break;
						}
					}
					if (validDelimiter) {
						var data = ProcessMessage(buffer, beginIndex, i - Delimiter.Length); // Process a message from the begin index to the current position
						containers.Add(data);
						beginIndex = i; // Since we've found a new delimiter, set the begin index equal to its location
						i--;
					}
				}
				if (i == read - 1) {
					retain = new byte[read - beginIndex];
					Array.Copy(buffer, beginIndex, retain, 0, read - beginIndex); // Take any left over data and keep it for next usage
				}
			}
			return containers;
		}
		private DataContainer ProcessMessage(byte[] buffer, int begin, int end)
		{
			var data = new byte[end-begin];
			for(var i = 0; i < data.Length; i++){
				data[i] = buffer[i + begin];
			}
			var str = EncodeText(data);
			return new DataContainer
			{
				Bytes = data,
				Text = str
			};
		}
	}
}
