using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;

#if ENABLE_WINMD_SUPPORT
using HL2UnityPlugin;
#endif

public class ResearchModeVideoStream : MonoBehaviour
{
#if ENABLE_WINMD_SUPPORT
    HL2ResearchMode researchMode;
#endif

    enum DepthSensorMode
    {
        ShortThrow,
        LongThrow,
        None
    };

    [SerializeField] bool enablePointCloud = true;
    [SerializeField] Camera camera;
    [SerializeField]
    private GameObject debugger;

    TCPClient tcpClient;

    public GameObject longDepthPreviewPlane = null;
    private Material longDepthMediaMaterial = null;
    private Texture2D longDepthMediaTexture = null;
    private byte[] longDepthFrameData = null;

    public GameObject longAbImagePreviewPlane = null;
    private Material longAbImageMediaMaterial = null;
    private Texture2D longAbImageMediaTexture = null;
    private byte[] longAbImageFrameData = null;

    private bool continuousSend = false;

    private Debugger currentDebugger;

    

#if ENABLE_WINMD_SUPPORT
    Windows.Perception.Spatial.SpatialCoordinateSystem unityWorldOrigin;
#endif

    private void Awake()
    {
#if ENABLE_WINMD_SUPPORT
#if UNITY_2020_1_OR_NEWER // note: Unity 2021.2 and later not supported
        IntPtr WorldOriginPtr = UnityEngine.XR.WindowsMR.WindowsMREnvironment.OriginSpatialCoordinateSystem;
        unityWorldOrigin = Marshal.GetObjectForIUnknown(WorldOriginPtr) as Windows.Perception.Spatial.SpatialCoordinateSystem;
        unityWorldOrigin = Windows.Perception.Spatial.SpatialLocator.GetDefault().CreateStationaryFrameOfReferenceAtCurrentLocation().CoordinateSystem;
#else
        IntPtr WorldOriginPtr = UnityEngine.XR.WSA.WorldManager.GetNativeISpatialCoordinateSystemPtr();
        unityWorldOrigin = Marshal.GetObjectForIUnknown(WorldOriginPtr) as Windows.Perception.Spatial.SpatialCoordinateSystem;
#endif
#endif
    }
    void Start()
    {

        currentDebugger = debugger.GetComponent<Debugger>();

        if (longDepthPreviewPlane != null)
        {
            longDepthMediaMaterial = longDepthPreviewPlane.GetComponent<MeshRenderer>().material;
            longDepthMediaTexture = new Texture2D(320, 288, TextureFormat.Alpha8, false);
            longDepthMediaMaterial.mainTexture = longDepthMediaTexture;
        }

        if (longAbImagePreviewPlane != null)
        {
            longAbImageMediaMaterial = longAbImagePreviewPlane.GetComponent<MeshRenderer>().material;
            longAbImageMediaTexture = new Texture2D(320, 288, TextureFormat.Alpha8, false);
            longAbImageMediaMaterial.mainTexture = longAbImageMediaTexture;
        }

        longDepthPreviewPlane.SetActive(true);
        longAbImagePreviewPlane.SetActive(true);

        tcpClient = GetComponent<TCPClient>();

#if ENABLE_WINMD_SUPPORT
        researchMode = new HL2ResearchMode();

        // Initialize Long Depth Sensor
        researchMode.InitializeLongDepthSensor();
        
        researchMode.SetReferenceCoordinateSystem(unityWorldOrigin);
        researchMode.SetPointCloudDepthOffset(0);

        // Depth sensor should be initialized in only one mode
        researchMode.StartLongDepthSensorLoop(enablePointCloud);

#endif
        currentDebugger.AddDebugMessage("Successfully initiated Hololens Researchmode");
        tcpClient.ConnectToServerEvent();
    }

    bool startRealtimePreview = true;
    void LateUpdate()
    {
#if ENABLE_WINMD_SUPPORT

        // update long depth map texture
        if (startRealtimePreview && longDepthPreviewPlane != null && researchMode.LongDepthMapTextureUpdated())
        {
            byte[] frameTexture = researchMode.GetLongDepthMapTextureBuffer();
            if (frameTexture.Length > 0)
            {
                if (longDepthFrameData == null)
                {
                    longDepthFrameData = frameTexture;
                }
                else
                {
                    System.Buffer.BlockCopy(frameTexture, 0, longDepthFrameData, 0, longDepthFrameData.Length);
                }

                longDepthMediaTexture.LoadRawTextureData(longDepthFrameData);
                longDepthMediaTexture.Apply();
            }
        }

        // update long-throw AbImage texture
        if (startRealtimePreview && longAbImagePreviewPlane != null && researchMode.LongAbImageTextureUpdated())
        {
            byte[] frameTexture = researchMode.GetLongAbImageTextureBuffer();
            if (frameTexture.Length > 0)
            {
                if (longAbImageFrameData == null)
                {
                    longAbImageFrameData = frameTexture;
                }
                else
                {
                    System.Buffer.BlockCopy(frameTexture, 0, longAbImageFrameData, 0, longAbImageFrameData.Length);
                }

                longAbImageMediaTexture.LoadRawTextureData(longAbImageFrameData);
                longAbImageMediaTexture.Apply();
            }
        }

        // Update point cloud
        UpdatePointCloud();
        
        
#endif
        if (tcpClient.Connected && !updatedPointCloudSent && continuousSend)
        {
            SendLongDepthSensorCombined();
            updatedPointCloudSent = true;
        }
    }

    private bool updatedPointCloudSent = true;
    private float[] pointCloud = new float[] { };
#if ENABLE_WINMD_SUPPORT
    private void UpdatePointCloud()
    {
        if (enablePointCloud)
        {
            if (researchMode.LongThrowPointCloudUpdated()){
                pointCloud = researchMode.GetLongThrowPointCloudBuffer();     
                updatedPointCloudSent = false;
            }
        }
    }
#endif



    #region Button Event Functions

    public void SendLongDepthSensorCombined()
    {
        currentDebugger.AddDebugMessage("Sending Data...");
#if WINDOWS_UWP
        long timestamp = GetCurrentTimestampUnix();
        var depthMap = researchMode.GetLongDepthMapBuffer();
        if (tcpClient != null)
        {
            tcpClient.SendLongDepthSensorCombined(depthMap, longAbImageFrameData, pointCloud, timestamp, camera.transform.eulerAngles, camera.transform.position);
        }
#endif
    }
    public void StopSensorsEvent()
    {
#if ENABLE_WINMD_SUPPORT
        researchMode.StopAllSensorDevice();
#endif
        startRealtimePreview = false;
    }
    public void startContinuousSend()
    {
        currentDebugger.AddDebugMessage("The continuous send is now " + continuousSend.ToString());
        continuousSend = !continuousSend;
    }

    #endregion
    private void OnApplicationFocus(bool focus)
    {
        if (!focus) StopSensorsEvent();
    }

#if WINDOWS_UWP
    private long GetCurrentTimestampUnix()
    {
        // Get the current time, in order to create a PerceptionTimestamp. 
        Windows.Globalization.Calendar c = new Windows.Globalization.Calendar();
        Windows.Perception.PerceptionTimestamp ts = Windows.Perception.PerceptionTimestampHelper.FromHistoricalTargetTime(c.GetDateTime());
        return ts.TargetTime.ToUnixTimeMilliseconds();
        //return ts.SystemRelativeTargetTime.Ticks;
    }
    private Windows.Perception.PerceptionTimestamp GetCurrentTimestamp()
    {
        // Get the current time, in order to create a PerceptionTimestamp. 
        Windows.Globalization.Calendar c = new Windows.Globalization.Calendar();
        return Windows.Perception.PerceptionTimestampHelper.FromHistoricalTargetTime(c.GetDateTime());
    }
#endif
}