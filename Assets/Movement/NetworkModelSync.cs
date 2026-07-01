using UnityEngine;
using NativeWebSocket;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

[Serializable]
public class TankState
{
    public float rx;
    public float ry;
    public float rz;

    public float explode;
}

public class NetworkModelSync : MonoBehaviour
{
    [Header("Networking")]
    public bool isHost = false;

    [Tooltip("Clients should enter the Host's IP here.\nThe Host will automatically overwrite this with its own IP.")]
    public string serverIP = "ws://127.0.0.1:8080";

    [Header("References")]
    public ExplodeView explodeView;

    private WebSocket websocket;

    private Quaternion targetRotation;
    private float targetExplode;

    private bool hasReceivedData = false;

    private Quaternion lastRotation;
    private float lastExplode;

    private float sendTimer = 0f;
    private const float sendRate = 0.05f;

    async void Start()
    {
        targetRotation = transform.rotation;

        if (explodeView != null)
            targetExplode = explodeView.explodeAmount;

        // Host automatically connects to itself
        if (isHost)
        {
            string ip = GetLocalIPAddress();

            serverIP = "ws://" + ip + ":8080";

            Debug.Log("====================================");
            Debug.Log("HOST IP:");
            Debug.Log(ip);
            Debug.Log("Clients connect to:");
            Debug.Log(serverIP);
            Debug.Log("====================================");
        }

        websocket = new WebSocket(serverIP);

        websocket.OnOpen += () =>
        {
            Debug.Log("Connected to " + serverIP);
        };

        websocket.OnError += (e) =>
        {
            Debug.LogError("WebSocket Error: " + e);
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("Disconnected");
        };

        websocket.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);

            TankState state = JsonUtility.FromJson<TankState>(json);

            targetRotation = Quaternion.Euler(
                state.rx,
                state.ry,
                state.rz);

            targetExplode = state.explode;

            hasReceivedData = true;
        };

        await websocket.Connect();

        lastRotation = transform.rotation;

        if (explodeView != null)
            lastExplode = explodeView.explodeAmount;
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif

        if (websocket == null)
            return;

        if (isHost)
        {
            sendTimer += Time.deltaTime;

            if (sendTimer >= sendRate)
            {
                sendTimer = 0f;

                bool rotationChanged =
                    Quaternion.Angle(lastRotation, transform.rotation) > 0.1f;

                bool explodeChanged = false;

                if (explodeView != null)
                    explodeChanged =
                        Mathf.Abs(lastExplode - explodeView.explodeAmount) > 0.001f;

                if (rotationChanged || explodeChanged)
                {
                    SendState();

                    lastRotation = transform.rotation;

                    if (explodeView != null)
                        lastExplode = explodeView.explodeAmount;
                }
            }
        }
        else
        {
            if (!hasReceivedData)
                return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 15f);

            if (explodeView != null)
            {
                explodeView.SetExplodeAmount(
                    Mathf.Lerp(
                        explodeView.explodeAmount,
                        targetExplode,
                        Time.deltaTime * 15f));
            }
        }
    }

    async void SendState()
    {
        if (websocket.State != WebSocketState.Open)
            return;

        TankState state = new TankState();

        Vector3 euler = transform.eulerAngles;

        state.rx = euler.x;
        state.ry = euler.y;
        state.rz = euler.z;

        if (explodeView != null)
            state.explode = explodeView.explodeAmount;

        string json = JsonUtility.ToJson(state);

        await websocket.SendText(json);
    }

    async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }

    string GetLocalIPAddress()
    {
        foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up)
                continue;

            if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (UnicastIPAddressInformation ip in network.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.Address.ToString();
                }
            }
        }

        return "127.0.0.1";
    }
}