namespace CsNetLib2
{
	public interface ITransmittable
	{
		bool Send(string data, long clientId);
		event DataAvailabeEvent OnDataAvailable;
	}
}
