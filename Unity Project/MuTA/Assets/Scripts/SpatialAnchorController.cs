using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpatialAnchorController : MonoBehaviour
{

    [SerializeField]
    private GameObject anchorObject;

    private AnchorModuleScript anchorModule;
    private NetworkUtils networkUtils;

    private Pose anchorTransform;

    public event Notify onAnchorLocationFound;
    public event Notify onAnchorLocationNotFound;

    [SerializeField]
    private Debugger debugger;

    // Start is called before the first frame update
    void Start()
    {
        networkUtils = GetComponent<NetworkUtils>();
        networkUtils.onAnchorNotFound += onAnchorIDNotFound;
        networkUtils.onAnchorUpdate += onAnchorIDFound;
        anchorModule = anchorObject.GetComponent<AnchorModuleScript>();
        anchorModule.OnStartASASessionFinished += startAzureAfterInit;
    }
    
    // Update is called once per frame
    void Update()
    {
    
    }

    #region Public Functions

    public Vector3 getAnchorRotation()
    {
        return anchorTransform.rotation.eulerAngles;
    }

    public Vector3 getAnchorPosition()
    {
        return anchorTransform.position;
    }
    #endregion

    #region Event Handler

    private void startAzureAfterInit()
    {
        debugger.AddDebugMessage("Init Finished");
        networkUtils.getAnchorID();
    }

    private void onAnchorIDNotFound()
    {
        debugger.AddDebugMessage("Anchor ID not found, proceeed creating anchor...");
        anchorModule.OnCreateAnchorSucceeded += onCreateAnchorSucceeded;
        anchorModule.CreateAzureAnchor(anchorObject);
    }

    private void onAnchorIDFound()
    {
        string anchorID = networkUtils.getAnchorData().id;
        debugger.AddDebugMessage("Anchor ID Found to be: " + anchorID);
        anchorModule.OnFindASAAnchor += onAnchorFound;
        anchorModule.FindAzureAnchor(anchorID);
    }

    private void onAnchorFound()
    {
        debugger.AddDebugMessage(anchorModule.currentAzureAnchorID + " Found");
        anchorTransform = anchorModule.GetCurrentAnchorTransform();
        debugger.AddDebugMessage("Current Position is: " + anchorTransform.position);
        onAnchorLocationFound?.Invoke();
    }

    private void onCreateAnchorSucceeded()
    {
        debugger.AddDebugMessage("Successfully created anchor with ID: "+ anchorModule.currentAzureAnchorID);
        networkUtils.setAnchorID(anchorModule.currentAzureAnchorID);
    }
    #endregion

}
