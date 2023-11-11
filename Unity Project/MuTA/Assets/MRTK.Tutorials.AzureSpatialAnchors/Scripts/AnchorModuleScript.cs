using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using RestSharp;

#if WINDOWS_UWP
using Windows.Storage;
#endif

public class AnchorModuleScript : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The unique identifier used to identify the shared file (containing the Azure anchor ID) on the web server.")]
    private string publicSharingPin = "1982734901747";

    [SerializeField]
    private Debugger debugger;

    [HideInInspector]
    // Anchor ID for anchor stored in Azure (provided by Azure) 
    public string currentAzureAnchorID = ""; 

    private SpatialAnchorManager cloudManager;
    private CloudSpatialAnchor currentCloudAnchor;
    private AnchorLocateCriteria anchorLocateCriteria;
    private CloudSpatialAnchorWatcher currentWatcher;
    private Pose currentAnchorTransform;

    private readonly Queue<Action> dispatchQueue = new Queue<Action>();

    #region Unity Lifecycle
    void Start()
    {
        // Get a reference to the SpatialAnchorManager component (must be on the same gameobject)
        cloudManager = GetComponent<SpatialAnchorManager>();

        // Register for Azure Spatial Anchor events
        cloudManager.AnchorLocated += CloudManager_AnchorLocated;

        anchorLocateCriteria = new AnchorLocateCriteria();

    }


    private bool isinit = false;

    void Update()
    {
        if (!isinit)
        {
            isinit = true;
            StartAzureSession();
        }
        lock (dispatchQueue)
        {
            if (dispatchQueue.Count > 0)
            {
                dispatchQueue.Dequeue()();
            }
        }
    }

    void OnDestroy()
    {
        if (cloudManager != null && cloudManager.Session != null)
        {
            cloudManager.DestroySession();
        }

        if (currentWatcher != null)
        {
            currentWatcher.Stop();
            currentWatcher = null;
        }
    }
    #endregion

    #region Public Methods
    public async void StartAzureSession()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.StartAzureSession()");

        // Notify AnchorFeedbackScript
        OnStartASASession?.Invoke();

        debugger.AddDebugMessage("Starting Azure session... please wait...");

        if (cloudManager.Session == null)
        {
            // Creates a new session if one does not exist
            await cloudManager.CreateSessionAsync();
        }

        // Starts the session if not already started
        await cloudManager.StartSessionAsync();
        OnStartASASessionFinished?.Invoke();
        debugger.AddDebugMessage("Azure session started successfully");

        
    }

    public async void StopAzureSession()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.StopAzureSession()");

        // Notify AnchorFeedbackScript
        OnEndASASession?.Invoke();

        debugger.AddDebugMessage("Stopping Azure session... please wait...");

        // Stops any existing session
        cloudManager.StopSession();

        // Resets the current session if there is one, and waits for any active queries to be stopped
        await cloudManager.ResetSessionAsync();

        debugger.AddDebugMessage("Azure session stopped successfully");
    }

    public async void CreateAzureAnchor(GameObject theObject)
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.CreateAzureAnchor()");

        // Notify AnchorFeedbackScript
        OnCreateAnchorStarted?.Invoke();

        // First we create a native XR anchor at the location of the object in question
        theObject.CreateNativeAnchor();

        // Notify AnchorFeedbackScript
        OnCreateLocalAnchor?.Invoke();

        // Then we create a new local cloud anchor
        CloudSpatialAnchor localCloudAnchor = new CloudSpatialAnchor();

        // Now we set the local cloud anchor's position to the native XR anchor's position
        localCloudAnchor.LocalAnchor = await theObject.FindNativeAnchor().GetPointer();

        // Check to see if we got the local XR anchor pointer
        if (localCloudAnchor.LocalAnchor == IntPtr.Zero)
        {
            debugger.AddDebugMessage("Didn't get the local anchor...");
            return;
        }
        else
        {
            debugger.AddDebugMessage("Local anchor created");
        }

        // In this sample app we delete the cloud anchor explicitly, but here we show how to set an anchor to expire automatically
        localCloudAnchor.Expiration = DateTimeOffset.Now.AddDays(7);

        // Save anchor to cloud
        while (!cloudManager.IsReadyForCreate)
        {
            await Task.Delay(330);
            float createProgress = cloudManager.SessionStatus.RecommendedForCreateProgress;
            QueueOnUpdate(new Action(() => debugger.AddDebugMessage($"Move your device to capture more environment data: {createProgress:0%}")));
        }

        bool success;

        try
        {
            debugger.AddDebugMessage("Creating Azure anchor... please wait...");

            // Actually save
            await cloudManager.CreateAnchorAsync(localCloudAnchor);

            // Store
            currentCloudAnchor = localCloudAnchor;
            localCloudAnchor = null;

            // Success?
            success = currentCloudAnchor != null;

            if (success)
            {
                debugger.AddDebugMessage($"Azure anchor with ID '{currentCloudAnchor.Identifier}' created successfully");

                // Notify AnchorFeedbackScript
                

                // Update the current Azure anchor ID
                debugger.AddDebugMessage($"Current Azure anchor ID updated to '{currentCloudAnchor.Identifier}'");
                currentAzureAnchorID = currentCloudAnchor.Identifier;
                OnCreateAnchorSucceeded?.Invoke();
            }
            else
            {
                debugger.AddDebugMessage($"Failed to save cloud anchor with ID '{currentAzureAnchorID}' to Azure");

                // Notify AnchorFeedbackScript
                OnCreateAnchorFailed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            debugger.AddDebugMessage(ex.ToString());
        }
    }

    public void RemoveLocalAnchor(GameObject theObject)
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.RemoveLocalAnchor()");

        // Notify AnchorFeedbackScript
        OnRemoveLocalAnchor?.Invoke();

        theObject.DeleteNativeAnchor();

        if (theObject.FindNativeAnchor() == null)
        {
            debugger.AddDebugMessage("Local anchor deleted succesfully");
        }
        else
        {
            debugger.AddDebugMessage("Attempt to delete local anchor failed");
        }
    }

    public void FindAzureAnchor(string id = "")
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.FindAzureAnchor()");

        if (id != "")
        {
            currentAzureAnchorID = id;
        }

        // Notify AnchorFeedbackScript
        OnFindASAAnchor?.Invoke();

        // Set up list of anchor IDs to locate
        List<string> anchorsToFind = new List<string>();

        if (currentAzureAnchorID != "")
        {
            anchorsToFind.Add(currentAzureAnchorID);
        }
        else
        {
            debugger.AddDebugMessage("Current Azure anchor ID is empty");
            return;
        }

        anchorLocateCriteria.Identifiers = anchorsToFind.ToArray();
        debugger.AddDebugMessage($"Anchor locate criteria configured to look for Azure anchor with ID '{currentAzureAnchorID}'");

        // Start watching for Anchors
        if ((cloudManager != null) && (cloudManager.Session != null))
        {
            currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);
            debugger.AddDebugMessage("Watcher created");
            debugger.AddDebugMessage("Looking for Azure anchor... please wait...");
        }
        else
        {
            debugger.AddDebugMessage("Attempt to create watcher failed, no session exists");
            currentWatcher = null;
        }
    }

    public async void DeleteAzureAnchor()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.DeleteAzureAnchor()");

        // Notify AnchorFeedbackScript
        OnDeleteASAAnchor?.Invoke();

        // Delete the Azure anchor with the ID specified off the server and locally
        await cloudManager.DeleteAnchorAsync(currentCloudAnchor);
        currentCloudAnchor = null;

        debugger.AddDebugMessage("Azure anchor deleted successfully");
    }

    public void SaveAzureAnchorIdToDisk()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.SaveAzureAnchorIDToDisk()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";
#endif

        string filePath = Path.Combine(path, filename);
        File.WriteAllText(filePath, currentAzureAnchorID);

        debugger.AddDebugMessage($"Current Azure anchor ID '{currentAzureAnchorID}' successfully saved to path '{filePath}'");
    }

    public void GetAzureAnchorIdFromDisk()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.LoadAzureAnchorIDFromDisk()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";
#endif

        string filePath = Path.Combine(path, filename);
        currentAzureAnchorID = File.ReadAllText(filePath);

        debugger.AddDebugMessage($"Current Azure anchor ID successfully updated with saved Azure anchor ID '{currentAzureAnchorID}' from path '{path}'");
    }

    public void ShareAzureAnchorIdToNetwork()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.ShareAzureAnchorID()");

        string filename = "SharedAzureAnchorID." + publicSharingPin;
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path + "/";           
#endif

        string filePath = Path.Combine(path, filename);
        File.WriteAllText(filePath, currentAzureAnchorID);

        debugger.AddDebugMessage($"Current Azure anchor ID '{currentAzureAnchorID}' successfully saved to path '{filePath}'");

        try
        {
            var client = new RestClient("http://167.99.111.15:8090");

            debugger.AddDebugMessage($"Connecting to network client '{client}'... please wait...");

            var request = new RestRequest("/uploadFile.php", Method.POST);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "multipart/form-data");
            request.AddFile("the_file", filePath);
            request.AddParameter("replace_file", 1);  // Only needed if you want to upload a static file

            var httpResponse = client.Execute(request);

            debugger.AddDebugMessage("Uploading file... please wait...");

            string json = httpResponse.Content.ToString();
        }
        catch (Exception ex)
        {
            debugger.AddDebugMessage(string.Format("Exception: {0}", ex.Message));
            throw;
        }

        debugger.AddDebugMessage($"Current Azure anchor ID '{currentAzureAnchorID}' shared successfully");
    }

    public void GetAzureAnchorIdFromNetwork()
    {
        debugger.AddDebugMessage("\nAnchorModuleScript.GetSharedAzureAnchorID()");

        StartCoroutine(GetSharedAzureAnchorIDCoroutine(publicSharingPin));
    }
    #endregion
    public Pose GetCurrentAnchorTransform()
    {
        return currentAnchorTransform;
    }
    
    #region Event Handlers
    private void CloudManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
    {
        QueueOnUpdate(new Action(() => debugger.AddDebugMessage($"Anchor recognized as a possible Azure anchor")));

        if (args.Status == LocateAnchorStatus.Located || args.Status == LocateAnchorStatus.AlreadyTracked)
        {
            currentCloudAnchor = args.Anchor;

            QueueOnUpdate(() =>
            {
                debugger.AddDebugMessage($"Azure anchor located successfully");

                // Notify AnchorFeedbackScript
                OnASAAnchorLocated?.Invoke();

#if WINDOWS_UWP || UNITY_WSA
                // HoloLens: The position will be set based on the unityARUserAnchor that was located.

                // Create a local anchor at the location of the object in question
                gameObject.CreateNativeAnchor();

                // Notify AnchorFeedbackScript
                OnCreateLocalAnchor?.Invoke();

                
                // On HoloLens, if we do not have a cloudAnchor already, we will have already positioned the
                // object based on the passed in worldPos/worldRot and attached a new world anchor,
                // so we are ready to commit the anchor to the cloud if requested.
                // If we do have a cloudAnchor, we will use it's pointer to setup the world anchor,
                // which will position the object automatically.
                if (currentCloudAnchor != null)
                {
                    debugger.AddDebugMessage("Local anchor position successfully set to Azure anchor position");
                    currentAnchorTransform = currentCloudAnchor.GetPose();
                    debugger.AddDebugMessage("Current Location of Anchor Is: " + currentAnchorTransform.position.ToString());
                    //gameObject.GetComponent<UnityEngine.XR.WSA.WorldAnchor>().SetNativeSpatialAnchorPtr(currentCloudAnchor.LocalAnchor);
                }

#elif UNITY_ANDROID || UNITY_IOS
                Pose anchorPose = Pose.identity;
                anchorPose = currentCloudAnchor.GetPose();

                debugger.AddDebugMessage($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
                transform.position = anchorPose.position;
                transform.rotation = anchorPose.rotation;

                // Create a native anchor at the location of the object in question
                gameObject.CreateNativeAnchor();

                // Notify AnchorFeedbackScript
                OnCreateLocalAnchor?.Invoke();

#endif
            });
        }
        else
        {
            QueueOnUpdate(new Action(() => debugger.AddDebugMessage($"Attempt to locate Anchor with ID '{args.Identifier}' failed, locate anchor status was not 'Located' but '{args.Status}'")));
        }
    }
    #endregion

    #region Internal Methods and Coroutines
    private void QueueOnUpdate(Action updateAction)
    {
        lock (dispatchQueue)
        {
            dispatchQueue.Enqueue(updateAction);
        }
    }

    IEnumerator GetSharedAzureAnchorIDCoroutine(string sharingPin)
    {
        string url = "http://167.99.111.15:8090/file-uploads/static/file." + sharingPin.ToLower();

        debugger.AddDebugMessage($"Looking for url '{url}'... please wait...");

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            if (www.isNetworkError || www.isHttpError)
            {
                debugger.AddDebugMessage(www.error);
            }
            else
            {
                debugger.AddDebugMessage("Downloading... please wait...");

                string filename = "SharedAzureAnchorID." + publicSharingPin;
                string path = Application.persistentDataPath;

#if WINDOWS_UWP
                StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
                path = storageFolder.Path;
#endif
                currentAzureAnchorID = www.downloadHandler.text;

                debugger.AddDebugMessage($"Current Azure anchor ID successfully updated with shared Azure anchor ID '{currentAzureAnchorID}' url");

                string filePath = Path.Combine(path, filename);
                File.WriteAllText(filePath, currentAzureAnchorID);
            }
        }
    }
    #endregion

    #region Public Events
    public delegate void StartASASessionDelegate();
    public event StartASASessionDelegate OnStartASASession;
    public event StartASASessionDelegate OnStartASASessionFinished;

    public delegate void EndASASessionDelegate();
    public event EndASASessionDelegate OnEndASASession;

    public delegate void CreateAnchorDelegate();
    public event CreateAnchorDelegate OnCreateAnchorStarted;
    public event CreateAnchorDelegate OnCreateAnchorSucceeded;
    public event CreateAnchorDelegate OnCreateAnchorFailed;

    public delegate void CreateLocalAnchorDelegate();
    public event CreateLocalAnchorDelegate OnCreateLocalAnchor;

    public delegate void RemoveLocalAnchorDelegate();
    public event RemoveLocalAnchorDelegate OnRemoveLocalAnchor;

    public delegate void FindAnchorDelegate();
    public event FindAnchorDelegate OnFindASAAnchor;

    public delegate void AnchorLocatedDelegate();
    public event AnchorLocatedDelegate OnASAAnchorLocated;

    public delegate void DeleteASAAnchorDelegate();
    public event DeleteASAAnchorDelegate OnDeleteASAAnchor;
    #endregion
}
