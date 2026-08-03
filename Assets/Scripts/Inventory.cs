using System;
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    private readonly List<KartPart> parts = new List<KartPart>();

    public IReadOnlyList<KartPart> Parts => parts;

    public event Action<KartPart> PartAdded;

    public void AddPart(KartPart part)
    {
        if (part == null)
            return;

        parts.Add(part);

        PartAdded?.Invoke(part);
    }
}