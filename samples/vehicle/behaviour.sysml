/*
 * How the vehicle starts, and the states its engine goes through.
 */
package VehicleBehaviour {
    private import VehicleLifecycle::*;

    action def StartUp {
        action unlock;
        action crank;
        action idle;
        first unlock then crank;
        first crank then idle;
    }

    state def EngineStates {
        entry; then off;
        state off;
        state cranking;
        state running;
        transition start first off then cranking;
        transition caught first cranking then running;
        transition stop first running then off;
    }
}
