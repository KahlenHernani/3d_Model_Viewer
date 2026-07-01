using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadNormal()
    {
        SceneManager.LoadScene("Webcam");
    }

    public void LoadSRD()
    {
        SceneManager.LoadScene("SRD");
    }

    public void Quit()
    {
        Application.Quit();
    }
}