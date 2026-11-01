#pragma once

#include <memory>

#include "vehicle.hpp"

namespace simcore {

struct InitialConditions {
    Vec3 pos_ned;
    Vec3 vel_body;
    Euler attitude;
    Vec3 omega_body;
};

struct TrimResult {
    bool converged = false;
    int iterations = 0;
    double residual = 0.0;  // max |acceleration| left, SI units
    Controls controls;
    RigidBodyState state;
};

struct Telemetry {
    double time = 0.0;
    RigidBodyState state;
    Euler attitude;
    Vec3 vel_ned;
    double altitude = 0.0;
    AirData air;
    bool on_ground = false;
};

class Simulation {
public:
    explicit Simulation(std::unique_ptr<VehicleModel> vehicle);

    void reset(const InitialConditions& ic);
    void set_controls(const Controls& c) { controls_ = clamp_controls(c); }
    void set_wind(const Vec3& wind_ned) { wind_ned_ = wind_ned; }

    // Advances by dt using fixed-step RK4 with substeps no longer than
    // max_substep. Returns false (state unchanged) if the result is not finite.
    bool step(double dt);

    // Finds controls and attitude for steady, wings-level, straight flight at
    // the given true airspeed (0 = hover), heading north. On success the
    // simulation state and controls are set to the trim point.
    TrimResult trim(double airspeed, double altitude);

    Wrench total_wrench(const RigidBodyState& s, const Controls& c) const;
    StateDerivative derivative(const RigidBodyState& s, const Controls& c) const;
    Environment environment_at(const RigidBodyState& s) const;

    Telemetry telemetry() const;
    const RigidBodyState& state() const { return state_; }
    const Controls& controls() const { return controls_; }
    double time() const { return time_; }
    const VehicleModel& vehicle() const { return *vehicle_; }

    double max_substep = 0.005;  // s

private:
    Wrench ground_wrench(const RigidBodyState& s) const;
    bool in_ground_contact(const RigidBodyState& s) const;

    std::unique_ptr<VehicleModel> vehicle_;
    RigidBodyState state_;
    Controls controls_;
    Vec3 wind_ned_;
    double time_ = 0.0;
};

}  // namespace simcore
