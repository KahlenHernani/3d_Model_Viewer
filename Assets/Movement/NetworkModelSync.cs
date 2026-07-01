using UnityEngine;
using NativeWebSocket;
using System;

[Serializable]
public class TransformPacket
{
    public float px;
    public float py;
    public float pz;

    public float rx;
    public float ry;
    public float rz;

    public float sx;
    public float sy;
    public float sz;
}

public class NetworkModelSync : MonoBehaviour
{
    public bool isHost = false;

    public string serverIP = "ws://172.20.10.2:8080";

    private WebSocket websocket;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;

    float sendTimer;

    async void Start()
    {
        websocket = new WebSocket(serverIP);

        websocket.OnOpen += () =>
        {
            Debug.Log("Connected");
        };

        websocket.OnError += (e) =>
        {
            Debug.Log(e);
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("Closed");
        };

        websocket.OnMessage += (bytes) =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);

            TransformPacket packet =
                JsonUtility.FromJson<TransformPacket>(json);

            targetPosition = new Vector3(
                packet.px,
                packet.py,
                packet.pz);

            targetRotation = Quaternion.Euler(
                packet.rx,
                packet.ry,
                packet.rz);

            targetScale = new Vector3(
                packet.sx,
                packet.sy,
                packet.sz);
        };

        await websocket.Connect();

        targetPosition = transform.position;
        targetRotation = transform.rotation;
        targetScale = transform.localScale;
    }

    async void SendTransform()
    {
        if (websocket.State != WebSocketState.Open)
            return;

        TransformPacket packet = new TransformPacket();

        packet.px = transform.position.x;
        packet.py = transform.position.y;
        packet.pz = transform.position.z;

        packet.rx = transform.eulerAngles.x;
        packet.ry = transform.eulerAngles.y;
        packet.rz = transform.eulerAngles.z;

        packet.sx = transform.localScale.x;
        packet.sy = transform.localScale.y;
        packet.sz = transform.localScale.z;

        string json = JsonUtility.ToJson(packet);

        await websocket.SendText(json);
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif

        if (isHost)
        {
            sendTimer += Time.deltaTime;

            if (sendTimer > 0.05f)
            {
                sendTimer = 0f;
                SendTransform();
            }
        }
        else
        {
            transform.position =
                Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    Time.deltaTime * 15);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * 15);

            transform.localScale =
                Vector3.Lerp(
                    transform.localScale,
                    targetScale,
                    Time.deltaTime * 15);
        }
    }

    async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }
}