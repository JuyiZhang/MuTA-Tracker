using System;
using UnityEngine;
//using System.Runtime.Serialization.Formatters.Binary;
#if WINDOWS_UWP
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
#endif

public class TCPClient : MonoBehaviour
{
    #region Unity Functions

    private void Awake()
    {
        ConnectionStatusLED.material.color = Color.red;
    }

    private void Start()
    {
        networkUtils = GetComponent<NetworkUtils>();
        networkUtils.onHostIPRetrieved += OnHostIPFound;
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
        {
            StopConnection();
        }
    }
    #endregion // Unity Functions

    [SerializeField]
    private string port;

    private string hostIPAddress;

    public Renderer ConnectionStatusLED;
    private bool connected = false;
    public bool Connected
    {
        get { return connected; }
    }

    private NetworkUtils networkUtils;

#if WINDOWS_UWP
    StreamSocket socket = null;
    public DataWriter dw;
    public DataReader dr;
#endif

    private async void StartConnection()
    {
        Debug.Log("Starting Connection...");
#if WINDOWS_UWP
        if (socket != null) socket.Dispose();

        try
        {
            socket = new StreamSocket();
            var hostName = new Windows.Networking.HostName(hostIPAddress);
            await socket.ConnectAsync(hostName, port);
            dw = new DataWriter(socket.OutputStream);
            dr = new DataReader(socket.InputStream);
            dr.InputStreamOptions = InputStreamOptions.Partial;
            connected = true;
            ConnectionStatusLED.material.color = Color.green;
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
#endif
    }

    private void StopConnection()
    {
#if WINDOWS_UWP
        dw?.DetachStream();
        dw?.Dispose();
        dw = null;

        dr?.DetachStream();
        dr?.Dispose();
        dr = null;

        socket?.Dispose();
        connected = false;
#endif
    }

#if WINDOWS_UWP
    #region Send Data

    public async void SendPointCloud(float[] pointCloud, long timestamp) {
    
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try {
            dw.WriteString("p"); //header "p" for point cloud
            dw.WriteInt64(timestamp);
            dw.WriteInt64(pointCloud.Length); //length of float elements

            dw.WriteBytes(FloatToBytes(pointCloud)); //write actual data

            await dw.StoreAsync();
            await dw.FlushAsync();
        } catch (Exception ex) {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
        //numSendFrames+=1;
        //serverFeedback="send " + numSendFrames.ToString() + " frames of point cloud";

    }
    
    bool lastMessageSent = true;

    public async void SendLongDepthSensorCombined(ushort[] data1, byte[] data2, float[] pointCloud, long timestamp, Vector3 euler, Vector3 position, Vector3 anchorEuler, Vector3 anchorPosition){
    
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try
        {
            float[] transform = {position.x, position.y, position.z, euler.x, euler.y, euler.z, anchorPosition.x, anchorPosition.y, anchorPosition.z, anchorEuler.x, anchorEuler.y, anchorEuler.z};
            // Write header
            dw.WriteString("c"); // header "c" 

            // Write timestamp
            dw.WriteInt64(timestamp);
            dw.WriteInt32(data1.Length * 2);
            dw.WriteInt32(data2.Length);
            dw.WriteInt32(pointCloud.Length * 4 + 48);
            dw.WriteBytes(UINT16ToBytes(data1));
            dw.WriteBytes(data2);
            dw.WriteBytes(FloatToBytes(pointCloud));
            dw.WriteBytes(FloatToBytes(transform));
            

            // Send out
            await dw.StoreAsync();
            await dw.FlushAsync();
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
    
    }

    public async void SendUINT16Async(ushort[] data, long timestamp)
    {
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try
        {
            // Write header
            dw.WriteString("s"); // header "s" 

            // Write point cloud
            dw.WriteInt64(timestamp);
            dw.WriteInt32(data.Length);
            dw.WriteBytes(UINT16ToBytes(data));

            // Send out
            await dw.StoreAsync();
            await dw.FlushAsync();
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
    }

    public async void SendUINT16Async(ushort[] data1, ushort[] data2, long timestamp)
    {
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try
        {
            // Write header
            dw.WriteString("s"); // header "s" stands for it is ushort array (uint16)

            // Write Length
            dw.WriteInt64(timestamp);
            dw.WriteInt32(data1.Length + data2.Length);

            // Write actual data
            dw.WriteBytes(UINT16ToBytes(data1));
            dw.WriteBytes(UINT16ToBytes(data2));

            // Send out
            await dw.StoreAsync();
            await dw.FlushAsync();
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
    }

    public async void SendSpatialImageAsync(byte[] LFImage, byte[] RFImage, long ts_left, long ts_right)
    {
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try
        {
            // Write header
            dw.WriteString("f"); // header "f"

            // Write Length
            dw.WriteInt32(LFImage.Length + RFImage.Length);
            dw.WriteInt64(ts_left);
            dw.WriteInt64(ts_right);

            // Write actual data
            dw.WriteBytes(LFImage);
            dw.WriteBytes(RFImage);

            // Send out
            await dw.StoreAsync();
            await dw.FlushAsync();
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
    }


    public async void SendSpatialImageAsync(byte[] LRFImage, long ts_left, long ts_right)
    {
        if (!lastMessageSent) return;
        lastMessageSent = false;
        try
        {
            // Write header
            dw.WriteString("f"); // header "f"

            // Write Timestamp and Length
            dw.WriteInt32(LRFImage.Length);
            dw.WriteInt64(ts_left);
            dw.WriteInt64(ts_right);

            // Write actual data
            dw.WriteBytes(LRFImage);

            // Send out
            await dw.StoreAsync();
            await dw.FlushAsync();
        }
        catch (Exception ex)
        {
            SocketErrorStatus webErrorStatus = SocketError.GetStatus(ex.GetBaseException().HResult);
            Debug.Log(webErrorStatus.ToString() != "Unknown" ? webErrorStatus.ToString() : ex.Message);
        }
        lastMessageSent = true;
    }
    #endregion
#endif


    #region Conversion Function
    byte[] UINT16ToBytes(ushort[] data)
    {
        byte[] ushortInBytes = new byte[data.Length * sizeof(ushort)];
        System.Buffer.BlockCopy(data, 0, ushortInBytes, 0, ushortInBytes.Length);
        return ushortInBytes;
    }

    byte[] FloatToBytes(float[] data)
    {
        byte[] floatInBytes = new byte[data.Length * sizeof(float)];
        System.Buffer.BlockCopy(data, 0, floatInBytes, 0, floatInBytes.Length);
        return floatInBytes;
    }

    float[] BytesToFloat(byte[] data)
    {
        float[] bytesInFloat = new float[data.Length / sizeof(float)];
        System.Buffer.BlockCopy(data, 0, bytesInFloat, 0, data.Length);
        return bytesInFloat;
    }
#endregion

    #region Button Callback
    public void ConnectToServerEvent()
    {
        var networkUtil = GetComponent<NetworkUtils>();
        Debug.Log("Begin Connection...");
        networkUtil.syncHostIP();
    }
    #endregion

    #region Delegate Callback
    public void OnHostIPFound()
    {
        hostIPAddress = networkUtils.getHostIP();
        Debug.Log("Host IP is: " + hostIPAddress);
        if (!connected) StartConnection();
        else StopConnection();
    }
    #endregion
}
