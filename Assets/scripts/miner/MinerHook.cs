using UnityEngine;

public class MinerHook : MonoBehaviour
{
    [Header("References")]
    public Transform hookHead;
    public LineRenderer linkLine;

    [Header("Settings")]
    public float shootSpeed = 8f;
    public float retractSpeed = 6f;
    public float maxDistance = 5f;
    public float timeBetweenShots = 1f;

    private enum State { Waiting, Shooting, Retracting }
    private State currentState = State.Waiting;

    private Vector3 shootDirection;
    private float timer;
    private GrabbableObject caughtObject;

    void Start()
    {
        // Initialize the line renderer to always have exactly 2 points
        linkLine.positionCount = 2;
        timer = timeBetweenShots;
        
        // Snap hook to miner at the start
        hookHead.position = transform.position; 

        // Hook head reports what it touches back to us
        HookCatcher catcher = hookHead.GetComponent<HookCatcher>();
        if (catcher == null)
        {
            catcher = hookHead.gameObject.AddComponent<HookCatcher>();
        }
        catcher.owner = this;
    }

    void Update()
    {
        switch (currentState)
        {
            case State.Waiting:
                timer -= Time.deltaTime;
                if (timer <= 0)
                {
                    ShootRandomly();
                }
                break;

            case State.Shooting:
                // Move the hook outward
                hookHead.position += shootDirection * shootSpeed * Time.deltaTime;
                
                // Check if it reached max distance
                if (Vector3.Distance(transform.position, hookHead.position) >= maxDistance)
                {
                    currentState = State.Retracting;
                }
                break;

            case State.Retracting:
                // Move the hook back to the miner
                hookHead.position = Vector3.MoveTowards(hookHead.position, transform.position, retractSpeed * Time.deltaTime);
                
                // Check if it returned
                if (Vector3.Distance(transform.position, hookHead.position) < 0.1f)
                {
                    hookHead.position = transform.position; // Snap perfectly to center

                    if (caughtObject != null)
                    {
                        caughtObject.Collect();
                        caughtObject = null;
                    }

                    timer = timeBetweenShots;
                    currentState = State.Waiting;
                }
                break;
        }
    }

    void LateUpdate()
    {
        // Keep the visual line connected every frame
        linkLine.SetPosition(0, transform.position);
        linkLine.SetPosition(1, hookHead.position);
    }

    // Called by HookCatcher. Only catches while shooting, one object per shot.
    public void TryCatch(GrabbableObject grabbable)
    {
        if (currentState != State.Shooting || grabbable.IsCaught)
        {
            return;
        }

        grabbable.Catch(hookHead);
        caughtObject = grabbable;
        currentState = State.Retracting; // Head straight back at retractSpeed
    }

    void ShootRandomly()
    {
        // Pick a random angle between 0 and 360 degrees
        float randomAngle = Random.Range(180f, 360f);
        
        // Convert angle to a direction vector using basic trigonometry
        shootDirection = new Vector3(Mathf.Cos(randomAngle * Mathf.Deg2Rad), Mathf.Sin(randomAngle * Mathf.Deg2Rad), 0).normalized;

        currentState = State.Shooting;
    }
}