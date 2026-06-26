using UnityEngine;

public class ExplodedViewController : MonoBehaviour
{

    [SerializeField] private float explodeDistance = 1.5f;
    
    // Array for each ModelPart component in the model.
    // Stores local and exploded positions.
    private ModelPart[] modelParts;

    private void Start()
    {
        // Populates array with components of type ModelPart
        modelParts = GetComponentsInChildren<ModelPart>();
        CalculateExplodedPositions();

    }
    private void CalculateExplodedPositions()
    {
        if (modelParts == null || modelParts.Length == 0)
        {
            return;
        }

        Vector3 center = Vector3.zero;

        foreach (ModelPart part in modelParts)
        {
            center += part.transform.localPosition;
        }

        center /= modelParts.Length;

        foreach (ModelPart part in modelParts)
        {
            Vector3 direction = part.transform.localPosition - center;
            if (direction == Vector3.zero)
            {
                direction = Vector3.up;
            }

            part.SetExplodedPosition(part.LocalPosition + direction.normalized * explodeDistance);
        }
    }
    public void Explode()
    {
     foreach (ModelPart part in modelParts){
        // Gets explodedPosition from ModelPart public property. Sets localPosition to explodedPosition.
        part.transform.localPosition = part.ExplodedPosition;
     }   

     // Starts at zero
     Vector3 center = Vector3.zero;
     foreach (ModelPart part in modelParts) {
        // Adds the localPosition of each part to the center variable.
            center+=part.transform.localPosition;
        }

        // Finds center by dividing sum of localPositions by the number of parts.
        center /= modelParts.Length;
     
    }

    public void Reassemble()
    {
     foreach (ModelPart part in modelParts){
        //Gets localPosition from ModelPart public property. Sets localPosition to localPosition.
        part.transform.localPosition = part.LocalPosition;
     }   
    }
    
}