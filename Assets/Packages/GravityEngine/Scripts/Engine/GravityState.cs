using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gravity state.
/// Basic container to hold mass/position for massive bodies. 
///
/// </summary>
public class GravityState  {

    //! masses of the massive bodies in the engine
	public double[] m;
    //! physics positions of massive objects in the engine
	public double[,] r; 	
    //! size of the arrays (may exceed the number of bodies due to pre-allocation)
	public int arraySize;

    //! time of current state (in the engine physics time)
    public double time;

    //!  physical time per evolver since start OR last timescale change
    public enum Evolvers { MASSIVE, MASSLESS, FIXED, PARTICLES };
    public double[] physicalTime; 


    // Integrators - these are held in GE
    public INBodyIntegrator integrator;
    public MasslessBodyEngine masslessEngine;


    public GravityState(int size) {
		m = new double[size];
		r = new double[size, GravityEngine.NDIM];
        physicalTime = new double[System.Enum.GetNames(typeof(Evolvers)).Length];
        arraySize = size;
	}

	// clone constructor
	public GravityState(GravityState fromState) {
		m = new double[fromState.arraySize];
		r = new double[fromState.arraySize, GravityEngine.NDIM];
        physicalTime = new double[System.Enum.GetNames(typeof(Evolvers)).Length];
        arraySize = fromState.arraySize;

        for (int i = 0; i < physicalTime.Length; i++) {
            physicalTime[i] = fromState.physicalTime[i];
        }
        time = fromState.time;

        for (int i=0; i < arraySize; i++) {
			m[i] = fromState.m[i];
			for (int j=0; j < GravityEngine.NDIM; j++) {
				r[i,j] = fromState.r[i,j];
			}
		}
	}

	public bool GrowArrays(int growBy) {
		double[] m_copy = new double[arraySize];
		double[,] r_copy = new double[arraySize, GravityEngine.NDIM];

		for (int i=0; i < arraySize; i++) {
			m_copy[i] = m[i];
			r_copy[i,0] = r[i,0];
			r_copy[i,1] = r[i,1];
			r_copy[i,2] = r[i,2];
		}

		m = new double[arraySize+growBy];
		r = new double[arraySize+growBy, GravityEngine.NDIM];

		for (int i=0; i < arraySize; i++) {
			m[i] = m_copy[i];
			r[i,0] = r_copy[i,0];
			r[i,1] = r_copy[i,1];
			r[i,2] = r_copy[i,2];
		}
		arraySize += growBy;
		return true;
	}

    public void ResetPhysicalTime() {
        for (int i = 0; i < physicalTime.Length; i++) {
            physicalTime[i] = 0.0;
        }
    }


}
