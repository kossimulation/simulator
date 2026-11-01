function p = fixed_wing_params()
%FIXED_WING_PARAMS Parameters of the light aircraft model (Cessna 172 class).
%   Must stay in sync with FixedWingParams in native/src/fixed_wing.hpp.
%   Units: SI, radians. Frames: body FRD, world NED.

p.mass        = 1043.0;                 % kg
p.inertia     = [1285.0 1825.0 2667.0]; % Ixx Iyy Izz, kg m^2

p.wing_area   = 16.2;   % S, m^2
p.wing_span   = 10.9;   % b, m
p.mean_chord  = 1.49;   % c, m
p.max_thrust  = 2500.0; % N at sea level

p.elevator_max = deg2rad(25);
p.aileron_max  = deg2rad(15);
p.rudder_max   = deg2rad(15);

% Lift / drag / side force
p.CL0 = 0.31;  p.CL_alpha = 5.143; p.CL_q = 3.9;  p.CL_de = 0.43;
p.CD0 = 0.031; p.CD_k = 0.054;
p.CY_beta = -0.31; p.CY_dr = 0.187;
% Rolling moment
p.Cl_beta = -0.089; p.Cl_p = -0.47; p.Cl_r = 0.096; p.Cl_da = -0.178; p.Cl_dr = 0.0147;
% Pitching moment
p.Cm0 = -0.015; p.Cm_alpha = -0.89; p.Cm_q = -12.4; p.Cm_de = -1.28;
% Yawing moment
p.Cn_beta = 0.065; p.Cn_p = -0.03; p.Cn_r = -0.099; p.Cn_da = -0.053; p.Cn_dr = -0.0657;

p.gear_height = 1.2; % m
end
