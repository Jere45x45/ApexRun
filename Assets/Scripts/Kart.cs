using UnityEngine;

public class Kart
{
    public KartConfiguration Configuration { get; private set; }

    public KartStats Stats { get; private set; }

    public Kart(KartConfiguration configuration)
    {
        Configuration = configuration;
        Stats = KartBuilder.Build(configuration);
    }
}