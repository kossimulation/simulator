#pragma once

#include "vehicle.hpp"

namespace simcore {

// Light twin-engine helicopter (Bo-105 class). Quasi-static main rotor:
// momentum-theory thrust and torque, tip-path-plane tilt from cyclic,
// hingeless hub moment. Tail rotor has ideal collective-to-pedal mixing
// (cancels main rotor torque) plus pedal authority. No inflow dynamics,
// translational lift or ground effect yet.
struct HelicopterParams {
    MassProperties mass{2200.0, {1433.0, 4973.0, 4099.0}};

    double rotor_radius = 4.91;   // m
    double rotor_speed = 44.4;    // rad/s
    double ct_max = 0.0082;       // thrust coefficient at full collective
    double figure_of_merit = 0.7;
    double profile_power = 60e3;  // W
    double max_disc_tilt = 0.14;  // rad, full cyclic
    double hub_height = 1.0;      // m above CG
    double hub_stiffness = 1.0e5; // N m / rad

    double tail_arm = 6.0;             // m behind CG
    double tail_pedal_authority = 1500.0;  // N at full pedal

    Vec3 fuselage_drag_area{1.5, 8.0, 10.0};         // f_x, f_y, f_z, m^2
    Vec3 rotor_damping{15000.0, 25000.0, 9000.0};    // N m s / rad

    double skid_height = 1.0;  // m, CG above ground when landed
};

class Helicopter final : public VehicleModel {
public:
    explicit Helicopter(const HelicopterParams& p = {}) : p_(p) {}

    const char* name() const override { return "helicopter"; }
    const MassProperties& mass_properties() const override { return p_.mass; }
    Wrench compute_wrench(const RigidBodyState& s, const Controls& c,
                          const Environment& env) const override;
    double ground_contact_offset() const override { return p_.skid_height; }

    const HelicopterParams& params() const { return p_; }

private:
    HelicopterParams p_;
};

}  // namespace simcore
