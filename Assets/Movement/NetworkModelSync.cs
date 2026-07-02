using UnityEngine;
using NativeWebSocket;
using System;

[Serializable]
public class TransformPacket
{
    public float px;
    public float py;
    public float pz;

    // Rotation is sent as a raw quaternion (not euler) to avoid
    // wrap-around artifacts near +/-180 degrees.
    public float qx;
    public float qy;
    public float qz;
    public float qw;

    public float sx;
    public float sy;
    public float sz;
}

public class NetworkModelSync : MonoBehaviour
{
    [Tooltip("When true, isHost is decided automatically: this machine is the " +
             "host if an active hand-gesture controller is running in the scene.")]
    public bool autoDetectHost = true;

    public bool isHost = false;

    public string serverIP = "ws://127.0.0.1:8080";

    [Tooltip("How quickly spectators interpolate toward the host's transform.")]
    public float lerpSpeed = 15f;

    [Tooltip("Seconds between host transform broadcasts.")]
    public float sendInterval = 0.05f;

    private WebSocket websocket;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;

    // Spectators must not move the model until they have actually received
    // a packet, otherwise they lerp toward uninitialized (zero) values and
    // the model collapses to the origin / zero scale (invisible).
    private bool hasReceived;

    private float sendTimer;

    private void Awake()
    {
        // The machine driving the model with hand gestures is the host.
        // Detect that by the presence of an active gesture controller.
        if (autoDetectHost)
        {
            TankHandController handController =
                FindObjectOfType<TankHandController>();

            isHost = handController != null && handController.isActiveAndEnabled;

            Debug.Log("[NetworkModelSync] Auto-detected role: " +
                      (isHost ? "HOST (hand gestures)" : "SPECTATOR"));
        }

        // Seed targets from the current transform BEFORE the first Update,
        // so nothing ever lerps toward (0,0,0) position/scale.
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        targetScale = transform.localScale;
    }

    async void Start()
    {
        websocket = new WebSocket(serverIP);

        websocket.OnOpen += () =>
        {
            Debug.Log("[NetworkModelSync] Connected");
        };

        websocket.OnError += (e) =>
        {
            Debug.LogWarning("[NetworkModelSync] Error: " + e);
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("[NetworkModelSync] Closed");
        };

        websocket.OnMessage += (bytes) =>
        {
            // Host controls the model and ignores inbound packets so it
            // can never be yanked around by a spectator's stale state.
            if (isHost)
                return;

            string json = System.Text.Encoding.UTF8.GetString(bytes);

            TransformPacket packet =
                JsonUtility.FromJson<TransformPacket>(json);

            if (packet == null)
                return;

            targetPosition = new Vector3(
                packet.px,
                packet.py,
                packet.pz);

            targetRotation = new Quaternion(
                packet.qx,
                packet.qy,
                packet.qz,
                packet.qw);

            targetScale = new Vector3(
                packet.sx,
                packet.sy,
                packet.sz);

            hasReceived = true;
        };

        await websocket.Connect();
    }

    async void SendTransform()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
            return;

        TransformPacket packet = new TransformPacket();

        packet.px = transform.position.x;
        packet.py = transform.position.y;
        packet.pz = transform.position.z;

        Quaternion r = transform.rotation;
        packet.qx = r.x;
        packet.qy = r.y;
        packet.qz = r.z;
        packet.qw = r.w;

        packet.sx = transform.localScale.x;
        packet.sy = transform.localScale.y;
        packet.sz = transform.localScale.z;

        string json = JsonUtility.ToJson(packet);

        try
        {
            await websocket.SendText(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetworkModelSync] Send failed: " + e.Message);
        }
    }

    void Update()
    {
        if (websocket == null)
            return;

#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif

        if (isHost)
        {
            sendTimer += Time.deltaTime;

            if (sendTimer >= sendInterval)
            {
                sendTimer = 0f;
                SendTransform();
            }

            return;
        }

        // Spectator: only interpolate once we've received real data.
        if (!hasReceived)
            return;

        float t = Time.deltaTime * lerpSpeed;

        transform.position =
            Vector3.Lerp(transform.position, targetPosition, t);

        transform.rotation =
            Quaternion.Slerp(transform.rotation, targetRotation, t);

        transform.localScale =
            Vector3.Lerp(transform.localScale, targetScale, t);
    }

    async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }
}
