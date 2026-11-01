#pragma once

#include "atmosphere.hpp"
#include "rigid_body.hpp"

namespace simcore {

// Normalised pilot inputs.
//   throttle: [0, 1]  engine throttle (aircraft) or collective (helicopter)
//   pitch:    [-1, 1] positive = nose up (stick aft)
//   roll:     [-1, 1] positive = right wing down (stick right)
//   yaw:      [-1, 1] positive = nose right (right pedal)
struct Controls {
    double throttle = 0.0;
    double pitch = 0.0;
    double roll = 0.0;
    double yaw = 0.0;
};

Controls clamp_controls(const Controls& c);

struct Environment {
    AtmosphereState atmosphere;
    Vec3 wind_ned;
    double gravity = kStandardGravity;
};

struct AirData {
    Vec3 vel_air_body;     // velocity relative to the air mass, body frame
    double airspeed = 0.0;  // m/s
    double alpha = 0.0;     // angle of attack, rad
    double beta = 0.0;      // sideslip, rad
    double qbar = 0.0;      // dynamic pressure, Pa
};

AirData compute_air_data(const RigidBodyState& s, const Environment& env);

// A vehicle supplies mass properties and the aerodynamic/propulsive wrench.
// Gravity and ground contact are added by the Simulation.
//
// Models generated from Simulink (Embedded Coder, C++ class interface) are
// integrated by writing a thin adapter that implements this interface and
// calls the generated class' step() from compute_wrench(). See
// docs/architecture.md.
class VehicleModel {
public:
    virtual ~VehicleModel() = default;

    virtual const char* name() const = 0;
    virtual const MassProperties& mass_properties() const = 0;
    virtual Wrench compute_wrench(const RigidBodyState& s, const Controls& c,
                                  const Environment& env) const = 0;

    // Distance from the CG down to the lowest point (wheels/skids), m.
    virtual double ground_contact_offset() const { return 1.0; }
};

}  // namespace simcore
