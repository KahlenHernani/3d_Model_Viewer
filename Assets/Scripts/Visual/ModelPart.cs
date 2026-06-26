using UnityEngine;

public class ModelPart : MonoBehaviour
{
    //Stores captured local position and exploded position of the part.
    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Vector3 explodedPosition;
    
    // Read only properties to access and return values.
    public Vector3 LocalPosition => localPosition;
    public Vector3 ExplodedPosition => explodedPosition;


    private void Start()
    {
        // Store initial local position of the part for later use.
        localPosition = transform.localPosition;
    }

    public void SetExplodedPosition(Vector3 position)
    {
        explodedPosition = position;
    }
}