using UnityEngine;

[System.Serializable]
public class ModelSlot
{
    [SerializeField]
    private Transform mountPoint;

    private GameObject currentInstance;

    public Transform MountPoint => mountPoint;

    public GameObject CurrentInstance => currentInstance;

    public void SetModel(GameObject prefab)
    {
        if (currentInstance != null)
        {
            Object.Destroy(currentInstance);
        }

        if (prefab == null)
        {
            currentInstance = null;
            return;
        }

        if (mountPoint == null)
        {
            Debug.LogError(
                "ModelSlot no tiene un Mount Point asignado."
            );

            currentInstance = null;
            return;
        }

        currentInstance = Object.Instantiate(
            prefab,
            mountPoint
        );

        currentInstance.transform.localPosition =
            Vector3.zero;

        currentInstance.transform.localRotation =
            Quaternion.identity;

        currentInstance.transform.localScale =
            Vector3.one;
    }
}