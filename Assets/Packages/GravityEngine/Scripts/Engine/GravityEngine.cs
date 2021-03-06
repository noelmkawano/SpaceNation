﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/*! \mainpage Gravity Engine Unity Asset
 *
 *  To use the gravity engine in a Unity scene there must be an object with a GravityEngine. GravityEngine will
 *  compute and move objects that have NBody components or particle systems that have GravityParticles components.
 *
 *  GravityEngine is commonly used in a mode that auto-detects all bodies in a scene. This default mode can be
 *  turned off and objects can be added as part of an explicit list or via API calls to AddBody(). 
 *
 * Full documentation is provided in the PDF file provided with the asset. 
 *
 * Tutorial and demo videos can be found on You Tube on the <a href="https://www.youtube.com/channel/UCxH9ldb8ULCO_B7_hZwIPvw">NBodyPhysics channel</a>
 * 
 * On-line documentation: <a href="http://nbodyphysics.com/blog/gravity-engine-doc">Gravity Engine Documentation</a>
 *
 * Support: nbodyphysics@gmail.com
 */


/// <summary>
/// GravityEngine (NBE)
/// Primary controller of Nbody physics evolution with selectable integration algorithms. Singleton. 
/// 
/// Bodies
/// The positions and masses of the N bodes are initialized here and passed by reference to the integrator. 
/// This allows high precision evolution of the bodies and a simpler integration scheme for particle systems
/// moving in the fields of the N bodies. 
///
/// Particles
/// NBE creates a ParticleEvolver and evolves particle systems once per fixed update based on new positions
/// of the N bodies. Particles are massless and so do not interact with each other (too computationally expensive).
///
/// Body initialization:
/// TODO
/// </summary>
public class GravityEngine : MonoBehaviour {

	public static string TAG = "GravityEngine";
	//! Singleton instance handle. Initialized during Awake().
	public static GravityEngine instance; 

	//! global flag for debug logging
	public const bool DEBUG = false; 
	                                       
	/// <summary>
	/// Integrator choices:
	/// LEAPFROG - a fixed timestep, with good energy conservation 
	/// HERMITE - an adaptive timestep algorithm with excellent energy conservation
	/// AZTRIPLE - For 3 bodies ONLY. Works in regularized co-ordinates that allow close encounters.
	/// ANY_FORCE_FL - Leapfrog with a force delegate
	/// <summary>
	public enum Algorithm { LEAPFROG, HERMITE8, AZTRIPLE};
	private static string[] algorithmName = new string[]{"Leapfrog", "Hermite (adaptive)", "TRIPLE (Regularized Burlisch-Stoer)"};

	/// <summary>
	/// The force used when one of the ANY_FORCE integrators is selected. 
	/// Any force use requires that the scale be set to DIMENSIONLESS. 
	/// </summary>
	public ForceChooser.Forces force; 

	public const int NDIM = 3; // Here to "de-magic" numbers. Some integrators have 3 baked in. Do not change.

	//! Algorithm for numerical integration of massive bodies
	public Algorithm algorithm; 
	//! Use masslessEngine to reduce computations when many massless bodies.
	public bool optimizeMassless = true; 
	//! Automatically detect all objects with an NBody component and add to the engine.
	public bool detectNbodies = true; 

	//! Enable trajectory prediction - TrajectoryTrails attached to NBody objects will be updated
	public bool trajectoryPrediction = false; 

	//! time to evolve forward for trajectory prediction
	public float trajectoryTime = 15f; 
	//! Used by Trajectory when text labels for time are enabled
	public GameObject trajectoryCanvas; 

	//! Optional parent object to assign trajectory markers to (so they do not clutter up the root object space)
	public GameObject markerParent;

    //! Multiplier for trajectory recompute simulations per frame. Low number spread update over more frames and
    //! have less impact on run-time performance and the cost of longer times to see new trajectory
    public float trajectoryComputeFactor = 4f;

	/// <summary>
	/// physToWorldFactor: factor to allow distance measurements in NBE to be on a different scale than in the Unity world view. 
	/// This is useful when taking initial conditions from literature (e.g. the three body solutions) in which 
	/// the data provided are normalized to [-1..1]. Having all world objects in [-1..1] becomes awkward. Setting
	/// this scale allows the Unity positions to be expanded to a more convenient range.
	///
	/// If this is used for objects that are created in Unity world space it will change the distance scale used
	/// by the physics engine and consequently the time evolution will also change. Moving objects closer (physToWorldFactor > 1)
	/// will result in stronger gravity and faster interactions. 
	/// </summary>
	public float physToWorldFactor = 1.0f;

	public GravityScaler.Units units;

	/// <summary>
	/// The length scale.
	/// Scale from NBody initial pos to Unity position
	/// Expressed in Unity units per scale unit (pos = scale_pos * lengthScale).
	/// Changing this value will result in changes in the positions of all NBody objects in 
	/// the scene. It is intended for use by the Editor scripts during script setup and not
	/// for run-time changes.
	/// </summary>
	[SerializeField]
	private float _lengthScale = 1f; 
	//! Orbital scale in Unity unity per AU
	public float lengthScale {
		get { return _lengthScale; }
		set { UpdateLengthScale(value);}
	}

	//! Mass scale applied to all NBody objects handled by the Gravity Engine. Increasing mass scale makes everything move faster for the same CPU cost.
	public float massScale = 1.0f;

	/// <summary>
	/// The time scale.
	/// Time scale is used for overall scale at setup and is used via dimension analysis to set an overall
	/// mass scale to produce the required evolution. 
	///
	/// To change evolution speed at run time use SetTimeZoom(). This affectes the amount of physics calculations
	/// performed per frame. 
	///
	/// </summary>
	[SerializeField]
	private float _timeScale = 1.0f; 
	//! Orbital scale in Unity unity per AU
	public float timeScale {
		get { return _timeScale; }
		set { UpdateTimeScale(value);}
	}

	private float timeZoom = 1f; 
	private bool timeZoomChangePending; 
	private float newTimeZoom; 

	//! Array of game objects to be controlled by the engine at start (used when detectNbodies=false). During evolution use AddBody().
	public GameObject[] bodies; 

	//! Begin gravitational evolution when the scene starts.
	public bool evolveAtStart = true;  
	private bool evolve;

	//! State of inspector Advanced foldout
	public bool editorShowAdvanced; 
	//! State of inspector Scale foldout
	public bool editorShowScale; 
	//! State of inspector Center of Mass foldout
	public bool editorCMfoldout; 
	//! Track state of foldout in editor
	public bool editorShowTrajectory; 

	//--Integrator stuff--	
	private INBodyIntegrator integrator;

	//! Number of physics steps per frame for massive body evolution. For LEAPFROG will directly map to CPU
	//! use and accuracy. In the case of HERMITE, number of iterations per frame will vary depending on
	//! speeds of bodies. 
	public int stepsPerFrame = 8;
	//! Number of steps per frame for particle evolution. All particle evolution is via LEAPFROG, independent of the
	//! choice of algorithm for massive body evolution.
	public int particleStepsPerFrame = 2;

	// Sub-divide frame time into 8 steps for Leapfrog integration
	private double engineDt = 1.0/(60.0 * 8);

	// run particles at a larger timestep to save CPU
	private double particle_dt;  
		
	// mass/position information for massive bodies
	// For performance reasons (http://jacksondunstan.com/articles/3058) need to manage arrays and
	// grow them dynamically when required. Adding to the fun, the integrator delegates have the same
	// issue and must stay aligned with the arrays here.
	//
	// Typically over-allocate the initial body count
	private int arraySize; 
	private const int GROW_SIZE = 10;

	//! current state of massive bodies
	private GravityState worldState; 

	//! future state of massive bodies (if trajectory prediction is enabled)
	private GravityState trajectoryState; 

	// need to keep size^2 for simple collision detection between particles and 
	// massive bodies. Collisions between massive bodies are left to usual Unity
	// collider intrastructure
	private double[] size2; // size^2 used for detecting particle incursions
	
	//! Bit flag used in integrator code
	public const byte INACTIVE 		= 1;		// mass should be skipped in integration
	//! Bit flag used in integrator code
	public const byte FIXED_MOTION  = 1 << 1;	// integrator should not update position/velocity but
	                                            // will use mass to affect other object
	public const byte TRAJ_DATA = 1 << 2; 		// Track trajectory data for the object                                        


	private byte[] info; // per GameObject flags for the integrator

	//! Objects to show future trajectories (if trajectory prediction is enabled)
	private Trajectory[] trajectories; 

	private List<GameObject> addedByScript; 

	// Gravitational Body tracking
	private GameObject[] gameObjects; 
	private int numBodies; 

		
	private List<GravityParticles> nbodyParticles; 
	
	// an optional masslessBodyEngine may be created if required
	// If present, any massless bodies will be updated on each cycle
	private MasslessBodyEngine masslessEngine; 

	// less than this consider a body massless
	private const double MASSLESS_LIMIT = 1E-6; 

	private bool isSetup = false; // flag to trigger setup on first evolution

	// Force delegate
	private IForceDelegate forceDelegate;

	// Delegate for handling Maneuvers. Access through GE wrapper methods, but
	// separate implementation in a delegate.
	private ManeuverMgr maneuverMgr;

	// ---------inner classes-----------
	public class FixedBody {
		public int index; 
		public IFixedOrbit fixedOrbit; 
		
		public FixedBody(int index, IFixedOrbit fixedOrbit) {
			this.index = index; 
			this.fixedOrbit = fixedOrbit;
		}
	}

	//! NBody type - used in integrator code. 
	public enum BodyType { MASSIVE, MASSLESS, FIXED };

	// Held by a NBody object. Hold reference to internal reference details. 
	public class EngineRef {
		public BodyType bodyType;
        public FixedBody fixedBody;
		public int index; 

        public EngineRef() {

        }

        public EngineRef(BodyType bodyType, int index) {
            this.bodyType = bodyType;
            this.index = index;
        }
	}
	
	// ---------main class-------------
	private List<FixedBody> fixedBodies;

	/// <summary>
	/// Static accessor that finds Instance. Useful for Editor scripts.
	/// </summary>
	public static GravityEngine Instance()
 	{
     	if (instance == null)
         	instance = (GravityEngine)FindObjectOfType(typeof(GravityEngine));
     	return instance;
    }
		
	void Awake () {
		if (instance == null) {
			instance = this; 
		} else {
			Debug.LogError("More than one GravityEngine in Scene");
			return;
		}

		ConfigureDT();
		SetAlgorithm(algorithm);
		nbodyParticles = new List<GravityParticles>();

		fixedBodies = new List<FixedBody>();

		if (massScale == 0) {
			Debug.LogError("Cannot evolve with massScale = 0"); 
			return;
		}
		if (physToWorldFactor == 0) {
			Debug.LogError("Cannot evolve with physToWorldFactor = 0"); 
			return;
		}
		if (timeScale == 0) {
			Debug.LogError("Cannot evolve with timeScale = 0"); 
			return;
		}
		// force computation of massScale
		UpdateTimeScale(_timeScale);

		addedByScript = new List<GameObject>();

        // defensive init for early access to physical time at start (will get replaced)
        worldState = new GravityState(arraySize);

    }

    void Start() {
		// setup if evolve is set
		if (evolveAtStart) {
			// evolve on start requires something to evolve!
			if (bodies.Length == 0 && !detectNbodies) {
				Debug.LogError("No bodies attached to Engine. Do not start until bodies present");
				evolve = false;
				return;
			}
			SetEvolve(true);
		} 
	}

	/// <summary>
	/// Control evolution of masses and particles in the gravity engine. 
	/// </summary>
	/// <param name="evolve">If set to <c>true</c> evolve.</param>
	public void SetEvolve(bool evolve) {

		this.evolve = evolve;
	}

	public bool GetEvolve() {
		return evolve;
	}
	
	/// <summary>
	/// Sets the integration algorithm used for massive bodies. 
	///
	/// The integration algorithm cannot be changed while the engine is running. 
	/// </summary>
	/// <param name="algorithm">Algorithm.</param>
	public void SetAlgorithm(Algorithm algorithm) {
		if (evolve) {
			Debug.LogError("Cannot change algorithm while evolving");
			return;
		}
		forceDelegate = ForceChooser.InstantiateForce(force, this.gameObject);
		switch(algorithm) {
			case Algorithm.LEAPFROG:
				integrator = new LeapFrogIntegrator(forceDelegate);
				break;
			case Algorithm.HERMITE8:
				integrator = new HermiteIntegrator(forceDelegate);
				break;
			case Algorithm.AZTRIPLE:
				integrator = new AZTripleIntegrator();
				break;
			default:
				Debug.LogError("Unknown algortithm");
				break;
		}

	}

	/// <summary>
	/// Gets the name of the algorithm as a string.
	/// </summary>
	/// <returns>The algorithm name.</returns>
	/// <param name="algorithm">Algorithm.</param>
	public static string GetAlgorithmName(Algorithm algorithm) {
		return algorithmName[(int) algorithm];
	}

	/// <summary>
	/// Gets the particle time step size.
	/// </summary>
	/// <returns>The particle dt.</returns>
	public double GetParticleDt() {
		return particle_dt;
	}

	/// <summary>
	/// Reset the bodies/particle systems known to the Gravity Engine.
	/// </summary>
	public void Clear() {

		#pragma warning disable 162		// disable unreachable code warning
		if (DEBUG) {
			Debug.Log("Clearing " + numBodies + " bodies");
		}
		#pragma warning restore 162
		for (int i=0; i < numBodies; i++) {
			RemoveBody(gameObjects[i]);
			gameObjects[i] = null;
		}
		numBodies = 0;
		fixedBodies.Clear();
		nbodyParticles.Clear();
		masslessEngine = null;
        worldState.masslessEngine = null;
		isSetup = false;
		#pragma warning disable 162		// disable unreachable code warning
		if (DEBUG) {
			Debug.Log("All bodies cleared.");
		}
		#pragma warning restore 162
		maneuverMgr = new ManeuverMgr();
	}

	public void Setup() {
		numBodies = 0;
		GravityScaler.UpdateTimeScale(units, _timeScale, _lengthScale);
		GravityScaler.ScaleScene(units,  _lengthScale);
		maneuverMgr = new ManeuverMgr();

		if (detectNbodies) {
			SetupAutoDetect();
		} else {
			SetupExplicit();
		}
		PreEvolve();
		worldState.ResetPhysicalTime();
		#pragma warning disable 162		// disable unreachable code warning
		if (DEBUG) {
			int numMassless = 0; 
			if (masslessEngine != null)
				numMassless = masslessEngine.NumBodies();
			Debug.Log(string.Format("GravityEngine started with {0} massive, {1} massless, {2} particle systems. {3} fixed",
							numBodies, numMassless, nbodyParticles.Count, fixedBodies.Count));
			DumpAll(worldState);
		}
		#pragma warning restore 162
	}


	/// <summary>
	/// Setup only the bodies (and their children) that have been explicitly added to the NBody engine
	/// via the bodies list. There can be bodies already added programatically via AddBody() as well, these
	/// are on the addedByScript list.
	/// </summary>
	private void SetupExplicit() {
		int maxBodies = 0; 
		// two passes - first get a count
		if (bodies != null) {
			foreach (GameObject body in bodies) {
				maxBodies += body.GetComponentsInChildren<NBody>().Length;
			}
		}
		if (addedByScript.Count > 0) {
			foreach (GameObject body in addedByScript) {
				maxBodies += body.GetComponentsInChildren<NBody>().Length;
			}
		}
		InitArrays(maxBodies+GROW_SIZE);
		// Now do setup on each body
		if (bodies != null) {
			foreach (GameObject body in bodies) {
				SetupGameObjectAndChildren(body);
			}
		}
		if (addedByScript.Count > 0) {
			foreach (GameObject body in addedByScript) {
				SetupGameObjectAndChildren(body);
			}
			addedByScript.Clear();
		}
	}

	/// <summary>
	/// Find all active NBody objects in the scene and add them to the engine. 
	/// </summary>
	private void SetupAutoDetect() {
        // Need to deterimine maxBodies from body lists
        // GetComponentsInChildren also returns components in parent object!
        int maxBodies = 0; 
        // objects added in the inspector
        if (bodies != null) {
               foreach (GameObject body in bodies) {
                       maxBodies += body.GetComponentsInChildren<NBody>().Length;
               }
        }
                       
		NBody[] nbodies = (NBody[]) Object.FindObjectsOfType(typeof(NBody));
		// allocate physics arrays (will over-allocate by number of massless bodies if optimizing massless)
		// add some buffer to allow for dynamic additions
		maxBodies += nbodies.Length;
        InitArrays(maxBodies+GROW_SIZE);

		foreach (NBody nbody in nbodies) {
			SetupOneGameObject(nbody.gameObject, nbody);
		}
	}

	private void InitArrays(int size) {
		// Typically over-allocate to allow for dynamic additions EXCEPT for the AZT integrator which can only
		// handle three bodies
		arraySize = size;
		if (algorithm == Algorithm.AZTRIPLE) {
			arraySize = 3;
		}
		worldState = new GravityState(arraySize);
        worldState.integrator = integrator;
        worldState.masslessEngine = masslessEngine;
		size2 = new double[arraySize];
		info = new byte[arraySize];
		trajectories = new Trajectory[arraySize];
		gameObjects = new GameObject[arraySize];
		// integrator will allocate internal data and set dt
		integrator.Setup(arraySize, engineDt);
	}

	// Grow arrays to hold new massive bodies and trigger the same operation in the 
	// integrator to maintain array alignment. 
	//
	// Not ideal - but scientific computing is array based and direct arrays have the best performance. 
	//
	private bool GrowArrays(int growBy) {
		if (algorithm == Algorithm.AZTRIPLE) {
			Debug.LogWarning("Cannot grow AZT beyond size 3");
			return false;
		}

		worldState.GrowArrays(growBy);

		double[] size2_copy = new double[arraySize];
		byte[] info_copy = new byte[arraySize];
		Trajectory[] traj_copy = new Trajectory[arraySize];
		GameObject[] gameObjects_copy = new GameObject[arraySize];

		for (int i=0; i < arraySize; i++) {
			size2_copy[i] = size2[i];
			info_copy[i] = info[i];
			traj_copy[i] = trajectories[i];
			gameObjects_copy[i] = gameObjects[i];
		}

		size2 = new double[arraySize+growBy];
		info = new byte[arraySize+growBy];
		trajectories = new Trajectory[arraySize+growBy];
		gameObjects = new GameObject[arraySize+growBy];

		for (int i=0; i < arraySize; i++) {
			size2[i] = size2_copy[i];
			info[i] = info_copy[i];
			trajectories[i] = traj_copy[i];
			gameObjects[i] = gameObjects_copy[i];
		}
		integrator.GrowArrays(growBy);
		arraySize += growBy;
		#pragma warning disable 162		// disable unreachable code warning
		if (DEBUG)
			Debug.Log("GrowArrays by " + growBy);
		#pragma warning restore 162		
		return true;
	}

	// Integrators need to use known positions to pre-determine accel. etc. to 
	// have valid starting values for evolution
	private void PreEvolve() {
		// particles will pre-evolve when loading complete
		if (masslessEngine != null) {
			masslessEngine.PreEvolve(numBodies, worldState); 
		}
		foreach (FixedBody fixedBody in fixedBodies) {
            if (fixedBody.fixedOrbit != null) {
                fixedBody.fixedOrbit.PreEvolve(physToWorldFactor, massScale);
            }
		}
		integrator.PreEvolve(worldState, ref info);
	}

	
	private int logCounter; 

	public bool IsSetup() {
		return isSetup;
	}

	/*******************************************
	* Trajectory Prediction
	* TP prediction is based on maintaining a parallel integrator and 
	* masslessEngine and running them ahead in time. 
	* Trajectory objects attached are given the updated position information 
	* so the future path can be displayed. 
	*
	* If the inputs to the system change (velocity change, body added) then the 
	* system needs to be reset and run forward from the current state again. 
	/*******************************************/

	private bool trajectoryRestart; 

	public void TrajectoryRestart() {
        if (trajectoryPrediction) {
            trajectoryRestart = true;
        }
	}

	/// <summary>
	/// Sets the trajectory prediction state (enable/disable). 
	/// On enable, will activate the Trajectory elements and re-run the trajectory prediction code.
	/// On disable will de-activate all trajectory elements. 
	/// </summary>
	/// <param name="newState">If set to <c>true</c> new state.</param>
	public void SetTrajectoryPrediction(bool newState) {
		if (newState != trajectoryPrediction) {
			if (newState) {
				// do a restart sync-ed with FixedUpdate
				trajectoryRestart = true;
				// set all trajectories active
				for (int i=0; i < numBodies; i++) {
					if ((trajectories[i] != null) && (info[i] & INACTIVE) == 0) {
						trajectories[i].gameObject.SetActive(true);
					}
				}
				if (masslessEngine != null) {
					masslessEngine.TrajectoryEnable(true);
				}
                trajectoryPrediction = true;
            } else {
				// hide all trajectories and remove all time/text markers
				for (int i=0; i < numBodies; i++) {
					if ((trajectories[i] != null) && (info[i] & INACTIVE) == 0) {
						trajectories[i].Cleanup();
						trajectories[i].gameObject.SetActive(false);
					}
				}
				if (masslessEngine != null) {
					masslessEngine.TrajectoryEnable(false);
				}
                trajectoryPrediction = false;
            }
        }
	}

	private void ResetTrajectoryPrediction() {

		// trajectory state starts as a clone of current world state
		trajectoryState = new GravityState(worldState);
        // Clone the integrator
        trajectoryState.integrator = integrator.DeepClone();
        for (int i = 0; i < numBodies; i++) {
            if ((trajectories[i] != null) && (info[i] & INACTIVE) == 0) {
                trajectories[i].Init((float)worldState.time);
            }
        }
        // clone the massless engine
        if (masslessEngine != null) {
            trajectoryState.masslessEngine = masslessEngine.DeepClone();
            trajectoryState.masslessEngine.ResetTrajectories((float)worldState.time);
        }
		trajectoryRestart = false;
        // enable only after all trajectories re-enabled and inited
        trajectoryPrediction = true;

    }

    /// <summary>
    /// If any NBodies have Trajectory components then update them with new projected position/times
    /// </summary>
    private void UpdateTrajectories() {
		for (int i=0; i < numBodies; i++) {
			if ((trajectories[i] != null) && (info[i] & INACTIVE) == 0) {
				Vector3 position = new Vector3((float)trajectoryState.r[i,0], (float)trajectoryState.r[i,1], (float)trajectoryState.r[i,2]); 
				position = physToWorldFactor * position;
				trajectories[i].AddPoint(position, (float)trajectoryState.time, (float) worldState.time);
				// update trajectory data if enabled (used for intercept detection)
				if (trajectories[i].recordData) {
                    // Want scaled velocity
                    Vector3 velocity = Vector3.zero;
                    if ((info[i] & FIXED_MOTION) != 0) {
                        NBody nbody = gameObjects[i].GetComponent<NBody>();
                        velocity = nbody.engineRef.fixedBody.fixedOrbit.GetVelocity();
                    } else {
                        velocity = integrator.GetVelocityForIndex(i);
                    }
                    trajectories[i].AddData(position, velocity, (float)trajectoryState.time);
				}
			}
		}
		// massless bodies update their own trajectories (a bit klunky)
		if (trajectoryState.masslessEngine != null) {
            trajectoryState.masslessEngine.UpdateTrajectories(physToWorldFactor, (float)trajectoryState.time, (float) worldState.time );
		}

	}

    /// <summary>
    /// Evolves the trajectory to the specified time or advances the trajectory by the fraction constrained
    /// by the trajectoryComputeFactor. This limits the number of trajectory integrations in a given fixed update
    /// to reduce the frame rate impact of frequent trajectory updates. 
    /// </summary>

    private bool trajectoryUpToDate = false; 

    private void EvolveTrajectory(double gameDt) {

        // determine delta time to evolve and invoke common Evolve routine
        double timeInterval = (worldState.time + trajectoryTime) - trajectoryState.time;
        // if we're catching up, then reduce amount of work we do on this step
        trajectoryUpToDate = true;
        if (timeInterval > gameDt) {
            timeInterval = trajectoryComputeFactor * gameDt;
            trajectoryUpToDate = false;
        } 
        Evolve(timeInterval, trajectoryState);
        trajectoryState.time = trajectoryState.physicalTime[(int)GravityState.Evolvers.MASSIVE];
    }

    public bool TrajectoryUpToDate() {
        return trajectoryUpToDate;
    }

	/*******************************************
	* Main Physics Loop
	********************************************/

	/// <summary>
	/// Evolve the objects subject to gravity. 
    /// 
    /// Normal evolution is done by passing in worldState with a time interval corresponding to the 
    /// frame advance time multipleied by the time zoom. 
    /// 
    /// For trajectory updates in the case where the trajectory is up to date, this will be for the same interval
    /// but starting at a futute time. 
    /// 
    /// In order for trajectory computation to "catch up", there are times when the interval may be longer (but limited
    /// by the re-compute factor to avoid a huge recomputation on a single frame). 
	/// </summary>
	///
	/// <param name="time">The time</param>
	private void Evolve(double gameDt, GravityState systemState) {
        // Objective is to keep physical time proportional to game time 
        // Each integrator will run for at least as long as it is told but may overshoot
        // so correct time on next iteration. 
        // 
        // Keep the current physical time each integrator has reached in physicalTime[integrator_type]
        //

        if (gameDt < engineDt)
            return;

        double timeEvolved = 0;

        // Need to move the integrators forward concurrently in steps matching the engineDt
        // - Hermite may be using a different timestep than this
        // - particles likely use a much longer timestep

        while (timeEvolved < gameDt) {
            //==============================
            // Massive bodies
            //==============================
            // evolve all the massive game objects 
            double massiveDt = 0.0;
            massiveDt = systemState.integrator.Evolve(engineDt, systemState, ref info);
            systemState.physicalTime[(int)GravityState.Evolvers.MASSIVE] += massiveDt;
            timeEvolved += massiveDt;
            // Debug.Log(string.Format("gameDt={0} integated={1} ptime={2} wtime={3}", gameDt, dt, physicalTime, worldTime));
            // LF is built in to particles and massless routines. They have their own DT built in
            // these run on a fixed timestep (if it is varied energy conservation is wrecked)
            // Track their evolution vs all clock time seperately

            //==============================
            // Fixed Bodies
            //==============================
            // Update fixed update objects (if any)
            // Evolution is to a specific time - so use massive object physical time
            float[] r_new = new float[NDIM];
            foreach (FixedBody fixedBody in fixedBodies) {
                fixedBody.fixedOrbit.Evolve((float)systemState.physicalTime[(int)GravityState.Evolvers.MASSIVE], 
                                            physToWorldFactor, 
                                            ref r_new);
                systemState.r[fixedBody.index, 0] = r_new[0];
                systemState.r[fixedBody.index, 1] = r_new[1];
                systemState.r[fixedBody.index, 2] = r_new[2];
            }

            //==============================
            // Particles (world state only)
            //==============================
            if ((nbodyParticles.Count > 0) && (systemState == worldState)) {
                if (systemState.physicalTime[(int)GravityState.Evolvers.PARTICLES] < 
                        systemState.physicalTime[(int)GravityState.Evolvers.MASSIVE]) {
                    double evolvedFor = 0.0;
                    if (forceDelegate != null) {
                        foreach (GravityParticles nbp in nbodyParticles) {
                            evolvedFor = nbp.EvolveWithForce(particle_dt, numBodies, worldState,
                                                        ref size2, ref info, forceDelegate);
                        }
                    } else {
                        foreach (GravityParticles nbp in nbodyParticles) {
                            evolvedFor = nbp.Evolve(particle_dt, numBodies, worldState, ref size2, ref info);
                        }
                    }
                    systemState.physicalTime[(int)GravityState.Evolvers.PARTICLES] += evolvedFor;
                }
            }

            //==============================
            // Massless
            //==============================
            if (systemState.masslessEngine != null) {
                // rockets need the time
                double time = systemState.time;
                if (systemState.physicalTime[(int)GravityState.Evolvers.MASSLESS] < 
                        systemState.physicalTime[(int)GravityState.Evolvers.MASSIVE]) {
                    if (forceDelegate != null) {
                        systemState.physicalTime[(int)GravityState.Evolvers.MASSLESS] +=
                                systemState.masslessEngine.EvolveWithForce(engineDt, time, numBodies, systemState, ref info, forceDelegate);
                    } else {
                        systemState.physicalTime[(int)GravityState.Evolvers.MASSLESS] +=
                                systemState.masslessEngine.Evolve(engineDt, time, numBodies, systemState, ref info);
                    }
                }
            }
            if (systemState == trajectoryState) {
                systemState.time += massiveDt;
                UpdateTrajectories();
            }

        }
    }


	void FixedUpdate () {

		if (evolve) {
			if (!isSetup) {
				Setup();
				if (trajectoryPrediction) {
					ResetTrajectoryPrediction();
				}
				isSetup = true; 
			}
            // Note: In fixed update this is the physics deltaTime (not frame rate)
            // "When called from inside MonoBehaviour's FixedUpdate, returns the fixed framerate delta time."

            if (trajectoryRestart) {
                ResetTrajectoryPrediction();
            }

            worldState.time += Time.fixedDeltaTime * timeZoom;
            double gameDt = System.Math.Max(worldState.time - (worldState.physicalTime[(int)GravityState.Evolvers.MASSIVE]), 0.0);

            if (trajectoryPrediction) {
                EvolveTrajectory(gameDt);
            }

            // Maneuver processing - if there is a manuever in the next interval, try to evolve as close to it as possible
            // then finish the time step
            if (maneuverMgr.HaveManeuvers()) {
                List<Maneuver> maneuversInDt = maneuverMgr.ManeuversUntil((float)worldState.time);
                if (maneuversInDt.Count > 0) {
                    foreach (Maneuver m in maneuversInDt) {
                        // evolve up to the time of the earliest maneuver
                        gameDt = System.Math.Max(m.worldTime - 
                            (worldState.physicalTime[(int)GravityState.Evolvers.MASSIVE]), 0.0);
                        Evolve(gameDt, worldState);
                        m.Execute(this);
                        maneuverMgr.Executed(m);
                    }
                    // recompute remaining time to evolve
                    gameDt = System.Math.Max(worldState.time - 
                            (worldState.physicalTime[(int)GravityState.Evolvers.MASSIVE]), 0.0);
                    if (trajectoryPrediction) {
                        trajectoryRestart = true;
                    }
                }
            }
			Evolve(gameDt, worldState);

			// update positions on screen
			UpdateGameObjects();

			// particles
			foreach (GravityParticles nbp in nbodyParticles) {
				nbp.UpdateParticles(physToWorldFactor);
			}
			// massless bodies
			if (masslessEngine != null) {
				masslessEngine.UpdateBodies(physToWorldFactor);
			}


            // if there is a timescale change pending, apply it
            if (timeZoomChangePending) {
				timeZoom = newTimeZoom; 
				timeZoomChangePending = false;
			}

		} else if (isSetup && trajectoryPrediction) {
            if (trajectoryRestart) {
                ResetTrajectoryPrediction();
            }
            // trajectory evolution when paused (if setup)
            EvolveTrajectory(Time.fixedDeltaTime * timeZoom);
        }
		
	}
	/// <summary>
	/// Gets the physical scale.
	/// </summary>
	/// <returns>The physical scale.</returns>
	public float GetPhysicalScale() {
		return physToWorldFactor;
	}

	/// <summary>
	/// Return the length scale that maps lengths in the specified unit system to 
	/// Unity game length units (e.g. Unity units per meter, Unity units per AU)
	/// </summary>
	/// <returns>The length scale.</returns>
	public float GetLengthScale() {
		return lengthScale;
	}

	/// <summary>
	/// Changes the timescale during runtime
	/// </summary>
	/// <param name="value">Value.</param>
	public void SetTimeZoom(float value) {
		newTimeZoom = value;
		timeZoomChangePending = true;
	}

    /// <summary>
    /// Get the current time zoom factor (run-time scaling of physics time execution). 
    /// 
    /// Note that the baseline timescale is set by timeScale (based on units selected) when 
    /// the Gravity Engine initializes. 
    /// 
    /// </summary>
    /// <returns></returns>
    public float GetTimeZoom() {
        if (timeZoomChangePending)
            return newTimeZoom;
        return timeZoom;
    }

	/// <summary>
	/// Gets the physical time. Physical time may differ from game time by the timescale factor.
	/// </summary>
	/// <returns>The physical time.</returns>
	public float GetPhysicalTime() {
		return (float) worldState.physicalTime[(int)GravityState.Evolvers.MASSIVE];
	}

	private void SetupGameObjectAndChildren(GameObject gameObject) {

		NBody[] nbodies = gameObject.GetComponentsInChildren<NBody>();
		foreach (NBody nbody in nbodies) {
			SetupOneGameObject(nbody.transform.gameObject, nbody);
		}
	}

	// Adds one game object - checking if it is fixed or should be evolved in the 
	// standard or massless engine. 
	private void SetupOneGameObject(GameObject gameObject, NBody nbody) {

		bool fixedObject = false; 
		IFixedOrbit fixedOrbit = gameObject.GetComponent<IFixedOrbit>();

		// Backwards compatibility. Pre 1.3 position was in transform. Now in initialPos (in specified units)
		if (units == GravityScaler.Units.DIMENSIONLESS && _lengthScale == 1f ) {
			nbody.initialPos = nbody.transform.position;
		}

		// check for initial condition setup (e.g. initial conditions for elliptical orbit)
		INbodyInit initNbody = gameObject.GetComponent<INbodyInit>();
		if ( initNbody != null) {
			initNbody.InitNBody(physToWorldFactor, massScale);
		}

        EngineRef engineRef = new EngineRef();

        if (fixedOrbit != null && fixedOrbit.IsFixed()) {
			fixedObject = true;
			// Fixed objects are ALSO added to the list of massive objects below so that their gravity can affect others
			info[numBodies] |= FIXED_MOTION;
            FixedBody fixedBody = new FixedBody(numBodies, fixedOrbit);

            fixedBodies.Add(fixedBody);
			if (evolve) {
				fixedOrbit.PreEvolve(physToWorldFactor, massScale);
			}
            engineRef.fixedBody = fixedBody;
		}


		if (nbody.mass == 0 && optimizeMassless && !fixedObject) {
            // use the massless engine and its built-in Leapfrog integrator
            #pragma warning disable 162       // disable unreachable code warning
            if (masslessEngine == null) {
				masslessEngine = new MasslessBodyEngine(engineDt);
                // worldState points to this also (simplifies Evolve code)
                worldState.masslessEngine = masslessEngine;
                if (DEBUG) {
                    Debug.Log("Created masslessEngine");
                }
			}
            // MBE will create an EngineRef
			masslessEngine.AddBody(gameObject, physToWorldFactor);
			if (DEBUG) {
				Vector3 pos = nbody.transform.position;
				Debug.Log(string.Format("GE add massless: {0} world r=[{1} {2} {3}] v=[{4} {5} {6}]",
					gameObject.name, pos.x, pos.y, pos.z, 
					nbody.vel_phys.x, nbody.vel_phys.y, nbody.vel_phys.z));
			}
            #pragma warning restore 162        // enable unreachable code warning

        } else {
            // traj will be attached to a child of NBody object - record them all
            // Note: if dynamically enable/disable trajectory prediction with scripting all will be affected. 
            // Could choose to bias this to initially active ones in this loop if that was preferred.
			for (int childNum=0; childNum < nbody.transform.childCount; childNum++) {
				Trajectory trajectory = nbody.transform.GetChild(childNum).GetComponent<Trajectory>(); 
				if (trajectory != null) {
					trajectories[numBodies] = trajectory;
					break;
				}
			}

			// Use the standard (massive) engine and the configured integrator
			// Make sure there's room
			if (numBodies+1 > arraySize) {
				if (!GrowArrays(GROW_SIZE)) {
					// Grow arrays has logged the error
					return; 
				}
			}
			// mass scale is applied to internal record BUT leave nbody mass as is
			worldState.m[numBodies] = nbody.mass * massScale;
			// divide by the physics to world scale factor - required for threebody solutions
			Vector3 physicsPosition = gameObject.transform.position/physToWorldFactor;
			worldState.r[numBodies,0] = physicsPosition.x;
			worldState.r[numBodies,1] = physicsPosition.y;
			worldState.r[numBodies,2] = physicsPosition.z; 
			size2[numBodies] = nbody.size * nbody.size;
			gameObjects[numBodies] = gameObject; 
			integrator.AddNBody(numBodies, nbody, physicsPosition, nbody.vel_phys);
            engineRef.bodyType = BodyType.MASSIVE;
            engineRef.index = numBodies;
            nbody.engineRef = engineRef;
            numBodies++;
			#pragma warning disable 162		// disable unreachable code warning
			if (DEBUG) {
				int i = numBodies-1;
				Debug.Log(string.Format("GE add massive: {0} as {1} r=[{2} {3} {4}] v=[{5} {6} {7}] index={8} info={9}",
					gameObject.name, i, worldState.r[i,0], worldState.r[i,1], worldState.r[i,2], 
					nbody.vel_phys.x, nbody.vel_phys.y, nbody.vel_phys.z, nbody.engineRef.index,
					info[i]));
			}
			#pragma warning restore 162		// enable unreachable code warning
		}
	}
		
	/// <summary>
	/// Adds the game object and it's children to GravityEngine. The engine will then handle position updates for the 
	/// body based on the gravitational force of all other bodies controlled by the engine. 
	///
	/// If the GravityEngine is set to auto-detect bodies, all game objects present in the scene with a NBody
	/// component will be added once the GravityEngine is set to evolve. If auto-detect is not enabled bodies are
	/// added by calling this method. 
	///
	/// A gameObject added to the engine must have an NBody script attached. The NBody script specifies the
	/// mass and initial velocity of the object. 
	///
	/// The add method will traverse the children of the added gameObject and add any that have NBody components.  
	///
	/// Optionally, a body may also have a fixed motion script (e.g. FixedEllipticalOrbit) or a script that
	/// set the initial position and velocity based on orbit parameters (e.g. EllipticalStart)
	/// </summary>
	/// <param name="gameObject">Game object.</param>
	public void AddBody(GameObject gameObject) {

		if (evolve) {
			NBody nbody = gameObject.GetComponent<NBody>(); 
			if (nbody == null) {
				Debug.LogError("No NBody found on " + gameObject.name);
				return;
			}
			GravityScaler.ScaleNBody(nbody, units, lengthScale);
			SetupGameObjectAndChildren(gameObject);
		} else {
			addedByScript.Add(gameObject);
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}

	}


	/// <summary>
	/// Remove game object from the list of objects. 
	/// Note: In a large-N simulation the shuffle down may cause a real-time hit. In those cases, 
	/// marking the body inactive with InactivateGameObjectwill exclude it from physics caluclations
	/// without the shuffle overhead
	/// </summary>
	/// <param name="toRemove">Game object to remove (must have a NBody component)</param>
	public void RemoveBody(GameObject toRemove) {

		NBody nbody = toRemove.GetComponent<NBody>();
		if (nbody == null) {
			Debug.LogError("object to remove has no NBody: " + toRemove.name);
			return;
		}
		if ((info[nbody.engineRef.index] & FIXED_MOTION) != 0) {
			// find object in FixedBOdies list and remove
			FixedBody fbRemove = null;
			foreach(FixedBody fb in fixedBodies) {
				if (fb.index == nbody.engineRef.index) {
					fbRemove = fb;
					break;
				}
			}
			if (fbRemove != null) {
				fixedBodies.Remove(fbRemove);
			}
		}
		if (nbody.engineRef.bodyType == BodyType.MASSIVE) {
			integrator.RemoveBodyAtIndex(nbody.engineRef.index);
			// shuffle the rest down, update indices
			for( int j=nbody.engineRef.index; j < (numBodies-1); j++) {
				gameObjects[j] = gameObjects[j+1];
				NBody nextNBody = gameObjects[j].GetComponent<NBody>();
				nextNBody.engineRef.index = j;
				worldState.m[j] = worldState.m[j+1];
				for (int k=0; k < NDIM; k++) {
					worldState.r[j,k] = worldState.r[j+1, k]; 
				}
			}
			numBodies--; 
		} else {
			masslessEngine.RemoveBody(toRemove);
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
	}

	/// <summary>
	/// Inactivates the body in the GravityEngine. 
	///
	/// Mark the object as inactive. It will not affect other bodies/particles in the simulation. 
	///
	/// This can be a better choice than removing since a removal may impact real-time performance. 
	///
	/// This does not affect the activity state of the GameObject, only it's involvement in the GravityEngine
	/// 
	/// </summary>
	/// <returns>The game object.</returns>
	/// <param name="toInactivate">Game object to inactivate.</param>
	public void InactivateBody(GameObject toInactivate) {
		NBody nbody = toInactivate.GetComponent<NBody>(); 
		if (nbody == null) {
			Debug.LogWarning("Not an NBody - cannot remove"); 
			return;
		}
		if (nbody.engineRef.bodyType == BodyType.MASSLESS ) {
			masslessEngine.InactivateBody(toInactivate);
		} else {
			int i = nbody.engineRef.index;
			info[i] |= INACTIVE;
			#pragma warning disable 162		// disable unreachable code warning
			if (DEBUG)
				Debug.Log("Inactivate body " + toInactivate.name);
			#pragma warning restore 162		// enable unreachable code warning
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
	}

	/// <summary>
	/// Re-activates an inactive body.
	/// </summary>
	/// <param name="toInactivate">To inactivate.</param>
	/// Code from John Burns
	public void ActivateBody(GameObject activate) {
		NBody nbody = activate.GetComponent<NBody>(); 
		if (nbody == null) {
			Debug.LogWarning("Not an NBody - cannot remove"); 
			return;
		}
		if (nbody.engineRef.bodyType == BodyType.MASSLESS ) {
			masslessEngine.ActivateBody(activate);
		} else {
			int i = nbody.engineRef.index;
			info[i] &= unchecked((byte)~INACTIVE);
			#pragma warning disable 162		// disable unreachable code warning
			if (DEBUG)
				Debug.Log("Activate body " + activate.name);
			#pragma warning restore 162		// enable unreachable code warning
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
	}

	/// <summary>
	/// Updates the position and velocity of an existing body in the engine to new 
	/// values (e.g. teleport of the object)
	/// </summary>
	/// <param name="nbody">Nbody.</param>
	/// <param name="pos">Position.</param>
	/// <param name="vel">Vel.</param>
	public void UpdatePositionAndVelocity(NBody nbody, Vector3 pos, Vector3 vel) {

		if (nbody == null) {
			Debug.LogError("object to update has no NBody: ");
			return;
		}
		int i = nbody.engineRef.index;
		if (nbody.engineRef.bodyType == BodyType.MASSIVE) {
			// GE holds pos/vel in array
			worldState.r[i,0] = pos.x;
			worldState.r[i,1] = pos.y;
			worldState.r[i,2] = pos.z;
			integrator.SetVelocityForIndex(i, vel);
		} else {
			masslessEngine.SetPositionAtIndex(i, pos, physToWorldFactor);
			masslessEngine.SetVelocityAtIndex(i, vel);
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
	}

	/// <summary>
	/// Changes the length scale of all NBody objects in the scene due to a change in the inspector.
	/// Find all NBody containing objects.
	/// - independent objects are rescaled
	/// - orbit based objects have their primary dimension adjusted (e.g. for ellipse, a)
	///   (these objects are scalable and are asked to rescale themselves)
	///
	/// Length scale is Nbody units/Unity Length e.g. km/Unity Length
	/// Not intended for run-time use.
	/// </summary>
	private void UpdateLengthScale(float newScale) {
		_lengthScale = newScale;
		GravityScaler.UpdateTimeScale(units, _timeScale, _lengthScale);
		GravityScaler.ScaleScene(units, _lengthScale);
	}

	/// <summary>
	/// Updates the time scale.
	/// Prior to scene starting GE adjusts the time scale by setting DT for the numerical integrators.
	///
	/// During evolution DT cannot be changed on the fly for the Leapfrog integrators without violating
	/// energy conservation - so changes are made in the number of integration performed. This imposes a
	/// practical limit on how much "speed up" can occur - since too much time evolution will lower the
	/// frame rate.
	/// </summary>
	/// <param name="value">Value.</param>

	private void UpdateTimeScale(float value) {

		if (!evolve) {
			_timeScale = value;
			GravityScaler.UpdateTimeScale(units, _timeScale, _lengthScale);
			// need to do something with timeZoom here...
		} 
	}

	// TODO: Need to get conversion from phys to world
	public float GetVelocityScale() {
		return GravityScaler.GetVelocityScale();
	}


	// Intially thought in terms of changing DT based on units - but decided it's better to rescale
	// distances and masses to adapt to a universal timescale. 
	private void ConfigureDT() {
		
		double time_g1 = 1f;
		double stepsPerSec = 60.0 * (double) stepsPerFrame;
		double particleStepsPerSec = 60.0 * (double) particleStepsPerFrame;
		engineDt = time_g1/stepsPerSec; 
		particle_dt = time_g1/particleStepsPerSec; 

	}

	/// <summary>
	/// Update physics based on collisionType between body1 and body2. 
	///
	/// In all cases except bounce, the handling is a "hit and stick" and body2 is assumed to be
	/// removed. It's momtm is not updated. body1 velocity is adjusted based on conservation of momtm.
	///
	/// </summary>
	/// <param name="body1">Body1.</param>
	/// <param name="body2">Body2.</param>
	/// <param name="collisionType">Collision type.</param>
	public void Collision(GameObject body1, GameObject body2, NBodyCollision.CollisionType collisionType, float bounce) {
		NBody nbody1 = body1.GetComponent<NBody>();
		NBody nbody2 = body2.GetComponent<NBody>();
		int index1 = nbody1.engineRef.index;
		int index2 = nbody2.engineRef.index;
		if (index1 < 0 || index2 < 0)
			return; 

		// if either is massless, no momtm to exchange
		if (nbody1.mass == 0 || nbody2.mass == 0) {
			if (collisionType == NBodyCollision.CollisionType.BOUNCE) {
				// reverse the velocities
				if (nbody1.mass == 0) {
					if (nbody1.engineRef.bodyType == BodyType.MASSLESS) {
						Vector3 vel1_ml = masslessEngine.GetVelocity(body1);
						masslessEngine.SetVelocity(body1, -1f*vel1_ml);
					} else {
						Vector3 vel1_m = integrator.GetVelocityForIndex(index1);
						integrator.SetVelocityForIndex(index1, -1f*vel1_m);
					}
				}
				if (nbody2.mass == 0) {
					if (nbody2.engineRef.bodyType == BodyType.MASSLESS) {
						Vector3 vel2_ml = masslessEngine.GetVelocity(body2);
						masslessEngine.SetVelocity(body1, vel2_ml);
					} else {
						Vector3 vel2_m = integrator.GetVelocityForIndex(index2);
						integrator.SetVelocityForIndex(index1, -1f*vel2_m);
					}
				}
			}
			return;
		}
		// velocity information is in the integrators. 
		// 
		Vector3 vel1 = integrator.GetVelocityForIndex(index1);
		Vector3 vel2 = integrator.GetVelocityForIndex(index2);
		// work in CM frame of B1 and B2
		float m_total = (float)(nbody1.mass + nbody2.mass);
		Vector3 cm_vel = (((float) nbody1.mass)*vel1 + ((float)nbody2.mass)*vel2)/m_total;
		// Determine new velocities in CM frame
		Vector3 vel1_cm = vel1 - cm_vel;
		Vector3 vel2_cm = vel2 - cm_vel;
		if (collisionType == NBodyCollision.CollisionType.ABSORB_IMMEDIATE ||
			collisionType == NBodyCollision.CollisionType.EXPLODE) {
			// hit and stick 
			Vector3 v_final_cm = (((float) nbody1.mass)*vel1_cm + ((float)nbody2.mass)*vel2_cm)/m_total;
			// Translate back to world frame
			vel1 = v_final_cm + cm_vel;
			// update mass of body1 to include body2
			nbody1.mass = m_total;
			// Update velocities in integrator
			integrator.SetVelocityForIndex(index1, vel1);
		} else if (collisionType == NBodyCollision.CollisionType.BOUNCE) {
			// reverse CM velocities and flip back to world velocities
			integrator.SetVelocityForIndex(index1, cm_vel - bounce * vel1_cm);
			integrator.SetVelocityForIndex(index2, cm_vel - bounce * vel2_cm);
		}
	}
	
	// Update the game objects positions from the values held by the GravityEngine based on physics evolution
	// These positions are globally scaled by physicalScale to allow the physics to act on a
	// suitable scale where required. 	
	//
	private void UpdateGameObjects() {
		for (int i=0; i < numBodies; i++) {
            // fixed objects update their own transforms as they evolve
            NBody nbody = gameObjects[i].GetComponent<NBody>();
            if ((info[i] & INACTIVE) == 0) {
                if ((info[i] & FIXED_MOTION) == 0) {
                    Vector3 position = new Vector3((float)worldState.r[i, 0], (float)worldState.r[i, 1], (float)worldState.r[i, 2]);
                    position = physToWorldFactor * position;
                    nbody.GEUpdate(position, GetVelocity(nbody));
                } else {
                    // Fixed 
                    if (nbody.engineRef.fixedBody.fixedOrbit != null) {
                        nbody.engineRef.fixedBody.fixedOrbit.GEUpdate();
                    }
                }
            }
		}
	}

	/// <summary>
	/// Gets the velocity of the body in "Physics Space" using a GameObject. 
	/// May be different from Unity co-ordinates if physToWorldFactor is not 1. 
	/// This is the velocity in Unity units. For dimensionful velocity use @GetScaledVelocity
	/// </summary>
	/// <returns>The velocity.</returns>
	/// <param name="body">Body</param>
	public Vector3 GetVelocity(GameObject body) {
		NBody nbody = body.GetComponent<NBody>();
		if (nbody == null) {
			Debug.LogError("No NBody found on " + body.name + " cannot get velocity"); 
			return Vector3.zero;
		}
		return GetVelocity(nbody);
	}

	/// <summary>
	/// Gets the velocity of the body in "Physics Space" using an NBody reference. 
	/// May be different from Unity co-ordinates if physToWorldFactor is not 1. 
	/// This is the velocity in Unity units. For dimensionful velocity use @GetScaledVelocity
	/// </summary>
	/// <returns>The velocity.</returns>
	/// <param name="body">Body</param>
	public Vector3 GetVelocity(NBody nbody) {
        if ((nbody.engineRef.fixedBody != null) && (nbody.engineRef.fixedBody.fixedOrbit != null)) {
            // If Kepler evolution 
            return nbody.engineRef.fixedBody.fixedOrbit.GetVelocity();

        } else if (nbody.engineRef.bodyType == BodyType.MASSLESS) {
			return masslessEngine.GetVelocity(nbody);
		} 
		return integrator.GetVelocityForIndex(nbody.engineRef.index);
	}

	public void SetVelocity(NBody nbody, Vector3 velocity) {
		if (nbody.engineRef.bodyType == BodyType.MASSLESS) {
			masslessEngine.SetVelocityAtIndex(nbody.engineRef.index, velocity);
		} else {
			integrator.SetVelocityForIndex(nbody.engineRef.index, velocity);
		}
	}

	/// <summary>
	/// Gets the position and velocity in double precision in scaled units.
	/// (Note that the scale factors used to create these are floats - 
	/// </summary>
	/// <param name="nbody">Nbody.</param>
	/// <param name="p">P.</param>
	/// <param name="v">V.</param>
	public void GetPositionVelocityScaled(NBody nbody, ref double[] p, ref double[] v ) {
		if (nbody.engineRef.bodyType == BodyType.MASSLESS && masslessEngine != null) {
			masslessEngine.GetPositionVelocityScaled(nbody.engineRef.index, ref p, ref v);
		} else {
			p[0] = worldState.r[nbody.engineRef.index,0];
			p[1] = worldState.r[nbody.engineRef.index,1];
			p[2] = worldState.r[nbody.engineRef.index,2];
			integrator.GetVelocityDoubleForIndex(nbody.engineRef.index, ref v);
		}
		p[0] = p[0]*lengthScale;
		p[1] = p[1]*lengthScale;
		p[2] = p[2]*lengthScale;
		double vscale = (double) GravityScaler.GetVelocityScale();
		v[0] = v[0]/vscale;
		v[1] = v[1]/vscale;
		v[2] = v[2]/vscale;

	}


	/// <summary>
	/// Gets the velocity of the body in selected unit system.
	/// e.g. for SOLAR get value in km/sec.
    /// 
    /// NBody objects have their position and velocity updated each frame and getting the info from them 
    /// is the normal approach. This routine is used internally to get an updated value during the Evolve()
    /// process (e.g. when a Kepler object wants a update of it's center body mid-integration)
	/// </summary>
	/// <returns>The velocity.</returns>
	/// <param name="body">Body</param>
	public Vector3 GetScaledVelocity(GameObject body) {
		Vector3 velocity = Vector3.zero; 
		NBody nbody = body.GetComponent<NBody>();
		if (nbody == null) {
			Debug.LogError("No NBody found on " + body.name + " cannot get velocity"); 
			return Vector3.zero;
		}
		if (nbody.engineRef.bodyType == BodyType.MASSLESS) {
			velocity = masslessEngine.GetVelocity(body);
        }
        if ((nbody.engineRef.fixedBody != null) && (nbody.engineRef.fixedBody.fixedOrbit != null)) {
            // If Kepler evolution 
            return nbody.engineRef.fixedBody.fixedOrbit.GetVelocity();
        } else {
            velocity = integrator.GetVelocityForIndex(nbody.engineRef.index);
        }
		velocity = velocity / GravityScaler.GetVelocityScale();
		return velocity;
	}

    /// <summary>
    /// Gets the position of the body in selected unit system.
    /// e.g. for SOLAR get value in km/sec.
    /// 
    /// NBody objects have their position and velocity updated each frame and getting the info from them 
    /// is the normal approach. This routine is used internally to get an updated value during the Evolve()
    /// process (e.g. when a Kepler object wants a update of it's center body mid-integration)
    /// </summary>
    /// <returns>The position in world space.</returns>
    /// <param name="body">Body</param>
    public Vector3 GetScaledPosition(GameObject body) {
        Vector3 position = Vector3.zero;
        NBody nbody = body.GetComponent<NBody>();
        if (nbody == null) {
            Debug.LogError("No NBody found on " + body.name + " cannot get velocity");
            return Vector3.zero;
        }
        if (nbody.engineRef.bodyType == BodyType.MASSLESS) {
            position = masslessEngine.GetPosition(nbody);
        }
        if ((nbody.engineRef.fixedBody != null) && (nbody.engineRef.fixedBody.fixedOrbit != null)) {
            // If Kepler evolution 
            return nbody.engineRef.fixedBody.fixedOrbit.GetPosition();
        } else {
            position = integrator.GetVelocityForIndex(nbody.engineRef.index);
        }
        position = lengthScale * new Vector3((float)worldState.r[nbody.engineRef.index, 0],
                                             (float) worldState.r[nbody.engineRef.index, 1],
                                             (float) worldState.r[nbody.engineRef.index, 2]);
        return position;
    }

    /// <summary>
    /// Gets the acceleration of the body in "Physics Space". 
    /// May be different from world co-ordinates if physToWorldFactor is not 1. 
    /// </summary>
    /// <returns>The acceleration.</returns>
    /// <param name="body">Body.</param>
    public Vector3 GetAcceleration(GameObject body) {
		NBody nbody = body.GetComponent<NBody>();
		if (nbody == null) {
			Debug.LogError("No NBody found on " + body.name + " cannot get velocity"); 
			return Vector3.zero;
		}
		if (optimizeMassless && nbody.mass < MASSLESS_LIMIT) {
			return masslessEngine.GetAcceleration(body);
		}
        // Fixed body will return zero - fix
		return integrator.GetAccelerationForIndex(nbody.engineRef.index);
	}

    public Vector3 GetAccelerationScaled(GameObject body)
    {
        Vector3 a = GetAcceleration(body);
        return GravityScaler.ScaleAcceleration( a, lengthScale, timeScale);
    }

    /// <summary>
    /// Applies an impulse to an evolving body. The impulse is a change in momentum. The resulting
    /// velocity change will be impulse/mass. In the case of a massless body the velocity will be
    /// changed by the impulse value directly. 
    /// </summary>
    /// <param name="nbody">Nbody.</param>
    /// <param name="impulse">Impulse.</param>
    public void ApplyImpulse(NBody nbody, Vector3 impulse) {
		ApplyImpulseInternal(nbody, impulse, true);
	}

	/// <summary>
	/// Determine the velocity that will result if the impulse is applied BUT
	/// do not apply the impulse
	/// </summary>
	/// <returns>The for impulse.</returns>
	/// <param name="nbody">Nbody.</param>
	/// <param name="impulse">Impulse.</param>
	public Vector3 VelocityForImpulse(NBody nbody, Vector3 impulse) {
		return ApplyImpulseInternal(nbody, impulse, false);
	}

	private Vector3 ApplyImpulseInternal(NBody nbody, Vector3 impulse, bool apply) {
		// apply an impulse to the indicated NBody
		// impulse = step change in the momentum (p) of a body
		// delta v = delta p/m
		// If the spaceship is massless, then treat impulse as a change in velocity
		Vector3 velocity = Vector3.zero;
		if (nbody.engineRef.bodyType == BodyType.MASSIVE) {
			velocity = integrator.GetVelocityForIndex(nbody.engineRef.index);
			velocity += impulse/(nbody.mass * massScale);
			if (apply) {
				integrator.SetVelocityForIndex(nbody.engineRef.index, velocity);
			}
		} else {
			velocity = masslessEngine.GetVelocity(nbody.transform.gameObject);
			velocity += impulse;
			if (apply) {
				masslessEngine.SetVelocity(nbody.transform.gameObject, velocity);
			}
		}
		// will need to re-calc trajectories
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
		return velocity;
	}

	/// <summary>
	/// Update the mass of a NBody in the integration while evolving.
	/// </summary>
	/// <param name="nbody">Nbody.</param>
	public void UpdateMass(NBody nbody) {
		// Can only do the update if the body had a mass (otherwise would need to shuffle from 
		// massless engine to mass-based engine - not currently supported)
		if (nbody.engineRef.bodyType == BodyType.MASSIVE) {
			worldState.m[nbody.engineRef.index] = nbody.mass * massScale;
		} else {
			Debug.LogWarning("Cannot set mass on a massless body");
		}
		if (trajectoryPrediction) {
			trajectoryRestart = true;
		}
	}


	/// <summary>
	/// Gets the world center of mass in world space co-ordinates.
	/// </summary>
	/// <returns>The world center of mass.</returns>
	public Vector3 GetWorldCenterOfMass() {
		// called by editor prior to setup - need to find all the NBodies
		NBody[] nbodies = (NBody[]) Object.FindObjectsOfType(typeof(NBody));

		Vector3 cmVector = Vector3.zero;
		float mTotal = 0.0f; 
		foreach( NBody nbody in nbodies) {
			cmVector += ((float) nbody.mass) * nbody.transform.position;
			mTotal += ((float) nbody.mass);
		}
		return cmVector/mTotal;		
	}

	/// <summary>
	/// Gets the world center of mass velocity.
	/// </summary>
	/// <returns>The world center of mass velocity.</returns>
	public Vector3 GetWorldCenterOfMassVelocity() {
		// called by editor prior to setup - need to find all the NBodies
		NBody[] nbodies = (NBody[]) Object.FindObjectsOfType(typeof(NBody));

		Vector3 cmVector = Vector3.zero;
		float mTotal = 0.0f; 
		foreach( NBody nbody in nbodies) {
			cmVector += ((float) nbody.mass) * nbody.vel;
			mTotal += ((float) nbody.mass);
		}
		return cmVector/mTotal;		
	}

	/// <summary>
	/// Gets the initial energy.
	/// </summary>
	/// <returns>The initial energy.</returns>
	public float GetInitialEnergy() {
		return integrator.GetInitialEnergy(worldState);
	}

	/// <summary>
	/// Gets the current energy.
	/// </summary>
	/// <returns>The energy.</returns>
	public float GetEnergy() {
	    if (isSetup)
		return integrator.GetEnergy(worldState);
            else
                return 0f;
	}

	/// <summary>
	/// Register a particle system (with GravityParticles component) to be evolved via the GravityEngine.
	/// </summary>
	/// <param name="nbp">Nbp.</param>
	public void RegisterParticles(GravityParticles nbp) {
		nbodyParticles.Add(nbp);
	}

	/// <summary>
	/// Remove a particle system from the Gravity Engine. 
	/// </summary>
	/// <param name="particles">Particles.</param>
	public void DeregisterParticles(GravityParticles particles) {
		nbodyParticles.Remove(particles);
	}

	//-------------------------------------------------------------------------------
	// Maneuver wrappers

	public void AddManeuver(Maneuver m) {
		maneuverMgr.Add(m);
	}

	public void RemoveManeuver(Maneuver m) {
		maneuverMgr.Remove(m);
	}

	public List<Maneuver> GetManeuvers(NBody nbody) {
		return maneuverMgr.GetManeuvers(nbody);
	}


	//-------------------------------------------------------------------------------

	private void DumpAll(GravityState gs) {
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		sb.Append(string.Format("massScale={0} timeScale={1}\n", massScale, timeScale));
		sb.Append("Massive Bodies:\n");
		for (int i=0; i < numBodies; i++) {
			Vector3 vel = GetVelocity(gameObjects[i]);
			sb.Append(string.Format("n={0} {1} m={2} r={3} {4} {5} v={6} {7} {8} info={9} \n", i, gameObjects[i].name, 
				gs.m[i], gs.r[i,0], gs.r[i,1], gs.r[i,2], 
				vel.x, vel.y, vel.z, info[i]
					));
		}
		if (masslessEngine != null) {
			sb.Append(masslessEngine.DumpString());
		}
		Debug.Log(sb.ToString());
	}
	
	
}
