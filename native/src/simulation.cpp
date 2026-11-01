#include "simulation.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <stdexcept>

namespace simcore {

Simulation::Simulation(std::unique_ptr<VehicleModel> vehicle) : vehicle_(std::move(vehicle)) {
    if (!vehicle_) throw std::invalid_argument("vehicle must not be null");
}

void Simulation::reset(const InitialConditions& ic) {
    state_.pos_ned = ic.pos_ned;
    state_.vel_body = ic.vel_body;
    state_.att = from_euler(ic.attitude);
    state_.omega_body = ic.omega_body;
    time_ = 0.0;
}

Environment Simulation::environment_at(const RigidBodyState& s) const {
    Environment env;
    env.atmosphere = isa(-s.pos_ned.z);
    env.wind_ned = wind_ned_;
    return env;
}

bool Simulation::in_ground_contact(const RigidBodyState& s) const {
    return s.pos_ned.z + vehicle_->ground_contact_offset() > 0.0;
}

// Placeholder ground model: a single spring-damper contact under the CG with
// Coulomb-like friction and an attitude-levelling torque. Good enough to sit on
// the runway/helipad; not a landing-gear model.
Wrench Simulation::ground_wrench(const RigidBodyState& s) const {
    Wrench w;
    const double penetration = s.pos_ned.z + vehicle_->ground_contact_offset();
    if (penetration <= 0.0) return w;

    const MassProperties& mp = vehicle_->mass_properties();
    const double g = kStandardGravity;
    const double k = mp.mass * g / 0.05;  // 5 cm static deflection
    const double c = 2.0 * 0.7 * std::sqrt(k * mp.mass);

    const Mat3 r = to_dcm(s.att);
    const Vec3 vel_ned = r * s.vel_body;
    const double normal = std::max(0.0, k * penetration + c * vel_ned.z);

    const double mu = 0.5;
    const double vh = std::hypot(vel_ned.x, vel_ned.y);
    const double scale = mu * normal / std::max(vh, 0.1);
    const Vec3 force_ned{-scale * vel_ned.x, -scale * vel_ned.y, -normal};
    w.force = r.transposed() * force_ned;

    const double load = normal / (mp.mass * g);
    const double k_rot = mp.mass * g * 2.0;
    const double i_max = std::max({mp.inertia.x, mp.inertia.y, mp.inertia.z});
    const double c_rot = 2.0 * 0.7 * std::sqrt(k_rot * i_max);
    const Euler e = to_euler(s.att);
    w.moment = load * (Vec3{-k_rot * e.roll, -k_rot * e.pitch, 0.0} - c_rot * s.omega_body);
    return w;
}

Wrench Simulation::total_wrench(const RigidBodyState& s, const Controls& c) const {
    const Environment env = environment_at(s);
    Wrench w = vehicle_->compute_wrench(s, c, env);

    const MassProperties& mp = vehicle_->mass_properties();
    const Vec3 gravity_ned{0.0, 0.0, mp.mass * env.gravity};
    w.force += to_dcm(s.att).transposed() * gravity_ned;
    w += ground_wrench(s);
    return w;
}

StateDerivative Simulation::derivative(const RigidBodyState& s, const Controls& c) const {
    return compute_derivative(s, vehicle_->mass_properties(), total_wrench(s, c));
}

bool Simulation::step(double dt) {
    if (!(dt > 0.0) || !std::isfinite(dt)) return false;

    const int n = std::max(1, static_cast<int>(std::ceil(dt / max_substep)));
    const double h = dt / n;
    RigidBodyState s = state_;
    for (int i = 0; i < n; ++i) {
        const StateDerivative k1 = derivative(s, controls_);
        const StateDerivative k2 = derivative(advance(s, k1, h / 2), controls_);
        const StateDerivative k3 = derivative(advance(s, k2, h / 2), controls_);
        const StateDerivative k4 = derivative(advance(s, k3, h), controls_);
        s = advance(s, k1, h / 6);
        s = advance(s, k2, h / 3);
        s = advance(s, k3, h / 3);
        s = advance(s, k4, h / 6);
        s.att = normalized(s.att);
    }
    if (!is_finite(s)) return false;
    state_ = s;
    time_ += dt;
    return true;
}

namespace {

constexpr int kN = 6;
using VecN = std::array<double, kN>;
using MatN = std::array<VecN, kN>;

// Gaussian elimination with partial pivoting; returns false if singular.
bool solve(MatN a, VecN b, VecN& x) {
    for (int col = 0; col < kN; ++col) {
        int piv = col;
        for (int r = col + 1; r < kN; ++r)
            if (std::abs(a[r][col]) > std::abs(a[piv][col])) piv = r;
        if (std::abs(a[piv][col]) < 1e-12) return false;
        std::swap(a[col], a[piv]);
        std::swap(b[col], b[piv]);
        for (int r = col + 1; r < kN; ++r) {
            const double f = a[r][col] / a[col][col];
            for (int k = col; k < kN; ++k) a[r][k] -= f * a[col][k];
            b[r] -= f * b[col];
        }
    }
    for (int r = kN - 1; r >= 0; --r) {
        double sum = b[r];
        for (int k = r + 1; k < kN; ++k) sum -= a[r][k] * x[k];
        x[r] = sum / a[r][r];
    }
    return true;
}

double max_abs(const VecN& v) {
    double m = 0.0;
    for (double e : v) m = std::max(m, std::abs(e));
    return m;
}

}  // namespace

TrimResult Simulation::trim(double airspeed, double altitude) {
    // Unknowns: pitch angle, roll angle, throttle, pitch/roll/yaw inputs.
    // Residuals: body linear and angular accelerations.
    auto build = [&](const VecN& x, RigidBodyState& s, Controls& c) {
        s = {};
        s.pos_ned = {0.0, 0.0, -altitude};
        s.att = from_euler({x[1], x[0], 0.0});
        s.vel_body = to_dcm(s.att).transposed() * (Vec3{airspeed, 0.0, 0.0} + wind_ned_);
        c = {x[2], x[3], x[4], x[5]};
    };
    auto residual = [&](const VecN& x) {
        RigidBodyState s;
        Controls c;
        build(x, s, c);
        const StateDerivative d = derivative(s, c);
        return VecN{d.vel_dot.x, d.vel_dot.y, d.vel_dot.z,
                    d.omega_dot.x, d.omega_dot.y, d.omega_dot.z};
    };

    TrimResult result;
    VecN x{0.02, 0.0, 0.5, 0.0, 0.0, 0.0};
    VecN f = residual(x);
    constexpr int kMaxIter = 50;
    constexpr double kTol = 1e-9;
    constexpr double kEps = 1e-7;

    int it = 0;
    for (; it < kMaxIter && max_abs(f) > kTol; ++it) {
        MatN jac{};
        for (int j = 0; j < kN; ++j) {
            VecN xp = x;
            xp[j] += kEps;
            const VecN fp = residual(xp);
            for (int i = 0; i < kN; ++i) jac[i][j] = (fp[i] - f[i]) / kEps;
        }
        VecN neg_f;
        for (int i = 0; i < kN; ++i) neg_f[i] = -f[i];
        VecN dx{};
        if (!solve(jac, neg_f, dx)) break;

        // Backtracking line search on the residual norm.
        double step = 1.0;
        VecN xn = x, fn = f;
        for (int ls = 0; ls < 20; ++ls, step *= 0.5) {
            for (int i = 0; i < kN; ++i) xn[i] = x[i] + step * dx[i];
            fn = residual(xn);
            if (max_abs(fn) < max_abs(f)) break;
        }
        x = xn;
        f = fn;
    }

    build(x, result.state, result.controls);
    result.iterations = it;
    result.residual = max_abs(f);

    const Controls& c = result.controls;
    const bool within_limits = c.throttle >= 0.0 && c.throttle <= 1.0 &&
                               std::abs(c.pitch) <= 1.0 && std::abs(c.roll) <= 1.0 &&
                               std::abs(c.yaw) <= 1.0;
    result.converged = result.residual <= 1e-6 && within_limits && is_finite(result.state);
    if (result.converged) {
        state_ = result.state;
        controls_ = result.controls;
        time_ = 0.0;
    }
    return result;
}

Telemetry Simulation::telemetry() const {
    Telemetry t;
    t.time = time_;
    t.state = state_;
    t.attitude = to_euler(state_.att);
    t.vel_ned = to_dcm(state_.att) * state_.vel_body;
    t.altitude = -state_.pos_ned.z;
    t.air = compute_air_data(state_, environment_at(state_));
    t.on_ground = in_ground_contact(state_);
    return t;
}

}  // namespace simcore
