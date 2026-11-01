/*
 * simcore — C ABI of the native flight-dynamics core.
 *
 * This is the only boundary consumed by the C# layer (P/Invoke). Keep it plain
 * C: opaque handles, POD structs of doubles/int32, integer status codes. Any
 * change to struct layouts must be mirrored in managed/src/Simulator.Core/Native
 * and must bump SIMCORE_API_VERSION.
 *
 * Frames and units: SI units, radians. NED world frame (north, east, down),
 * FRD body frame (forward, right, down).
 */
#ifndef SIMCORE_SIMCORE_H
#define SIMCORE_SIMCORE_H

#include <stdint.h>

#if defined(SIMCORE_STATIC)
#  define SIMCORE_API
#elif defined(_WIN32)
#  if defined(SIMCORE_BUILDING)
#    define SIMCORE_API __declspec(dllexport)
#  else
#    define SIMCORE_API __declspec(dllimport)
#  endif
#else
#  define SIMCORE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SIMCORE_API_VERSION 1

typedef struct sim_instance* sim_handle;

typedef enum sim_vehicle_type {
    SIM_VEHICLE_FIXED_WING = 0,
    SIM_VEHICLE_HELICOPTER = 1
} sim_vehicle_type;

typedef enum sim_status {
    SIM_OK = 0,
    SIM_ERR_NULL_ARGUMENT = -1,
    SIM_ERR_INVALID_ARGUMENT = -2,
    SIM_ERR_NOT_CONVERGED = -3,
    SIM_ERR_NUMERIC = -4,
    SIM_ERR_INTERNAL = -5
} sim_status;

/* Normalised pilot inputs, see native/src/vehicle.hpp. Values are clamped. */
typedef struct sim_controls {
    double throttle; /* [0,1] throttle (aircraft) or collective (helicopter) */
    double pitch;    /* [-1,1] + nose up   */
    double roll;     /* [-1,1] + right     */
    double yaw;      /* [-1,1] + nose right */
} sim_controls;

typedef struct sim_initial_conditions {
    double pos_north, pos_east, altitude; /* m */
    double u, v, w;                       /* body velocity, m/s */
    double roll, pitch, yaw;              /* rad */
    double p, q, r;                       /* body rates, rad/s */
} sim_initial_conditions;

typedef struct sim_state {
    double time;                          /* s */
    double pos_north, pos_east, pos_down; /* m */
    double altitude;                      /* m */
    double u, v, w;                       /* body velocity, m/s */
    double vel_north, vel_east, vel_down; /* m/s */
    double roll, pitch, yaw;              /* rad */
    double p, q, r;                       /* rad/s */
    double qw, qx, qy, qz;                /* attitude quaternion body->NED */
    double airspeed;                      /* m/s */
    double alpha, beta;                   /* rad */
    int32_t on_ground;                    /* 0/1 */
    int32_t reserved;
} sim_state;

SIMCORE_API int32_t simcore_api_version(void);

/* Returns NULL for an unknown vehicle type. */
SIMCORE_API sim_handle sim_create(sim_vehicle_type type);
SIMCORE_API void sim_destroy(sim_handle sim);

SIMCORE_API sim_status sim_reset(sim_handle sim, const sim_initial_conditions* ic);
SIMCORE_API sim_status sim_set_controls(sim_handle sim, const sim_controls* controls);
SIMCORE_API sim_status sim_get_controls(sim_handle sim, sim_controls* controls);
SIMCORE_API sim_status sim_set_wind(sim_handle sim, double north, double east, double down);

/* Advances simulated time by dt seconds (internally sub-stepped). */
SIMCORE_API sim_status sim_step(sim_handle sim, double dt);
SIMCORE_API sim_status sim_get_state(sim_handle sim, sim_state* state);

/*
 * Trims for steady wings-level flight heading north at the given true airspeed
 * (0 = hover). On success the simulation is reset to the trim point and the
 * trim controls are written to out_controls (may be NULL).
 */
SIMCORE_API sim_status sim_trim(sim_handle sim, double airspeed, double altitude,
                                sim_controls* out_controls);

#ifdef __cplusplus
}
#endif

#endif /* SIMCORE_SIMCORE_H */
