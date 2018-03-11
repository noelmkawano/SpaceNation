using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UserInterfaceRV : MonoBehaviour {

	//! Link to the GameObject holding the spaceship model (assumed parent is NBody)
	public SpaceshipRV spaceship;

    public NBody[] targets; 

	public NBody centralMass;

	//! Prefab for interects
	public GameObject interceptMarker;

	//! Prefab for match
	public GameObject rendezvousMarker;

	//! Rate of spin per frame key is held down
	public float spinRate = 1f;

	//! Thrust delta applied per-press of the thrust +/- key 
	public float thrustPerKeypress = 1f;

	// UI Panels
	public GameObject objectivePanel;
    public SelectionPanel objectiveSelectionPanel;

    public GameObject courseChangePanel;

    public GameObject manualPanel;


	public GameObject interceptPanel;
    public SelectionPanel interceptSelectionPanel;

    public GameObject maneuverPanel;

    public GameObject orbitXferPanel;
    public SelectionPanel orbitSelectionPanel;

    // text object on the panel
    public Text maneuverText;

	private const string SELECT = "Select a ship.";
	private const string SELECTED = "Ship:";
	private const string TARGET = "Select a target.";
	private const string TARGETED = "Target ";
	private const string CHOOSE = "Choose an intercept";
	private const string CONFIRM_INTERCEPT = "Add maneuver (Y/N)?";
	private const string MANEUVER = "Manuever Set. <SPACE> to run.";

	// UI State Machine:
	// 
	//    SELECT_OBJECTIVE ---------> INTERCEPT_SELECTION
	//         |                      (manual control)
	//         |
	//         +---------------> ORBIT_SELECTION
	//

	public enum State {
		SELECT_OBJECTIVE,
        COURSE_CHANGE,
		INTERCEPT_SELECTION,
		ORBIT_SELECTION,
		MANEUVER,
		RUNNING
	}

	private State state;
	private NBody target;

	private bool running = true;

	// List of intercepts used in INTERCEPT_SELECTION mode
	private List<TrajectoryData.Intercept> intercepts;

	private List<OrbitTransfer> transfers; 

	//! Optional element to mark trajectory intercepts of two spaceships
	private TrajectoryIntercepts trajIntercepts;

	private TransferCalc transferCalc;

    //! trajectory has changed, used in checking for intercepts in manual maneuver mode
    private bool lastTrajectoryUpToDate = false;

    //! OrbitPredictors in the scene that are active when the scene starts
    private List<OrbitPredictor> orbitPredictors;
    private List<OrbitRenderer> orbitRenderers;


    // Use this for initialization (must Awake, since start of GameLoop will set states)
    void Awake () {
		state = State.SELECT_OBJECTIVE;
		intercepts = null; 
	
		if (targets.Length == 0) {
			Debug.LogError("No targets configured");
		}
		// Player is spaceship 1, others are objectives
		
		// take first ship to tbe the player
		target = targets[0];


		// Need to configure objective chooser 
		SetObjectiveOptions(targets);

		SetState(state);

		// add a trajectory intercepts component (it need to handle markers so it has
		// a monobehaviour base class). 
		// The pair of spaceships to be checked will be selected dynamically
		trajIntercepts = gameObject.AddComponent<TrajectoryIntercepts>();
		trajIntercepts.interceptSymbol = interceptMarker;
		trajIntercepts.rendezvousSymbol = rendezvousMarker;

        // only record the elements that are active at the start of the scene
        orbitPredictors = new List<OrbitPredictor>();
        foreach (OrbitPredictor op in  (OrbitPredictor[])Object.FindObjectsOfType(typeof(OrbitPredictor)) ) {
            if (op.gameObject.activeInHierarchy) {
                orbitPredictors.Add(op);
            }
        }
        orbitRenderers = new List<OrbitRenderer>();
        foreach (OrbitRenderer or in (OrbitRenderer[])Object.FindObjectsOfType(typeof(OrbitRenderer)) ) {
            if (or.gameObject.activeInHierarchy) {
                orbitRenderers.Add(or);
            }
        }
    }

    private void ObjectiveSelected(int selection) {
        target = targets[selection];
        // enable maneuver selection panel
        SetState(State.COURSE_CHANGE);
    }

	private void SetObjectiveOptions(NBody[] targets) {
        int count = 0;
        objectiveSelectionPanel.Clear();
		foreach (NBody nbody in targets) 
		{
            int _count = count++;
            objectiveSelectionPanel.AddButton(nbody.gameObject.name, () => ObjectiveSelected(_count));
        }
    }

	private const float MouseSelectRadius = 20f;

	// Also allow ship selection with mouse click
	private int GetShipSelection() {
		// TODO: Extend to do touch
		if (Input.GetMouseButtonDown(0)) {
			Vector3 mousePos = Input.mousePosition;
			// see if close enough to a ship
			for( int i=0; i < targets.Length; i++) {
				Vector3 shipScreenPos = Camera.main.WorldToScreenPoint(targets[i].transform.position);
				Vector3 shipXYPos = new Vector3(shipScreenPos.x, shipScreenPos.y, 0);
				if (Vector3.Distance(mousePos, shipXYPos) < MouseSelectRadius) {
					return i;
				}
			}
		}
		return -1;
	}
    
    // Set all panels to inactive
	private void InactivatePanels() {
		objectivePanel.SetActive(false);
        courseChangePanel.SetActive(false);
        manualPanel.SetActive(false);
		interceptPanel.SetActive(false);
		orbitXferPanel.SetActive(false);
		maneuverPanel.SetActive(false);
	}

	private void SetState(State newState) {
        if (state == newState) {
            return;
        }
        // Debug.Log("new state = " + newState);
		switch(newState) {
		case State.SELECT_OBJECTIVE:
			InactivatePanels();
			objectivePanel.SetActive(true);
			break;

        case State.COURSE_CHANGE:
            InactivatePanels();
            courseChangePanel.SetActive(true);
            break;

        case State.INTERCEPT_SELECTION:
			running = false;
			GravityEngine.Instance().SetEvolve(running);
            // trajectory prediction is "heavy", especially at high time zoom. Only enable it when it is
            // needed
            SetTrajectoryPrediction(true);
			InactivatePanels();
			manualPanel.SetActive(true);
			break;

		case State.ORBIT_SELECTION:
			running = false;
			GravityEngine.Instance().SetEvolve(running);
			InactivatePanels();
			orbitXferPanel.SetActive(true);
			CalculateTransfers();
			break;

		case State.MANEUVER:
			running = true;
            SetTrajectoryPrediction(false);
            GravityEngine.Instance().SetEvolve(running);
            trajIntercepts.ClearMarkers();
            InactivatePanels();
			maneuverPanel.SetActive(true);
            spaceship.ResetThrust();
			break;

		default:
			Debug.LogError("Internal error - unknown state");
			break;
		}
        state = newState;
	}

    /// <summary>
    /// Configure trajectory prediction. Since prediction is a computational burden (especially when timeZoom is high)
    /// only use this when the ship maneuver is being changed. 
    /// 
    /// Orbit renderering and prediction is replaced with trajectories of the same color, so need to flip between these
    /// sets of objects being enabled. 
    /// 
    /// </summary>
    /// <param name="enable"></param>
    private void SetTrajectoryPrediction(bool enable) {
        GravityEngine.Instance().SetTrajectoryPrediction(enable);
        foreach( OrbitPredictor op in orbitPredictors) {
            op.gameObject.SetActive(!enable);
        }
        foreach (OrbitRenderer or in orbitRenderers) {
            or.gameObject.SetActive(!enable);
        }
    }


    // "Note also that the Input flags are not reset until "Update()", 
    // so its suggested you make all the Input Calls in the Update Loop."
    // Do key processing in Update. Means some GE calls happen off stride in
    // FixedUpdate.
    void Update() {

		if (Input.GetKeyDown(KeyCode.Space)) {
			running = !running;
			GravityEngine.Instance().SetEvolve(running);
		}

        spaceship.Run(running, GravityEngine.Instance().GetPhysicalTime());

        // RF: Have state inner class with update method to segregate?
        switch (state) 
		{
			case State.INTERCEPT_SELECTION:
                // check for rotations
                // ship change will trigger a recompute of trajectories, intercepts will only be
                // determinable once this is done
                Quaternion rotation = Quaternion.identity;
                if (Input.GetKey(KeyCode.A)) {
                    spaceship.Rotate(spinRate, Vector3.forward);
                } else if (Input.GetKey(KeyCode.D)) {
                    spaceship.Rotate(-spinRate, Vector3.forward);
                } else if (Input.GetKey(KeyCode.W)) {
                    spaceship.Rotate(spinRate, Vector3.right);
                } else if (Input.GetKey(KeyCode.S)) {
                    spaceship.Rotate(-spinRate, Vector3.right);
                } else if (Input.GetKeyDown(KeyCode.Minus)) {
                    spaceship.UpdateThrust(-thrustPerKeypress);
                } else if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus)) {
                    spaceship.UpdateThrust(thrustPerKeypress);
                }
                // Can check intercepts when trajectory is up to date
                if (!lastTrajectoryUpToDate && GravityEngine.Instance().TrajectoryUpToDate()) {
                    CheckIntercepts();
                }
                lastTrajectoryUpToDate = GravityEngine.Instance().TrajectoryUpToDate();
                break;

            case State.SELECT_OBJECTIVE:
				// check for mouse on a target object
				int selected = GetShipSelection();
				if (selected >= 0) {
					target = targets[selected];
				}
				break;

			case State.MANEUVER:
				// Show current time and pending maneuvers
				System.Text.StringBuilder sb = new System.Text.StringBuilder();
				sb.Append(string.Format("World Time: {0:000.0} [x{1}]\n", 
                            GravityEngine.Instance().GetPhysicalTime(), 
                            GravityEngine.Instance().GetTimeZoom() ));
				sb.Append(string.Format("\nManeuvers Pending:\n"));
                string[] mstrings = spaceship.ManeuverString();
                if (mstrings.Length > 0) {
                    foreach (string s in spaceship.ManeuverString()) {
                        sb.Append(string.Format("{0}\n", s));
                    }
                    maneuverText.text = sb.ToString();
                } else {
                    SetState(State.SELECT_OBJECTIVE);
                }
				break;

			default:
				break;
		}

		
	}

    //-------------------------------------------------------------------------
    // Transfer/Maneuver calculations
    //-------------------------------------------------------------------------

    private void OrbitSelected(int selection) {
        spaceship.SetTransfer(transfers[selection]);
        SetState(State.MANEUVER);
    }
    

	private void CalculateTransfers() {

		// Find xfer choices and present to user
		transferCalc = new TransferCalc(spaceship.GetNBody(), target, centralMass);
        transfers =  transferCalc.FindTransfers();
        orbitSelectionPanel.Clear();
        int count = 0; 
		foreach (OrbitTransfer t in transfers) {
            // need a new variable for each lambda
            int _count = count++;
            orbitSelectionPanel.AddButton(t.ToString(), ()=>OrbitSelected(_count) );
		}
	}

    private void InterceptSelected(int selection) {
        spaceship.SetManeuver(intercepts[selection]);
        SetState(State.MANEUVER);
    }

    private void CheckIntercepts() {
		// check for intercepts
		// List them in a dropdown and allow user to click an intercept to select it
		intercepts = MarkIntercepts(spaceship, target);
		if (intercepts.Count > 0) {
			InactivatePanels();
			interceptPanel.SetActive(true);
            // populate the intercept drop down.
            interceptSelectionPanel.Clear();
            int count = 0; 
            // With long trajectory times can get same point on multiple orbits. 
            // Just take the first two intercepts
			foreach (TrajectoryData.Intercept t in intercepts) 
			{
                if (count >= 2)
                    break;
                int _count = count++;
                string label = string.Format("@t={0:00.0}\ndV={1:00.0} dT={2:00.0}", t.tp1.t, t.dV, t.dT);
                interceptSelectionPanel.AddButton(label, () => InterceptSelected(_count));
                // t.Log();
             }
 		} else {
			InactivatePanels();
			manualPanel.SetActive(true);
		}
	}

	/// <summary>
	/// Mark intercepts with the designated symbols and return a list of intercepts found
	/// for the predicted path of ship intersecting with the path of the target.
	/// </summary>
	///
	/// <param name="ship">The ship</param>
	/// <param name="target">The target</param>
	///
	/// <returns>The intercepts.</returns>
	private List<TrajectoryData.Intercept> MarkIntercepts(SpaceshipRV ship, NBody target) {
        // delta distance is scale dependent. For now use an ugly *if*
        float deltaDistance = 1f;
        if (GravityEngine.Instance().units == GravityScaler.Units.ORBITAL) {
            deltaDistance = 20f;
        }
        const float deltaTime = 2f;
        const float rendezvousDT = 1f; 
		trajIntercepts.spaceship = ship.GetTrajectory();
        Trajectory trajectory = target.GetComponentInChildren<Trajectory>();
        if (trajectory == null) {
            Debug.LogError("Target requires a child with a trajectory component");
            return new List<TrajectoryData.Intercept>();
        }
        trajIntercepts.target = trajectory;
		trajIntercepts.ComputeAndMarkIntercepts(deltaDistance, deltaTime, rendezvousDT);
		intercepts = trajIntercepts.GetIntercepts();
		return intercepts;
	}

	//-------------------------------------------------------------------------
	// UI Callbacks
	// Buttons/Dropdowns boxes call back via these methods
	//-------------------------------------------------------------------------


	public void ManualMode() {
		SetState(State.INTERCEPT_SELECTION);
	}

    public void OrbitSelectionMode() {
        SetState(State.ORBIT_SELECTION);
    }


    public void Top() {
		SetState(State.SELECT_OBJECTIVE);
	}

}
