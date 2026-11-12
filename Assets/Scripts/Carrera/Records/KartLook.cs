using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Las piezas que se ven de un kart (motor, ruedas, kit aero y volante), por
/// partID. Van con cada marca de la tabla de la contrarreloj (TimeTrialRecords),
/// así el fantasma y la repetición muestran el kart con el que se hizo la vuelta.
/// El chasis no está: es fijo.
/// </summary>
[Serializable]
public struct KartParts
{
    public string Engine;
    public string Wheels;
    public string AeroKit;
    public string SteeringWheel;

    public static readonly PartType[] Types =
    {
        PartType.Engine,
        PartType.Wheels,
        PartType.AeroKit,
        PartType.SteeringWheel
    };

    public bool IsEmpty =>
        string.IsNullOrEmpty(Engine) && string.IsNullOrEmpty(Wheels) &&
        string.IsNullOrEmpty(AeroKit) && string.IsNullOrEmpty(SteeringWheel);

    public string Get(PartType type)
    {
        switch (type)
        {
            case PartType.Engine: return Engine;
            case PartType.Wheels: return Wheels;
            case PartType.AeroKit: return AeroKit;
            case PartType.SteeringWheel: return SteeringWheel;
            default: return null;
        }
    }

    public void Set(PartType type, string partID)
    {
        switch (type)
        {
            case PartType.Engine: Engine = partID; break;
            case PartType.Wheels: Wheels = partID; break;
            case PartType.AeroKit: AeroKit = partID; break;
            case PartType.SteeringWheel: SteeringWheel = partID; break;
        }
    }

    /// <summary>Las piezas elegidas en el Catálogo (con las que corre el jugador).</summary>
    public static KartParts FromLoadout()
    {
        var parts = new KartParts();

        foreach (PartType type in Types)
            parts.Set(type, KartLoadoutStore.GetSelectedID(type));

        return parts;
    }

    /// <summary>Las piezas instaladas ahora en un kart.</summary>
    public static KartParts FromKart(KartConfigurationController controller)
    {
        var parts = new KartParts();

        if (controller == null || controller.Configuration == null)
            return parts;

        foreach (PartType type in Types)
        {
            KartPart part = controller.GetInstalledPart(type);
            parts.Set(type, part != null ? part.partID : null);
        }

        return parts;
    }

    /// <summary>True si las dos tienen lo mismo en cada pieza que dicen (vacío = cualquiera).</summary>
    public bool Matches(KartParts other)
    {
        foreach (PartType type in Types)
        {
            string a = Get(type);
            string b = other.Get(type);

            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a != b)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Para copiar el modelo de un kart con otras piezas (fantasmas y repetición de
/// la tabla): le pone esas piezas al kart, deja copiar sus mallas y le vuelve a
/// poner las suyas, todo en el mismo cuadro.
/// Ojo: ModelSlot borra los modelos viejos con Destroy, que espera al final del
/// cuadro. Mientras tanto siguen ahí, así que quien copia tiene que saltearlos
/// (la lista que recibe build).
/// </summary>
public static class KartLook
{
    private static readonly FieldInfo[] SlotFields = FindSlotFields();

    private static FieldInfo[] FindSlotFields()
    {
        var fields = new List<FieldInfo>();

        foreach (FieldInfo field in typeof(KartModelController).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.FieldType == typeof(ModelSlot))
                fields.Add(field);
        }

        return fields.ToArray();
    }

    /// <summary>
    /// Viste el kart con esas piezas, llama a build y lo deja como estaba. Las
    /// piezas vacías o que no están en el catálogo quedan como las tiene el kart.
    /// build recibe los modelos que hay que saltear al copiar.
    /// </summary>
    public static void WithParts(KartBehaviour kart, KartParts parts, Action<ICollection<Transform>> build)
    {
        var skip = new List<Transform>();

        NetworkKart network = kart != null ? kart.GetComponent<NetworkKart>() : null;
        KartConfigurationController controller = network != null ? network.ConfigurationController : null;
        PartCatalog catalog = network != null ? network.Catalog : null;
        KartModelController models = kart != null ? kart.GetComponentInChildren<KartModelController>(true) : null;

        if (controller == null || catalog == null || models == null || controller.Configuration == null ||
            parts.IsEmpty || KartParts.FromKart(controller).Matches(parts))
        {
            build(skip);
            return;
        }

        // Lo que ya colgaba de los puntos de montaje y no es un modelo de pieza (se copia siempre).
        var keep = new HashSet<Transform>();

        foreach (ModelSlot slot in Slots(models))
        {
            foreach (Transform child in slot.MountPoint)
            {
                if (slot.CurrentInstance == null || child != slot.CurrentInstance.transform)
                    keep.Add(child);
            }
        }

        KartParts original = KartParts.FromKart(controller);
        Install(controller, catalog, parts);

        try
        {
            // Los modelos de antes siguen ahí hasta el final del cuadro.
            foreach (ModelSlot slot in Slots(models))
            {
                foreach (Transform child in slot.MountPoint)
                {
                    if (!keep.Contains(child) && (slot.CurrentInstance == null || child != slot.CurrentInstance.transform))
                        skip.Add(child);
                }
            }

            build(skip);
        }
        finally
        {
            Install(controller, catalog, original);
        }
    }

    /// <summary>True si el renderer cuelga de alguno de esos modelos.</summary>
    public static bool IsSkipped(Renderer renderer, ICollection<Transform> skip)
    {
        if (skip == null || skip.Count == 0)
            return false;

        foreach (Transform root in skip)
        {
            if (root != null && renderer.transform.IsChildOf(root))
                return true;
        }

        return false;
    }

    private static IEnumerable<ModelSlot> Slots(KartModelController models)
    {
        foreach (FieldInfo field in SlotFields)
        {
            if (field.GetValue(models) is ModelSlot slot && slot.MountPoint != null)
                yield return slot;
        }
    }

    private static void Install(KartConfigurationController controller, PartCatalog catalog, KartParts parts)
    {
        foreach (PartType type in KartParts.Types)
        {
            string id = parts.Get(type);

            if (string.IsNullOrEmpty(id))
                continue;

            KartPart part = catalog.GetPartByID(id);

            if (part == null || part.PartType != type || controller.GetInstalledPart(type) == part)
                continue;

            controller.InstallPart(part);
        }
    }
}
