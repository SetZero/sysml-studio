/*
 * What the drone has to do, and which part answers for it.
 */
package DroneRequirements {
    private import DroneLifecycle::*;
    private import DroneStructure::*;

    requirement def <'R.1'> FlightTime {
        doc /* The drone stays airborne for 30 minutes with a full load. */
    }

    requirement def <'R.2'> Payload {
        doc /* It carries a parcel of up to 2 kg. */
    }

    requirement def <'R.3'> Stability {
        doc /* It holds its position within 0.5 m in a 10 m/s wind. */
    }

    #inProgress
    requirement def <'R.4'> Navigation {
        doc /* It finds the delivery address to within 2 m. */
    }

    #planned
    requirement def <'R.5'> SafeReturn {
        doc /* On a lost link it flies home and lands on its own. */
    }

    satisfy FlightTime by Drone::battery;
    satisfy Payload by Drone::bay;
    satisfy Stability by Drone::fc;
    satisfy Stability by Drone::imu;
    satisfy Navigation by Drone::gps;
    satisfy SafeReturn by Drone::fc;
}
