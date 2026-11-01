function configure_codegen(model)
%CONFIGURE_CODEGEN Apply the project's Embedded Coder settings to a model.
%   configure_codegen('heli_rotor') sets a fixed-step discrete solver and
%   C++ class code generation (ert.tlc), code only, no sample main.
%   Requires Simulink Coder + Embedded Coder.

load_system(model);
dt = evalin('base', 'sim_dt');

set_param(model, 'SolverType', 'Fixed-step');
set_param(model, 'Solver', 'FixedStepDiscrete');
set_param(model, 'FixedStep', num2str(dt));

set_param(model, 'SystemTargetFile', 'ert.tlc');
set_param(model, 'TargetLang', 'C++');
set_param(model, 'CodeInterfacePackaging', 'C++ class');
set_param(model, 'GenCodeOnly', 'on');
set_param(model, 'GenerateSampleERTMain', 'off');
set_param(model, 'GenerateReport', 'on');
set_param(model, 'ERTFilePackagingFormat', 'Compact');

% Doubles everywhere to match the native core.
set_param(model, 'DefaultUnderspecifiedDataType', 'double');

fprintf('Configured %s for C++ code generation (step %g s).\n', model, dt);
end
