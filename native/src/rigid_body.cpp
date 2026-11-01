#include "rigid_body.hpp"

namespace simcore {

StateDerivative compute_derivative(const RigidBodyState& s, const MassProperties& mp,
                                   const Wrench& w) {
    StateDerivative d;
    const Vec3& v = s.vel_body;
    const Vec3& om = s.omega_body;
    const Vec3& I = mp.inertia;

    d.pos_dot = to_dcm(s.att) * v;
    d.vel_dot = w.force / mp.mass - cross(om, v);

    const Quat omega_q{0.0, om.x, om.y, om.z};
    const Quat qd = s.att * omega_q;
    d.att_dot = {0.5 * qd.w, 0.5 * qd.x, 0.5 * qd.y, 0.5 * qd.z};

    const Vec3 h{I.x * om.x, I.y * om.y, I.z * om.z};
    const Vec3 m = w.moment - cross(om, h);
    d.omega_dot = {m.x / I.x, m.y / I.y, m.z / I.z};
    return d;
}

RigidBodyState advance(const RigidBodyState& s, const StateDerivative& d, double h) {
    RigidBodyState r;
    r.pos_ned = s.pos_ned + d.pos_dot * h;
    r.vel_body = s.vel_body + d.vel_dot * h;
    r.att = {s.att.w + d.att_dot.w * h, s.att.x + d.att_dot.x * h,
             s.att.y + d.att_dot.y * h, s.att.z + d.att_dot.z * h};
    r.omega_body = s.omega_body + d.omega_dot * h;
    return r;
}

bool is_finite(const RigidBodyState& s) {
    return is_finite(s.pos_ned) && is_finite(s.vel_body) && is_finite(s.att) &&
           is_finite(s.omega_body);
}

}  // namespace simcore
