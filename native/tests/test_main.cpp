// Minimal self-contained test runner (no external dependencies).

#include <cmath>
#include <cstdio>
#include <functional>
#include <memory>
#include <string>
#include <vector>

#include "fixed_wing.hpp"
#include "helicopter.hpp"
#include "simcore/simcore.h"
#include "simulation.hpp"

using namespace simcore;

namespace {

struct TestCase {
    const char* name;
    std::function<void()> fn;
};

std::vector<TestCase>& registry() {
    static std::vector<TestCase> r;
    return r;
}

struct Registrar {
    Registrar(const char* name, std::function<void()> fn) { registry().push_back({name, fn}); }
};

int g_failures = 0;

#define TEST(name)                                        \
    static void name();                                   \
    static const Registrar name##_registrar(#name, name); \
    static void name()

#define CHECK(cond)                                                              \
    do {                                                                         \
        if (!(cond)) {                                                           \
            std::printf("    %s:%d: CHECK(%s) failed\n", __FILE__, __LINE__, #cond); \
            ++g_failures;                                                        \
        }                                                                        \
    } while (0)

#define CHECK_NEAR(a, b, tol)                                                        \
    do {                                                                             \
        const double va_ = (a), vb_ = (b);                                           \
        if (!(std::abs(va_ - vb_) <= (tol))) {                                       \
            std::printf("    %s:%d: CHECK_NEAR(%s, %s) failed: %.9g vs %.9g (tol %g)\n", \
                        __FILE__, __LINE__, #a, #b, va_, vb_, static_cast<double>(tol)); \
            ++g_failures;                                                            \
        }                                                                            \
    } while (0)

// Vehicle producing no aerodynamic forces: pure rigid body under gravity.
class InertBody final : public VehicleModel {
public:
    explicit InertBody(MassProperties mp) : mp_(mp) {}
    const char* name() const override { return "inert"; }
    const MassProperties& mass_properties() const override { return mp_; }
    Wrench compute_wrench(const RigidBodyState&, const Controls&,
                          const Environment&) const override {
        return {};
    }

private:
    MassProperties mp_;
};

void run(Simulation& sim, double seconds, double dt = 0.01) {
    const int n = static_cast<int>(std::lround(seconds / dt));
    for (int i = 0; i < n; ++i) CHECK(sim.step(dt));
}

}  // namespace

// --- Atmosphere -------------------------------------------------------------

TEST(isa_sea_level) {
    const AtmosphereState a = isa(0.0);
    CHECK_NEAR(a.temperature, 288.15, 1e-9);
    CHECK_NEAR(a.pressure, 101325.0, 1e-6);
    CHECK_NEAR(a.density, 1.225, 1e-3);
    CHECK_NEAR(a.speed_of_sound, 340.29, 0.01);
}

TEST(isa_tropopause_and_stratosphere) {
    CHECK_NEAR(isa(11000.0).temperature, 216.65, 1e-9);
    CHECK_NEAR(isa(11000.0).pressure, 22632.0, 1.0);
    CHECK_NEAR(isa(15000.0).temperature, 216.65, 1e-9);
    CHECK_NEAR(isa(15000.0).pressure, 12045.0, 5.0);
}

// --- Math / rigid body --------------------------------------------------------

TEST(euler_quaternion_round_trip) {
    const Euler e{0.3, -0.4, 2.5};
    const Euler r = to_euler(from_euler(e));
    CHECK_NEAR(r.roll, e.roll, 1e-12);
    CHECK_NEAR(r.pitch, e.pitch, 1e-12);
    CHECK_NEAR(r.yaw, e.yaw, 1e-12);
}

TEST(dcm_rotates_body_forward_to_heading) {
    const Mat3 r = to_dcm(from_euler({0.0, 0.0, kPi / 2}));  // heading east
    const Vec3 v = r * Vec3{1.0, 0.0, 0.0};
    CHECK_NEAR(v.x, 0.0, 1e-12);
    CHECK_NEAR(v.y, 1.0, 1e-12);
    CHECK_NEAR(v.z, 0.0, 1e-12);
}

TEST(free_fall_matches_analytic) {
    Simulation sim(std::make_unique<InertBody>(MassProperties{10.0, {1, 1, 1}}));
    InitialConditions ic;
    ic.pos_ned = {0.0, 0.0, -1000.0};
    sim.reset(ic);
    run(sim, 2.0);
    const double g = kStandardGravity;
    CHECK_NEAR(sim.telemetry().vel_ned.z, g * 2.0, 1e-9);
    CHECK_NEAR(sim.telemetry().altitude, 1000.0 - 0.5 * g * 4.0, 1e-9);
}

TEST(torque_free_rotation_conserves_energy_and_momentum) {
    const MassProperties mp{5.0, {1.0, 2.0, 3.0}};
    Simulation sim(std::make_unique<InertBody>(mp));
    InitialConditions ic;
    ic.pos_ned = {0.0, 0.0, -10000.0};
    ic.omega_body = {0.1, 2.0, 0.1};  // near the unstable intermediate axis
    sim.reset(ic);

    auto energy = [&](const Vec3& w) {
        return 0.5 * (mp.inertia.x * w.x * w.x + mp.inertia.y * w.y * w.y +
                      mp.inertia.z * w.z * w.z);
    };
    auto momentum = [&](const RigidBodyState& s) {
        const Vec3& w = s.omega_body;
        return to_dcm(s.att) * Vec3{mp.inertia.x * w.x, mp.inertia.y * w.y, mp.inertia.z * w.z};
    };
    const double e0 = energy(sim.state().omega_body);
    const Vec3 h0 = momentum(sim.state());
    run(sim, 10.0, 0.01);
    CHECK_NEAR(energy(sim.state().omega_body), e0, 1e-6 * e0);
    const Vec3 h1 = momentum(sim.state());
    CHECK_NEAR(h1.x, h0.x, 1e-5);
    CHECK_NEAR(h1.y, h0.y, 1e-5);
    CHECK_NEAR(h1.z, h0.z, 1e-5);
}

// --- Fixed wing -------------------------------------------------------------

TEST(fixed_wing_trim_holds_level_flight) {
    Simulation sim(std::make_unique<FixedWing>());
    const TrimResult t = sim.trim(50.0, 1000.0);
    CHECK(t.converged);
    CHECK(t.controls.throttle > 0.1 && t.controls.throttle < 0.9);
    const double alpha = sim.telemetry().air.alpha;
    CHECK(alpha > 0.0 && alpha < 0.1);

    run(sim, 20.0);
    const Telemetry tm = sim.telemetry();
    CHECK_NEAR(tm.altitude, 1000.0, 0.5);
    CHECK_NEAR(tm.air.airspeed, 50.0, 0.1);
    CHECK_NEAR(tm.attitude.roll, 0.0, 1e-3);
    CHECK_NEAR(tm.state.pos_ned.x, 50.0 * 20.0, 1.0);
}

TEST(fixed_wing_control_signs) {
    Simulation base(std::make_unique<FixedWing>());
    CHECK(base.trim(50.0, 1000.0).converged);
    const Controls trim = base.controls();

    {  // Aft stick: nose up, then climb.
        Simulation sim(std::make_unique<FixedWing>());
        sim.trim(50.0, 1000.0);
        Controls c = trim;
        c.pitch += 0.1;
        sim.set_controls(c);
        run(sim, 0.5);
        CHECK(sim.state().omega_body.y > 0.0);
        run(sim, 2.5);
        CHECK(sim.telemetry().altitude > 1000.0);
    }
    {  // Right stick: roll right.
        Simulation sim(std::make_unique<FixedWing>());
        sim.trim(50.0, 1000.0);
        Controls c = trim;
        c.roll += 0.2;
        sim.set_controls(c);
        run(sim, 0.5);
        CHECK(sim.state().omega_body.x > 0.0);
        CHECK(sim.telemetry().attitude.roll > 0.0);
    }
    {  // Right pedal: nose right.
        Simulation sim(std::make_unique<FixedWing>());
        sim.trim(50.0, 1000.0);
        Controls c = trim;
        c.yaw += 0.2;
        sim.set_controls(c);
        run(sim, 0.5);
        CHECK(sim.state().omega_body.z > 0.0);
    }
}

// --- Helicopter -------------------------------------------------------------

TEST(helicopter_hover_trim_holds_position) {
    Simulation sim(std::make_unique<Helicopter>());
    const TrimResult t = sim.trim(0.0, 100.0);
    CHECK(t.converged);
    CHECK(t.controls.throttle > 0.3 && t.controls.throttle < 0.9);
    // Tail rotor pushes right; the aircraft hovers banked slightly left.
    CHECK(to_euler(t.state.att).roll < 0.0);

    run(sim, 10.0);
    const Telemetry tm = sim.telemetry();
    CHECK_NEAR(tm.altitude, 100.0, 0.01);
    CHECK_NEAR(tm.state.pos_ned.x, 0.0, 0.01);
    CHECK_NEAR(tm.state.pos_ned.y, 0.0, 0.01);
    CHECK_NEAR(tm.attitude.yaw, 0.0, 1e-4);
}

TEST(helicopter_forward_flight_trim) {
    Simulation sim(std::make_unique<Helicopter>());
    const TrimResult t = sim.trim(30.0, 500.0);
    CHECK(t.converged);
    CHECK(sim.telemetry().attitude.pitch < 0.0);  // nose down to fly forward
    run(sim, 10.0);
    CHECK_NEAR(sim.telemetry().altitude, 500.0, 0.1);
    CHECK_NEAR(sim.telemetry().vel_ned.x, 30.0, 0.05);
}

TEST(helicopter_control_signs) {
    auto trimmed = [] {
        auto sim = std::make_unique<Simulation>(std::make_unique<Helicopter>());
        sim->trim(0.0, 100.0);
        return sim;
    };
    {
        auto sim = trimmed();
        Controls c = sim->controls();
        c.throttle += 0.05;
        sim->set_controls(c);
        run(*sim, 3.0);
        CHECK(sim->telemetry().altitude > 100.5);
    }
    {
        auto sim = trimmed();
        Controls c = sim->controls();
        c.yaw += 0.2;
        sim->set_controls(c);
        run(*sim, 0.5);
        CHECK(sim->state().omega_body.z > 0.0);
    }
    {
        auto sim = trimmed();
        Controls c = sim->controls();
        c.roll += 0.2;
        sim->set_controls(c);
        run(*sim, 0.3);
        CHECK(sim->state().omega_body.x > 0.0);
    }
    {
        auto sim = trimmed();
        Controls c = sim->controls();
        c.pitch -= 0.2;  // forward stick
        sim->set_controls(c);
        run(*sim, 3.0);
        CHECK(sim->telemetry().vel_ned.x > 0.0);
    }
}

TEST(helicopter_settles_on_ground) {
    Simulation sim(std::make_unique<Helicopter>());
    InitialConditions ic;
    ic.pos_ned = {0.0, 0.0, -5.0};
    sim.reset(ic);
    sim.set_controls({});
    run(sim, 10.0);
    const Telemetry t = sim.telemetry();
    CHECK(t.on_ground);
    CHECK_NEAR(t.altitude, 1.0 - 0.05, 0.01);
    CHECK(norm(t.vel_ned) < 0.01);
}

// --- C API ------------------------------------------------------------------

TEST(c_api_round_trip) {
    CHECK(simcore_api_version() == SIMCORE_API_VERSION);
    CHECK(sim_create(static_cast<sim_vehicle_type>(42)) == nullptr);

    sim_handle h = sim_create(SIM_VEHICLE_FIXED_WING);
    CHECK(h != nullptr);
    sim_controls c{};
    CHECK(sim_trim(h, 50.0, 1000.0, &c) == SIM_OK);
    CHECK(c.throttle > 0.0);
    for (int i = 0; i < 100; ++i) CHECK(sim_step(h, 0.01) == SIM_OK);

    sim_state s{};
    CHECK(sim_get_state(h, &s) == SIM_OK);
    CHECK_NEAR(s.time, 1.0, 1e-9);
    CHECK_NEAR(s.altitude, 1000.0, 0.1);
    CHECK_NEAR(s.airspeed, 50.0, 0.1);
    CHECK(s.on_ground == 0);

    CHECK(sim_step(h, 0.0) == SIM_ERR_INVALID_ARGUMENT);
    CHECK(sim_step(h, NAN) == SIM_ERR_INVALID_ARGUMENT);
    CHECK(sim_get_state(h, nullptr) == SIM_ERR_NULL_ARGUMENT);
    CHECK(sim_step(nullptr, 0.01) == SIM_ERR_NULL_ARGUMENT);
    sim_destroy(h);
    sim_destroy(nullptr);
}

int main() {
    int failed_tests = 0;
    for (const TestCase& t : registry()) {
        const int before = g_failures;
        t.fn();
        const bool ok = g_failures == before;
        if (!ok) ++failed_tests;
        std::printf("[%s] %s\n", ok ? " OK " : "FAIL", t.name);
    }
    std::printf("\n%zu tests, %d failed\n", registry().size(), failed_tests);
    return failed_tests == 0 ? 0 : 1;
}
