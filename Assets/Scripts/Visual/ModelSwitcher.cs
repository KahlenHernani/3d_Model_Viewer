using UnityEngine;

public class ModelSwitcher : MonoBehaviour
{
    [SerializeField] private GameObject[] modelPrefabs;
    [SerializeField] private Transform spawnPoint;

    private GameObject currentModel;
    private ExplodedViewController explodedViewController;
    private int currentIndex = -1;

    private void Start() {
        // Ensures that everything is properly intialized.
        if(spawnPoint == null)
        {
            return;
        }

        // modelPrefabs must be initialized and contain at least one element.
        if(modelPrefabs == null || modelPrefabs.Length == 0)
        {
            return;
        }

        // If we have at least one model prefab, switch to the first one.
        SwitchModel(0);

    }

    public void SwitchModel(int index) {
        if (modelPrefabs == null || modelPrefabs.Length == 0) {
            return;
        }

        if (index < 0 || index >= modelPrefabs.Length) {
            return;
        }

        if (currentModel != null) {
            Destroy(currentModel);
        }

        GameObject prefab = modelPrefabs[index];

        // Creates a new instance of the selected model at the spawn point.
        currentModel = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);

        // Gets the ExplodedViewController component from the newly instantiated model.
        explodedViewController = currentModel.GetComponentInChildren<ExplodedViewController>();
        currentIndex = index;

        // No child of currentModel has an ExplodedViewController component.
        if(explodedViewController == null)
        {
            // Log a warning message to the console. Harder to debug without it.
            Debug.LogWarning("Failed to find ExplodedViewController in the new model.");
        }
    }
}