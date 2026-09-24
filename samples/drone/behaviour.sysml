/*
 * How a delivery flight goes, and the modes the flight controller moves through.
 */
package DroneBehaviour {
    private import DroneLifecycle::*;

    action def Deliver {
        action preflightCheck;
        action takeOff;
        action cruise;
        action descend;
        action releaseParcel;
        action returnHome;
        action land;
        first preflightCheck then takeOff;
        first takeOff then cruise;
        first cruise then descend;
        first descend then releaseParcel;
        first releaseParcel then returnHome;
        first returnHome then land;
    }

    state def FlightModes {
        entry; then disarmed;
        state disarmed;
        state armed;
        state flying;
        state hovering;
        state landing;
        state failsafe;
        transition arm first disarmed then armed;
        transition launch first armed then flying;
        transition hold first flying then hovering;
        transition resume first hovering then flying;
        transition descend first flying then landing;
        transition touchdown first landing then disarmed;
        transition linkLost first flying then failsafe;
        transition recover first failsafe then landing;
    }
}
