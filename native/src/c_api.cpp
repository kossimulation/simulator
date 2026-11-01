#include "simcore/simcore.h"

#include <cmath>
#include <memory>

#include "fixed_wing.hpp"
#include "helicopter.hpp"
#include "simulation.hpp"

struct sim_instance {
    simcore::Simulation sim;
};

namespace {

using simcore::Controls;

Controls from_c(const sim_controls& c) { return {c.throttle, c.pitch, c.roll, c.yaw}; }
sim_controls to_c(const Controls& c) { return {c.throttle, c.pitch, c.roll, c.yaw}; }

// Exceptions must never cross the C boundary.
template <typename F>
sim_status guarded(F&& f) {
    try {
        return f();
    } catch (...) {
        return SIM_ERR_INTERNAL;
    }
}

}  // namespace

extern "C" {

int32_t simcore_api_version(void) { return SIMCORE_API_VERSION; }

sim_handle sim_create(sim_vehicle_type type) {
    try {
        std::unique_ptr<simcore::VehicleModel> vehicle;
        switch (type) {
            case SIM_VEHICLE_FIXED_WING: vehicle = std::make_unique<simcore::FixedWing>(); break;
            case SIM_VEHICLE_HELICOPTER: vehicle = std::make_unique<simcore::Helicopter>(); break;
            default: return nullptr;
        }
        const double gear = vehicle->ground_contact_offset();
        auto* h = new sim_instance{simcore::Simulation(std::move(vehicle))};
        simcore::InitialConditions ic;
        ic.pos_ned = {0.0, 0.0, -gear};
        h->sim.reset(ic);
        return h;
    } catch (...) {
        return nullptr;
    }
}

void sim_destroy(sim_handle sim) { delete sim; }

sim_status sim_reset(sim_handle sim, const sim_initial_conditions* ic) {
    if (!sim || !ic) return SIM_ERR_NULL_ARGUMENT;
    return guarded([&] {
        simcore::InitialConditions c;
        c.pos_ned = {ic->pos_north, ic->pos_east, -ic->altitude};
        c.vel_body = {ic->u, ic->v, ic->w};
        c.attitude = {ic->roll, ic->pitch, ic->yaw};
        c.omega_body = {ic->p, ic->q, ic->r};
        sim->sim.reset(c);
        return simcore::is_finite(sim->sim.state()) ? SIM_OK : SIM_ERR_INVALID_ARGUMENT;
    });
}

sim_status sim_set_controls(sim_handle sim, const sim_controls* controls) {
    if (!sim || !controls) return SIM_ERR_NULL_ARGUMENT;
    const Controls c = from_c(*controls);
    if (!std::isfinite(c.throttle) || !std::isfinite(c.pitch) || !std::isfinite(c.roll) ||
        !std::isfinite(c.yaw))
        return SIM_ERR_INVALID_ARGUMENT;
    sim->sim.set_controls(c);
    return SIM_OK;
}

sim_status sim_get_controls(sim_handle sim, sim_controls* controls) {
    if (!sim || !controls) return SIM_ERR_NULL_ARGUMENT;
    *controls = to_c(sim->sim.controls());
    return SIM_OK;
}

sim_status sim_set_wind(sim_handle sim, double north, double east, double down) {
    if (!sim) return SIM_ERR_NULL_ARGUMENT;
    if (!std::isfinite(north) || !std::isfinite(east) || !std::isfinite(down))
        return SIM_ERR_INVALID_ARGUMENT;
    sim->sim.set_wind({north, east, down});
    return SIM_OK;
}

sim_status sim_step(sim_handle sim, double dt) {
    if (!sim) return SIM_ERR_NULL_ARGUMENT;
    if (!(dt > 0.0) || !std::isfinite(dt)) return SIM_ERR_INVALID_ARGUMENT;
    return guarded([&] { return sim->sim.step(dt) ? SIM_OK : SIM_ERR_NUMERIC; });
}

sim_status sim_get_state(sim_handle sim, sim_state* out) {
    if (!sim || !out) return SIM_ERR_NULL_ARGUMENT;
    return guarded([&] {
        const simcore::Telemetry t = sim->sim.telemetry();
        const simcore::RigidBodyState& s = t.state;
        sim_state o{};
        o.time = t.time;
        o.pos_north = s.pos_ned.x;
        o.pos_east = s.pos_ned.y;
        o.pos_down = s.pos_ned.z;
        o.altitude = t.altitude;
        o.u = s.vel_body.x;
        o.v = s.vel_body.y;
        o.w = s.vel_body.z;
        o.vel_north = t.vel_ned.x;
        o.vel_east = t.vel_ned.y;
        o.vel_down = t.vel_ned.z;
        o.roll = t.attitude.roll;
        o.pitch = t.attitude.pitch;
        o.yaw = t.attitude.yaw;
        o.p = s.omega_body.x;
        o.q = s.omega_body.y;
        o.r = s.omega_body.z;
        o.qw = s.att.w;
        o.qx = s.att.x;
        o.qy = s.att.y;
        o.qz = s.att.z;
        o.airspeed = t.air.airspeed;
        o.alpha = t.air.alpha;
        o.beta = t.air.beta;
        o.on_ground = t.on_ground ? 1 : 0;
        *out = o;
        return SIM_OK;
    });
}

sim_status sim_trim(sim_handle sim, double airspeed, double altitude, sim_controls* out_controls) {
    if (!sim) return SIM_ERR_NULL_ARGUMENT;
    if (!(airspeed >= 0.0) || !std::isfinite(airspeed) || !std::isfinite(altitude))
        return SIM_ERR_INVALID_ARGUMENT;
    return guarded([&] {
        const simcore::TrimResult r = sim->sim.trim(airspeed, altitude);
        if (!r.converged) return SIM_ERR_NOT_CONVERGED;
        if (out_controls) *out_controls = to_c(r.controls);
        return SIM_OK;
    });
}

}  // extern "C"

// Layout is mirrored by managed/src/Simulator.Core/Native/NativeTypes.cs.
static_assert(sizeof(sim_controls) == 4 * sizeof(double), "sim_controls layout changed");
static_assert(sizeof(sim_initial_conditions) == 12 * sizeof(double), "sim_initial_conditions layout changed");
static_assert(sizeof(sim_state) == 200, "sim_state layout changed");
