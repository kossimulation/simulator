function p = helicopter_params()
%HELICOPTER_PARAMS Parameters of the light helicopter model (Bo-105 class).
%   Must stay in sync with HelicopterParams in native/src/helicopter.hpp.
%   Units: SI, radians. Frames: body FRD, world NED.

p.mass    = 2200.0;                 % kg
p.inertia = [1433.0 4973.0 4099.0]; % Ixx Iyy Izz, kg m^2

p.rotor_radius    = 4.91;   % m
p.rotor_speed     = 44.4;   % rad/s (424 rpm)
p.ct_max          = 0.0082; % thrust coefficient at full collective
p.figure_of_merit = 0.7;
p.profile_power   = 60e3;   % W
p.max_disc_tilt   = 0.14;   % rad at full cyclic
p.hub_height      = 1.0;    % m above CG
p.hub_stiffness   = 1.0e5;  % N m / rad

p.tail_arm             = 6.0;    % m behind CG
p.tail_pedal_authority = 1500.0; % N at full pedal

p.fuselage_drag_area = [1.5 8.0 10.0];           % f_x f_y f_z, m^2
p.rotor_damping      = [15000.0 25000.0 9000.0]; % N m s / rad

p.skid_height = 1.0; % m
end
