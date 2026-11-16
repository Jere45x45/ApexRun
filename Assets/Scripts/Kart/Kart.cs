public class Kart
{
    public RuntimeKartConfiguration Configuration { get; }

    public KartStats Stats { get; private set; }

    public Kart(RuntimeKartConfiguration configuration)
    {
        Configuration = configuration;
        Rebuild();
    }

    public void Rebuild()
    {
        Stats = KartBuilder.Build(Configuration);
    }
}