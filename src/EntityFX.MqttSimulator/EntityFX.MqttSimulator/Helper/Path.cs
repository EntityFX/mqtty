namespace EntityFX.MqttY.Helper
{
    public class Path<T>
        where T : notnull
    {
        public T Source { get; set; }

        public T Destination { get; set; }

        public int Cost { get; set; }

        public Path(T source, T destination)
        {
            Source = source;
            Destination = destination;
        }
    }


}
