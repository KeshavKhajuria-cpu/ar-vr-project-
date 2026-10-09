using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.EventSystems;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using InputTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

[RequireComponent(typeof(ARRaycastManager))]
public class ARTapToPlaceObject : MonoBehaviour
{
    [Header("Placement Settings")]
    [Tooltip("Prefab to instantiate on the detected AR plane.")]
    [SerializeField] private GameObject objectToPlace;

    [Tooltip("If true, spawn a new instance on each tap. If false, move/reposition the existing instance.")]
    [SerializeField] private bool allowMultiple = false;

    [Tooltip("Automatically place the object on the first detected surface when found.")]
    [SerializeField] private bool autoPlaceOnFirstSurface = true;

    [Tooltip("Optional placement indicator / reticle GameObject.")]
    [SerializeField] private GameObject placementIndicator;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARSession arSession;
    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private GameObject spawnedObject;
    private bool cameraPermissionGranted = false;
    private float statusTimer = 0f;

    public GameObject ObjectToPlace
    {
        get => objectToPlace;
        set => objectToPlace = value;
    }

    public GameObject SpawnedObject => spawnedObject;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        if (raycastManager == null)
            raycastManager = FindAnyObjectByType<ARRaycastManager>();

        planeManager = GetComponent<ARPlaneManager>();
        if (planeManager == null)
            planeManager = FindAnyObjectByType<ARPlaneManager>();

        arSession = FindAnyObjectByType<ARSession>();

        CheckCameraPermission();
    }

    private void Start()
    {
        RequestCameraPermission();
    }

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Enable();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Disable();
#endif
    }

    public void RequestCameraPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnPermissionGranted;
            callbacks.PermissionDenied += (perm) => { cameraPermissionGranted = false; };
            Permission.RequestUserPermission(Permission.Camera, callbacks);
        }
        else
        {
            cameraPermissionGranted = true;
        }
#else
        cameraPermissionGranted = true;
#endif
    }

    private void OnPermissionGranted(string permissionName)
    {
        cameraPermissionGranted = true;
        if (arSession != null)
        {
            arSession.Reset();
        }
    }

    private void CheckCameraPermission()
    {
#if UNITY_ANDROID
        cameraPermissionGranted = Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
        cameraPermissionGranted = true;
#endif
    }

    private void Update()
    {
        statusTimer += Time.deltaTime;
        if (statusTimer > 1f)
        {
            statusTimer = 0f;
            CheckCameraPermission();
        }

        UpdatePlacementIndicator();

        // Auto-place on first detected plane if not yet spawned
        if (autoPlaceOnFirstSurface && spawnedObject == null)
        {
            TryAutoPlace();
        }

        // Tap to place or reposition
        if (TryGetInputPosition(out Vector2 touchPosition))
        {
            if (IsPointerOverUI(touchPosition))
                return;

            if (raycastManager != null && raycastManager.Raycast(touchPosition, hits, TrackableType.PlaneWithinPolygon))
            {
                if (hits.Count > 0 && objectToPlace != null)
                {
                    Pose hitPose = hits[0].pose;
                    PlaceObject(hitPose);
                }
            }
        }
    }

    private void TryAutoPlace()
    {
        if (objectToPlace == null) return;

        // First try raycasting from screen center to see if camera is aiming at a surface
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager != null && raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            if (hits.Count > 0)
            {
                PlaceObject(hits[0].pose);
                return;
            }
        }

        // Otherwise, place on first tracked plane with valid area
        if (planeManager != null && planeManager.trackables.count > 0)
        {
            foreach (var plane in planeManager.trackables)
            {
                if (plane != null && plane.trackingState == TrackingState.Tracking)
                {
                    Vector3 worldCenter = plane.transform.TransformPoint(plane.center);
                    Pose planePose = new Pose(worldCenter, plane.transform.rotation);
                    PlaceObject(planePose);
                    break;
                }
            }
        }
    }

    private void UpdatePlacementIndicator()
    {
        if (placementIndicator == null || raycastManager == null) return;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            if (hits.Count > 0)
            {
                Pose hitPose = hits[0].pose;
                placementIndicator.transform.SetPositionAndRotation(hitPose.position, hitPose.rotation);
                if (!placementIndicator.activeSelf)
                    placementIndicator.SetActive(true);
            }
        }
        else
        {
            if (placementIndicator.activeSelf)
                placementIndicator.SetActive(false);
        }
    }

    public void PlaceObject(Pose hitPose)
    {
        if (objectToPlace == null) return;

        // Orient object to face towards the main camera on spawn
        Quaternion spawnRotation = hitPose.rotation;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 lookDirection = cam.transform.position - hitPose.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                spawnRotation = Quaternion.LookRotation(lookDirection);
            }
        }

        if (allowMultiple || spawnedObject == null)
        {
            spawnedObject = Instantiate(objectToPlace, hitPose.position, spawnRotation);
        }
        else
        {
            spawnedObject.transform.SetPositionAndRotation(hitPose.position, spawnRotation);
        }
    }

    public void ResetPlacement()
    {
        if (spawnedObject != null)
        {
            Destroy(spawnedObject);
            spawnedObject = null;
        }
    }

    private bool TryGetInputPosition(out Vector2 touchPosition)
    {
        touchPosition = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        // 1. Enhanced Touch (Mobile device touchscreen)
        if (InputTouch.activeTouches.Count > 0)
        {
            var touch = InputTouch.activeTouches[0];
            if (touch.phase == InputTouchPhase.Began)
            {
                touchPosition = touch.screenPosition;
                return true;
            }
        }

        // 2. Touchscreen device fallback
        if (Touchscreen.current != null)
        {
            var primaryTouch = Touchscreen.current.primaryTouch;
            if (primaryTouch.press.wasPressedThisFrame)
            {
                touchPosition = primaryTouch.position.ReadValue();
                return true;
            }
        }

        // 3. Mouse / Editor fallback
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            touchPosition = Mouse.current.position.ReadValue();
            return true;
        }

        // 4. Generic Pointer fallback
        if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
        {
            touchPosition = Pointer.current.position.ReadValue();
            return true;
        }
#endif

        // 5. Legacy Input fallback
        if (Input.touchCount > 0)
        {
            UnityEngine.Touch legacyTouch = Input.GetTouch(0);
            if (legacyTouch.phase == UnityEngine.TouchPhase.Began)
            {
                touchPosition = legacyTouch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            touchPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private bool IsPointerOverUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = screenPos
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    private void OnGUI()
    {
        int screenW = Screen.width;
        int screenH = Screen.height;
        float dpiScale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
        int fontSize = Mathf.Max(14, Mathf.RoundToInt(16 * dpiScale));

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        labelStyle.normal.textColor = Color.white;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter
        };

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold
        };
        buttonStyle.normal.textColor = Color.white;

#if UNITY_ANDROID
        if (!cameraPermissionGranted)
        {
            float modalW = Mathf.Min(screenW * 0.85f, 400 * dpiScale);
            float modalH = 180 * dpiScale;
            float modalX = (screenW - modalW) * 0.5f;
            float modalY = (screenH - modalH) * 0.5f;

            GUI.Box(new Rect(modalX, modalY, modalW, modalH), "");
            GUI.Label(new Rect(modalX + 10, modalY + 15, modalW - 20, 70 * dpiScale), 
                "Camera Permission Required\nAR Beagle needs camera access to show augmented reality in your room.", labelStyle);

            if (GUI.Button(new Rect(modalX + 20, modalY + 100 * dpiScale, modalW - 40, 50 * dpiScale), "Grant Camera Access", buttonStyle))
            {
                RequestCameraPermission();
            }
            return;
        }
#endif

        // Top status HUD
        float bannerW = Mathf.Min(screenW * 0.92f, 500 * dpiScale);
        float bannerH = 50 * dpiScale;
        float bannerX = (screenW - bannerW) * 0.5f;
        float bannerY = 30 * dpiScale;

        string statusText;
        int planeCount = planeManager != null ? planeManager.trackables.count : 0;

        if (spawnedObject != null)
        {
            statusText = "Beagle Dog Active! Tap floor to reposition.";
        }
        else if (planeCount > 0)
        {
            statusText = "Floor detected! Tap on surface to place Beagle.";
        }
        else
        {
            statusText = "Scanning floor... Move phone slowly around room.";
        }

        GUI.Box(new Rect(bannerX, bannerY, bannerW, bannerH), "");
        GUI.Label(new Rect(bannerX + 10, bannerY, bannerW - 20, bannerH), statusText, labelStyle);

        // Reset button when dog is placed
        if (spawnedObject != null)
        {
            float btnW = 120 * dpiScale;
            float btnH = 40 * dpiScale;
            float btnX = (screenW - btnW) * 0.5f;
            float btnY = bannerY + bannerH + 10 * dpiScale;

            if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "Reset Dog", buttonStyle))
            {
                ResetPlacement();
            }
        }
    }
}