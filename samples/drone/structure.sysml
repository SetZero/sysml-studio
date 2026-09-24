/*
 * What a delivery drone is made of, and how its parts are wired.
 */
package DroneStructure {
    private import ScalarValues::*;
    private import DroneLifecycle::*;

    port def PowerPort;
    port def DataPort;
    port def ThrustPort;

    #implemented
    part def <'D.1'> Battery {
        doc /* Six-cell lithium pack, swappable in the field. */
        attribute capacity : Real;
        attribute voltage : Real;
        port power : PowerPort;
    }

    #implemented
    part def <'D.2'> FlightController {
        doc /* Runs the attitude and navigation loops. */
        attribute loopRate : Real;
        port power : PowerPort;
        port imu : DataPort;
        port gps : DataPort;
        port esc : DataPort;
    }

    part def Sensor {
        port data : DataPort;
    }

    #implemented
    part def Imu :> Sensor {
        attribute sampleRate : Real;
    }

    #inProgress
    part def Gps :> Sensor {
        attribute accuracy : Real;
    }

    #implemented
    part def Motor {
        attribute kv : Real;
        port power : PowerPort;
        port control : DataPort;
        port shaft : ThrustPort;
    }

    part def Propeller {
        attribute diameter : Real;
        port hub : ThrustPort;
    }

    #planned
    part def PayloadBay {
        doc /* Holds and releases one parcel. */
        attribute maxLoad : Real;
    }

    #implemented
    part def <'D.0'> Drone {
        doc /* A quadcopter that carries one parcel to a doorstep and back. */
        part battery : Battery;
        part fc : FlightController;
        part imu : Imu;
        part gps : Gps;
        part motors : Motor[4];
        part props : Propeller[4];
        part bay : PayloadBay;
        connection mainPower connect battery.power to fc.power;
        connection motorPower connect battery.power to motors.power;
        connection imuLink connect imu.data to fc.imu;
        connection gpsLink connect gps.data to fc.gps;
        connection escBus connect fc.esc to motors.control;
        connection drive connect motors.shaft to props.hub;
    }
}
