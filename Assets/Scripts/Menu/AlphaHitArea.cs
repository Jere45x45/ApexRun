using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hace que un botón con forma (por ejemplo, con el corte en diagonal) solo
/// responda al mouse sobre la parte pintada de su imagen, y no en las
/// esquinas transparentes del rectángulo.
///
/// Usa alphaHitTestMinimumThreshold de la Image, que Unity no guarda en la
/// escena: por eso se aplica al arrancar. Las imágenes del botón (las de
/// todos sus estados) tienen que tener Read/Write activado en el importador.
/// </summary>
[RequireComponent(typeof(Image))]
public class AlphaHitArea : MonoBehaviour
{
    [Tooltip("Transparencia mínima (0 a 1) para que el mouse cuente sobre la imagen. 0,5 = la mitad.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float minimumAlpha = 0.5f;

    private void Awake()
    {
        Image image = GetComponent<Image>();

        if (image.sprite == null || !image.sprite.texture.isReadable)
        {
            Debug.LogWarning($"AlphaHitArea: la imagen de {name} necesita Read/Write activado en el importador.", this);
            return;
        }

        image.alphaHitTestMinimumThreshold = minimumAlpha;
    }
}
