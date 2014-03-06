namespace CsNetLib2
{
	public delegate void DataAvailabeEvent(string data, long clientId);
	public delegate void BytesAvailableEvent(byte[] bytes, long clientId);
	public delegate void LocalPortKnownEvent(int port);
}