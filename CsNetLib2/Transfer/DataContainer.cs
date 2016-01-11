namespace CsNetLib2
{
	public struct DataContainer
	{
		public byte[] Bytes;
		public string Text;

        public override string ToString()
        {
            return string.Format("{0} bytes: \"{1}\"", Bytes.Length, Text);
        }
	}
}
